using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Globalization;
using System.Text;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class DatabaseExportController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseExportController> _logger;

    public DatabaseExportController(IConfiguration config, ILogger<DatabaseExportController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("download")]
    public async Task<IActionResult> DownloadSql()
    {
        var connStr = _config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string tidak ditemukan.");

        var fileName = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
        Response.ContentType = "application/octet-stream";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        using var writer = new StreamWriter(Response.Body, new UTF8Encoding(false), leaveOpen: true);

        await writer.WriteLineAsync($"-- PostgreSQL backup generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteLineAsync("-- Restore: psql -d <target_db> -f backup.sql");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("SET client_encoding = 'UTF8';");
        await writer.WriteLineAsync("SET standard_conforming_strings = on;");
        await writer.WriteLineAsync("SET session_replication_role = replica; -- disable FK checks during import");
        await writer.WriteLineAsync();

        var tables = await GetTablesAsync(conn);

        foreach (var table in tables)
        {
            _logger.LogInformation("Exporting table: {Table}", table);

            await writer.WriteLineAsync($"-- =====================");
            await writer.WriteLineAsync($"-- Table: {table}");
            await writer.WriteLineAsync($"-- =====================");

            var columns = await GetColumnsAsync(conn, table);
            if (columns.Count == 0) continue;

            var colList = string.Join(", ", columns.Select(c => $"\"{c}\""));

            await using var dataCmd = new NpgsqlCommand($"SELECT * FROM \"{table}\"", conn);
            dataCmd.CommandTimeout = 300;
            await using var reader = await dataCmd.ExecuteReaderAsync();

            int rowCount = 0;
            while (await reader.ReadAsync())
            {
                var values = string.Join(", ", Enumerable.Range(0, reader.FieldCount)
                    .Select(i => FormatValue(reader, i)));
                await writer.WriteLineAsync($"INSERT INTO \"{table}\" ({colList}) VALUES ({values}) ON CONFLICT DO NOTHING;");
                rowCount++;
            }

            if (rowCount > 0)
                await writer.WriteLineAsync($"-- {rowCount} rows inserted");
            await writer.WriteLineAsync();
        }

        // Reset sequences using pg_catalog
        await writer.WriteLineAsync("-- =====================");
        await writer.WriteLineAsync("-- Reset sequences");
        await writer.WriteLineAsync("-- =====================");
        await using var seqCmd = new NpgsqlCommand(@"
            SELECT 'SELECT SETVAL(' || quote_literal(n.nspname || '.' || s.relname) ||
                   ', COALESCE((SELECT MAX(' || quote_ident(a.attname) || ') FROM ' ||
                   quote_ident(n.nspname) || '.' || quote_ident(t.relname) || '), 1))'
            FROM pg_class s
            JOIN pg_depend d ON d.objid = s.oid AND d.deptype = 'a'
            JOIN pg_class t ON d.refobjid = t.oid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = d.refobjsubid
            JOIN pg_namespace n ON n.oid = s.relnamespace
            WHERE s.relkind = 'S' AND n.nspname = 'public'", conn);
        await using var seqReader = await seqCmd.ExecuteReaderAsync();
        while (await seqReader.ReadAsync())
            await writer.WriteLineAsync(seqReader.GetString(0) + ";");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("SET session_replication_role = DEFAULT; -- re-enable FK checks");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"-- Backup selesai: {tables.Count} tables");

        await writer.FlushAsync();
        return new EmptyResult();
    }

    [HttpPost("import")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB max
    public async Task<IActionResult> ImportSql(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File .sql tidak boleh kosong." });

        if (!file.FileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Hanya file .sql yang diperbolehkan." });

        var connStr = _config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string tidak ditemukan.");

        string sqlContent;
        using (var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false)))
            sqlContent = await reader.ReadToEndAsync();

        var statements = SplitSqlStatements(sqlContent);
        if (statements.Count == 0)
            return BadRequest(new { message = "File SQL kosong atau tidak valid." });

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // Ambil semua kolom yang ada di DB tujuan
        var tableColumns = await GetAllTableColumnsAsync(conn);

        int success = 0, adapted = 0, skipped = 0;
        var skippedErrors = new List<string>();

        foreach (var stmt in statements)
        {
            var finalStmt = AdaptInsertStatement(stmt, tableColumns, out bool wasAdapted);
            if (finalStmt == null) continue; // kosong setelah filter, lewati

            try
            {
                await using var cmd = new NpgsqlCommand(finalStmt, conn);
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
                if (wasAdapted) adapted++; else success++;
            }
            catch (Exception ex)
            {
                skipped++;
                skippedErrors.Add(ex.Message.Split('\n')[0]);
                _logger.LogWarning("Statement gagal: {Error}", ex.Message.Split('\n')[0]);
            }
        }

        _logger.LogInformation("Import selesai: {Ok} berhasil, {Adapted} diadaptasi, {Skip} gagal", success, adapted, skipped);

        return Ok(new
        {
            message = $"Import selesai: {success + adapted} data masuk ({adapted} disesuaikan skema), {skipped} dilewati.",
            success,
            adapted,
            skipped,
            errors = skippedErrors.Distinct().Take(10).ToList()
        });
    }

    // Cek kolom INSERT vs kolom di DB tujuan, filter yang tidak ada
    private static string? AdaptInsertStatement(string stmt, Dictionary<string, HashSet<string>> tableColumns, out bool wasAdapted)
    {
        wasAdapted = false;
        var match = System.Text.RegularExpressions.Regex.Match(stmt,
            @"^INSERT\s+INTO\s+""?(\w+)""?\s*\(([^)]+)\)\s*VALUES\s*\((.+)\)(.*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success) return stmt; // bukan INSERT, eksekusi apa adanya

        var tableName = match.Groups[1].Value;
        if (!tableColumns.TryGetValue(tableName, out var targetCols)) return stmt;

        var sourceCols = match.Groups[2].Value
            .Split(',')
            .Select(c => c.Trim().Trim('"'))
            .ToList();

        var values = ParseSqlValueList(match.Groups[3].Value);
        var suffix = match.Groups[4].Value.Trim();

        if (sourceCols.Count != values.Count) return stmt;

        // Filter hanya kolom yang ada di tabel tujuan
        var pairs = sourceCols.Zip(values)
            .Where(p => targetCols.Contains(p.First))
            .ToList();

        if (pairs.Count == 0) return null; // tidak ada kolom yang cocok sama sekali
        if (pairs.Count == sourceCols.Count) return stmt; // semua cocok, tidak perlu adaptasi

        wasAdapted = true;
        var newCols = string.Join(", ", pairs.Select(p => $"\"{p.First}\""));
        var newVals = string.Join(", ", pairs.Select(p => p.Second));
        var onConflict = suffix.Length > 0 ? $" {suffix}" : " ON CONFLICT DO NOTHING";
        return $"INSERT INTO \"{tableName}\" ({newCols}) VALUES ({newVals}){onConflict}";
    }

    private static async Task<Dictionary<string, HashSet<string>>> GetAllTableColumnsAsync(NpgsqlConnection conn)
    {
        var result = new Dictionary<string, HashSet<string>>();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tbl = reader.GetString(0);
            var col = reader.GetString(1);
            if (!result.ContainsKey(tbl)) result[tbl] = new HashSet<string>();
            result[tbl].Add(col);
        }
        return result;
    }

    private static List<string> ParseSqlValueList(string valuesPart)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inString = false;
        int depth = 0;

        for (int i = 0; i < valuesPart.Length; i++)
        {
            char c = valuesPart[i];
            if (c == '\'' && !inString)
            {
                inString = true;
                current.Append(c);
            }
            else if (c == '\'' && inString)
            {
                current.Append(c);
                if (i + 1 < valuesPart.Length && valuesPart[i + 1] == '\'') { current.Append(valuesPart[++i]); }
                else inString = false;
            }
            else if (!inString && (c == '(' || c == '[')) { depth++; current.Append(c); }
            else if (!inString && (c == ')' || c == ']')) { depth--; current.Append(c); }
            else if (!inString && depth == 0 && c == ',')
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(c);
        }
        if (current.Length > 0) values.Add(current.ToString().Trim());
        return values;
    }

    private static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inSingleQuote = false;
        bool inDollarQuote = false;

        var lines = sql.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Lewati baris komentar
            if (trimmed.StartsWith("--")) continue;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (!inDollarQuote && c == '\'' && (i == 0 || line[i - 1] != '\\'))
                    inSingleQuote = !inSingleQuote;

                current.Append(c);

                if (!inSingleQuote && !inDollarQuote && c == ';')
                {
                    var stmt = current.ToString().Trim().TrimEnd(';').Trim();
                    if (!string.IsNullOrWhiteSpace(stmt))
                        statements.Add(stmt);
                    current.Clear();
                }
            }
            current.Append('\n');
        }

        // Statement terakhir tanpa titik koma
        var last = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            statements.Add(last);

        return statements;
    }

    private static async Task<List<string>> GetTablesAsync(NpgsqlConnection conn)
    {
        var tables = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<List<string>> GetColumnsAsync(NpgsqlConnection conn, string table)
    {
        var cols = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t ORDER BY ordinal_position",
            conn);
        cmd.Parameters.AddWithValue("t", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(0));
        return cols;
    }

    private static string FormatValue(NpgsqlDataReader reader, int i)
    {
        if (reader.IsDBNull(i)) return "NULL";

        var type = reader.GetFieldType(i);

        if (type == typeof(string))
        {
            var s = reader.GetString(i);
            return $"'{s.Replace("\\", "\\\\").Replace("'", "''")}'";
        }

        if (type == typeof(bool))
            return reader.GetBoolean(i) ? "TRUE" : "FALSE";

        if (type == typeof(short)) return reader.GetInt16(i).ToString();
        if (type == typeof(int)) return reader.GetInt32(i).ToString();
        if (type == typeof(long)) return reader.GetInt64(i).ToString();

        if (type == typeof(decimal))
            return reader.GetDecimal(i).ToString(CultureInfo.InvariantCulture);

        if (type == typeof(double))
            return reader.GetDouble(i).ToString(CultureInfo.InvariantCulture);

        if (type == typeof(float))
            return reader.GetFloat(i).ToString(CultureInfo.InvariantCulture);

        if (type == typeof(DateTime))
        {
            var dt = reader.GetDateTime(i);
            return $"'{dt:yyyy-MM-dd HH:mm:ss.ffffff}'";
        }

        if (type == typeof(DateTimeOffset))
        {
            var dto = reader.GetFieldValue<DateTimeOffset>(i);
            return $"'{dto:yyyy-MM-dd HH:mm:ss.ffffffzzz}'";
        }

        if (type == typeof(DateOnly))
            return $"'{reader.GetFieldValue<DateOnly>(i):yyyy-MM-dd}'";

        if (type == typeof(Guid))
            return $"'{reader.GetGuid(i)}'";

        // arrays, jsonb, and other types
        var raw = reader.GetValue(i);
        if (raw is string[] strArr)
        {
            var escaped = strArr.Select(x => x.Replace("\\", "\\\\").Replace("\"", "\\\""));
            return $"ARRAY[{string.Join(",", escaped.Select(x => $"'{x.Replace("'", "''")}'"))}]::text[]";
        }

        var str = raw?.ToString() ?? "";
        return $"'{str.Replace("\\", "\\\\").Replace("'", "''")}'";
    }
}
