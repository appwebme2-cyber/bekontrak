using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace RefineryContractAPI.Services;

/// <summary>
/// Akses database baca-saja untuk tool "query_data" chatbot. Keamanannya tidak bergantung pada
/// validasi teks query, tapi pada database itu sendiri:
/// - Query dijalankan lewat LOGIN ROLE terpisah (maestro_chat_ro) yang hanya punya hak SELECT pada
///   view di schema "chat"; tabel asli, profil user, token, log, dan konfigurasi tidak bisa dibaca.
/// - View dibuat otomatis dari daftar tabel yang diizinkan, tanpa kolom sensitif (password, token,
///   file/URL dokumen, kontak pribadi).
/// - Role diset read-only dan dibatasi waktu eksekusi; hasil dibatasi jumlah barisnya.
/// Password role diturunkan dari JWT secret (HMAC), jadi tidak perlu env var baru.
/// </summary>
public class ChatReadOnlyDb
{
    public const string RoleName = "maestro_chat_ro";
    public const int MaxRows = 200;

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

    public bool Enabled { get; private set; }
    public string SchemaDescription { get; private set; } = string.Empty;
    private string? _readOnlyConnectionString;

    public ChatReadOnlyDb(IConfiguration config)
    {
        _mainConnectionString = config.GetConnectionString("DefaultConnection");
        _jwtSecret = config["JwtSettings:SecretKey"];
    }

    /// <summary>Dipanggil sekali saat startup. Kalau gagal (mis. user DB tidak boleh membuat role), tool dimatikan.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_mainConnectionString) || string.IsNullOrEmpty(_jwtSecret))
                throw new InvalidOperationException("connection string / JWT secret kosong");

            // Hanya karakter hex, aman ditulis langsung di perintah ALTER ROLE
            var password = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(_jwtSecret), Encoding.UTF8.GetBytes("maestro-chat-readonly"))).ToLowerInvariant();

            await using var conn = new NpgsqlConnection(_mainConnectionString);
            await conn.OpenAsync();

            await ExecAsync(conn, $@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RoleName}') THEN
                        CREATE ROLE {RoleName} LOGIN;
                    END IF;
                END $$;
                ALTER ROLE {RoleName} WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{password}';
                ALTER ROLE {RoleName} SET default_transaction_read_only = on;
                ALTER ROLE {RoleName} SET statement_timeout = '8s';
                CREATE SCHEMA IF NOT EXISTS chat;
                REVOKE ALL ON SCHEMA public FROM {RoleName};
                GRANT USAGE ON SCHEMA chat TO {RoleName};");

            var schema = new StringBuilder();
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

                var allowed = columns.Where(c => !SensitiveColumn.IsMatch(c.Name.ToLowerInvariant())).ToList();
                if (allowed.Count == 0) continue;

                var select = string.Join(", ", allowed.Select(c => Quote(c.Name)));
                await ExecAsync(conn, $@"
                    DROP VIEW IF EXISTS chat.{Quote(table)};
                    CREATE VIEW chat.{Quote(table)} AS SELECT {select} FROM public.{Quote(table)};
                    GRANT SELECT ON chat.{Quote(table)} TO {RoleName};");

                schema.AppendLine($"chat.{table}({string.Join(", ", allowed.Select(c => $"{c.Name} {ShortType(c.Type)}"))})");
            }

            var builder = new NpgsqlConnectionStringBuilder(_mainConnectionString)
            {
                Username = RoleName,
                Password = password,
                SearchPath = "chat",
                CommandTimeout = 15,
                MaxPoolSize = 5,
            };
            _readOnlyConnectionString = builder.ConnectionString;

            SchemaDescription = schema.ToString().TrimEnd();
            Enabled = SchemaDescription.Length > 0;
            Console.WriteLine($"[Startup] Chatbot query_data aktif ({AllowedTables.Length} tabel diperiksa).");
        }
        catch (Exception ex)
        {
            Enabled = false;
            Console.WriteLine($"[Startup] Chatbot query_data dinonaktifkan: {ex.Message}");
        }
    }

    /// <summary>Menjalankan satu query SELECT lewat role baca-saja. Mengembalikan objek siap diserialisasi untuk tool result.</summary>
    public async Task<object> QueryAsync(string sql, CancellationToken ct)
    {
        if (!Enabled || _readOnlyConnectionString == null)
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

        await using var conn = new NpgsqlConnection(_readOnlyConnectionString);
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
