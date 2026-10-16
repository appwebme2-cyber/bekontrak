using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace RefineryContractAPI.Services;

/// <summary>
/// Akses database baca-saja untuk tool "query_data" chatbot. Keamanannya tidak bergantung pada
/// validasi teks query, tapi pada database itu sendiri:
/// - Query dijalankan lewat LOGIN ROLE terpisah yang hanya punya hak SELECT pada view di schema
///   khususnya; tabel asli, profil user, token, log, dan konfigurasi tidak bisa dibaca.
/// - Multi-RU: ada satu role per RU (maestro_chat_ro_ru2 ... _ru7, schema chat_ru2 ...) yang view-nya
///   hanya berisi data RU itu, plus role lintas RU (maestro_chat_ro, schema chat) untuk Super Admin /
///   Head Office. View dibuat dengan security_barrier supaya kondisi query (mis. cast yang memicu error)
///   tidak dievaluasi sebelum saringan RU, sehingga data RU lain tidak bisa bocor lewat pesan error.
/// - View tanpa kolom sensitif (password, token, file/URL dokumen, kontak pribadi).
/// - Role read-only, dibatasi waktu eksekusi; hasil dibatasi jumlah barisnya.
/// Password role diturunkan dari JWT secret (HMAC), jadi tidak perlu env var baru.
/// </summary>
public class ChatReadOnlyDb
{
    public const string RoleName = "maestro_chat_ro";
    public const int MaxRows = 200;
    private const string AllRuScope = "";

    private static readonly string[] AllowedTables =
    {
        "vendor", "kontrak", "amandemen_kontrak", "tagihan", "progress_lumpsum", "progress_unit_price",
        "monitoring_ltsa", "padi", "dokumen_approval", "daily_report", "direksi_pekerjaan", "program_kerja",
        "planner", "sla_setting", "sla_tagihan", "rab_item", "material_requirement_draft", "material_requirement_line",
    };

    // Kolom yang tidak boleh terbaca chatbot (dicocokkan ke nama kolom, huruf kecil)
    private static readonly Regex SensitiveColumn = new(
        @"password|hash|token|secret|documents$|^dokumen_|evidence|file|url|path|s_curve|email|telp|telepon|phone|whatsapp|npwp|alamat|^pic_",
        RegexOptions.Compiled);

    private readonly string? _mainConnectionString;
    private readonly string? _jwtSecret;

    // Koneksi baca-saja per cakupan: "" = semua RU, "RU2".."RU7" = satu RU
    private readonly Dictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled { get; private set; }
    public string SchemaDescription { get; private set; } = string.Empty;

    public ChatReadOnlyDb(IConfiguration config)
    {
        _mainConnectionString = config.GetConnectionString("DefaultConnection");
        _jwtSecret = config["JwtSettings:SecretKey"];
    }

    private static IEnumerable<string> Scopes =>
        new[] { AllRuScope }.Concat(CurrentRu.ValidRus.Select(r => r.ToUpperInvariant()).OrderBy(r => r));

    private static string RoleFor(string scope) => scope == AllRuScope ? RoleName : $"{RoleName}_{scope.ToLowerInvariant()}";
    private static string SchemaFor(string scope) => scope == AllRuScope ? "chat" : $"chat_{scope.ToLowerInvariant()}";

    /// <summary>Dipanggil sekali saat startup. Kalau gagal (mis. user DB tidak boleh membuat role), tool dimatikan.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_mainConnectionString) || string.IsNullOrEmpty(_jwtSecret))
                throw new InvalidOperationException("connection string / JWT secret kosong");

            await using var conn = new NpgsqlConnection(_mainConnectionString);
            await conn.OpenAsync();

            // Kolom tiap tabel (dibaca sekali, dipakai untuk semua cakupan)
            var tableColumns = new Dictionary<string, List<(string Name, string Type)>>();
            foreach (var table in AllowedTables)
            {
                var columns = new List<(string Name, string Type)>();
                await using (var cmd = new NpgsqlCommand(
                    "SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t ORDER BY ordinal_position", conn))
                {
                    cmd.Parameters.AddWithValue("t", table);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        columns.Add((reader.GetString(0), reader.GetString(1)));
                }
                tableColumns[table] = columns;
            }

