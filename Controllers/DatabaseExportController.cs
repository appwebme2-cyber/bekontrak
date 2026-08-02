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

        // Hapus komentar baris tunggal sebelum parsing
        var lines = sqlContent.Split('\n')
            .Select(l => l.Contains("--") ? l[..l.IndexOf("--")] : l);
        var cleanSql = string.Join('\n', lines).Trim();

        if (string.IsNullOrWhiteSpace(cleanSql))
            return BadRequest(new { message = "File SQL kosong atau hanya berisi komentar." });

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(cleanSql, conn, tx);
            cmd.CommandTimeout = 600;
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();

            _logger.LogInformation("SQL import berhasil: {File} ({Size} bytes)", file.FileName, file.Length);
            return Ok(new { message = $"Import berhasil. File: {file.FileName} ({file.Length / 1024} KB)" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "SQL import gagal: {File}", file.FileName);
            return StatusCode(500, new { message = $"Import gagal: {ex.Message}" });
        }
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