            foreach (var scope in Scopes)
            {
                var role = RoleFor(scope);
                var schemaName = SchemaFor(scope);
                // Hanya karakter hex, aman ditulis langsung di perintah ALTER ROLE.
                // Role lintas RU memakai turunan yang sama seperti sebelum multi-RU.
                var salt = scope == AllRuScope ? "maestro-chat-readonly" : $"maestro-chat-readonly-{scope.ToLowerInvariant()}";
                var password = Convert.ToHexString(
                    HMACSHA256.HashData(Encoding.UTF8.GetBytes(_jwtSecret), Encoding.UTF8.GetBytes(salt))).ToLowerInvariant();

                await ExecAsync(conn, $@"
                    DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                            CREATE ROLE {role} LOGIN;
                        END IF;
                    END $$;
                    ALTER ROLE {role} WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{password}';
                    ALTER ROLE {role} SET default_transaction_read_only = on;
                    ALTER ROLE {role} SET statement_timeout = '8s';
                    CREATE SCHEMA IF NOT EXISTS {schemaName};
                    REVOKE ALL ON SCHEMA public FROM {role};
                    GRANT USAGE ON SCHEMA {schemaName} TO {role};");

                foreach (var table in AllowedTables)
                {
                    var columns = tableColumns[table];
                    var allowed = columns.Where(c => !SensitiveColumn.IsMatch(c.Name.ToLowerInvariant())).ToList();
                    if (allowed.Count == 0) continue;

                    var select = string.Join(", ", allowed.Select(c => Quote(c.Name)));
                    var where = RuWhere(scope, columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
                    await ExecAsync(conn, $@"
                        DROP VIEW IF EXISTS {schemaName}.{Quote(table)};
                        CREATE VIEW {schemaName}.{Quote(table)} WITH (security_barrier = true) AS SELECT {select} FROM public.{Quote(table)}{where};
                        GRANT SELECT ON {schemaName}.{Quote(table)} TO {role};");
                }

                var builder = new NpgsqlConnectionStringBuilder(_mainConnectionString)
                {
                    Username = role,
                    Password = password,
                    SearchPath = schemaName,
                    CommandTimeout = 15,
                    MaxPoolSize = 5,
                };
                _connectionStrings[scope] = builder.ConnectionString;
            }

            // Deskripsi tanpa nama schema: tiap role memakai search_path schema-nya sendiri
            var schema = new StringBuilder();
            foreach (var table in AllowedTables)
            {
                var allowed = tableColumns[table].Where(c => !SensitiveColumn.IsMatch(c.Name.ToLowerInvariant())).ToList();
                if (allowed.Count == 0) continue;
                schema.AppendLine($"{table}({string.Join(", ", allowed.Select(c => $"{c.Name} {ShortType(c.Type)}"))})");
            }

            SchemaDescription = schema.ToString().TrimEnd();
            Enabled = SchemaDescription.Length > 0;
            Console.WriteLine($"[Startup] Chatbot query_data aktif ({AllowedTables.Length} tabel, {_connectionStrings.Count} cakupan RU).");
        }
        catch (Exception ex)
        {
            Enabled = false;
            Console.WriteLine($"[Startup] Chatbot query_data dinonaktifkan: {ex.Message}");
        }
    }

    /// <summary>Saringan baris untuk view satu RU. Vendor & pengaturan global dipakai bersama (tanpa saringan).</summary>
    private static string RuWhere(string scope, HashSet<string> columnNames)
    {
        if (scope == AllRuScope) return string.Empty;
        var ru = $"'{scope}'"; // scope selalu salah satu kode RU yang valid
        if (columnNames.Contains("id_ru")) return $" WHERE id_ru = {ru}";
        if (columnNames.Contains("id_kontrak")) return $" WHERE id_kontrak IN (SELECT id_kontrak FROM public.kontrak WHERE id_ru = {ru})";
        if (columnNames.Contains("id_draft")) return $" WHERE id_draft IN (SELECT id_draft FROM public.material_requirement_draft WHERE id_ru = {ru})";
        return string.Empty;
    }

    /// <summary>
    /// Menjalankan satu query SELECT lewat role baca-saja sesuai cakupan RU.
    /// ruFilter null = semua RU (Super Admin / Head Office); selain itu hanya data RU tersebut.
    /// </summary>
    public async Task<object> QueryAsync(string sql, string? ruFilter, CancellationToken ct)
    {
        var scope = string.IsNullOrEmpty(ruFilter) ? AllRuScope : ruFilter.ToUpperInvariant();
        if (!Enabled || !_connectionStrings.TryGetValue(scope, out var connectionString))
            return new { error = "Query data tidak tersedia." };

        var query = (sql ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (query.Length == 0 || query.Length > 6000)
            return new { error = "Query kosong atau terlalu panjang." };
        if (query.Contains(';') || query.Contains("--") || query.Contains("/*"))
            return new { error = "Hanya satu perintah SELECT tanpa titik koma atau komentar." };
        if (!Regex.IsMatch(query, @"^(select|with)\b", RegexOptions.IgnoreCase))
            return new { error = "Query harus diawali SELECT atau WITH." };

        // Dibungkus subquery: memaksa satu pernyataan SELECT dan membatasi jumlah baris
        var wrapped = $"SELECT * FROM ({query}) AS hasil LIMIT {MaxRows + 1}";

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            await ro.ExecuteNonQueryAsync(ct);

        try
        {
            await using var cmd = new NpgsqlCommand(wrapped, conn, tx);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<object?[]>();
            var truncated = false;
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count == MaxRows) { truncated = true; break; }
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : ToJsonValue(reader.GetValue(i));
                rows.Add(row);
            }

            return new
            {
                kolom = columns,
                jumlah_baris = rows.Count,
                terpotong = truncated,
                catatan = truncated ? $"Hasil dibatasi {MaxRows} baris; pakai agregasi (COUNT/SUM/GROUP BY) atau filter." : null,
                baris = rows,
            };
        }
        catch (PostgresException ex)
        {
            // Pesan error SQL dikembalikan ke AI supaya bisa memperbaiki query-nya
            return new { error = $"Query gagal: {ex.MessageText}" };
        }
        finally
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { /* koneksi sudah ditutup */ }
        }
    }

    private static object ToJsonValue(object value) => value switch
    {
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal or double or float or int or long or short or bool or string => value,
        _ => value.ToString() ?? string.Empty,
    };

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string ShortType(string dataType) => dataType switch
    {
        "character varying" or "text" => "text",
        "timestamp without time zone" or "timestamp with time zone" => "timestamp",
        "double precision" or "real" => "float",
        _ => dataType,
    };

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
