using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Services;

public record ChatHistoryItem(string Role, string Content);

/// <summary>Halaman yang sedang dibuka user saat bertanya (mis. detail kontrak tertentu).</summary>
public record ChatPageContext(string? Path, string? PageTitle, string? KontrakId, string? TagihanId);

/// <summary>Jawaban chatbot: JSON {"blocks":[...]} yang dirender frontend sebagai teks/tabel/chart/link.</summary>
public record ChatAnswer(JsonElement Answer, string Raw);

/// <summary>
/// Chatbot MAESTRO (Tahap 1). Claude menjawab pertanyaan user dengan memanggil "tool" baca-saja di
/// bawah untuk mengambil data kontrak, amandemen, progress, dan tagihan dari database. Tidak ada tool
/// yang bisa mengubah data. Data dimuat lewat proyeksi sederhana lalu disaring di memori supaya tidak
/// bergantung pada terjemahan query EF yang rumit.
/// </summary>
public class ChatService
{
    private const int MaxToolRounds = 8;
    private const int MaxRows = 200;
    private const int MaxToolResultChars = 120_000;

    private readonly AppDbContext _db;
    private readonly ChatReadOnlyDb _readOnlyDb;
    private readonly ICurrentRu _currentRu;
    private readonly AiUsageTracker _usage;
    private readonly string? _apiKey;
    private readonly string _model;

    private List<KontrakRow>? _kontrakCache;
    private List<TagihanRow>? _tagihanCache;

    public ChatService(AppDbContext db, ChatReadOnlyDb readOnlyDb, AiUsageTracker usage, ICurrentRu currentRu, IConfiguration config)
    {
        _db = db;
        _readOnlyDb = readOnlyDb;
        _currentRu = currentRu;
        _usage = usage;
        _apiKey = config["Anthropic:ApiKey"];
        _model = string.IsNullOrWhiteSpace(config["Anthropic:ChatModel"]) ? "claude-opus-5-5" : config["Anthropic:ChatModel"]!;
    }

    // ===================================================================== PROMPT

    private const string SystemPrompt = @"Anda adalah Asisten MAESTRO, chatbot internal aplikasi MAESTRO (Maintenance Contract Supervisory & Control Tools) milik Pertamina Patra Niaga untuk memantau kontrak pemeliharaan kilang. Anda menjawab pertanyaan staf internal dalam Bahasa Indonesia yang ringkas, jelas, dan sopan.

## Aturan data
- Untuk SETIAP pertanyaan tentang data (kontrak, vendor, nilai, tanggal, progress, amandemen, tagihan), WAJIB ambil data lewat tool. Jangan pernah mengarang angka, nama, atau tanggal.
- Kalau tool tidak mengembalikan data yang cocok, katakan terus terang bahwa datanya tidak ditemukan, dan sarankan kata kunci lain bila relevan.
- Utamakan tool khusus (cari_kontrak, ringkasan_kontrak, detail_kontrak, cari_tagihan, ringkasan_tagihan, cari_amandemen) bila cukup. Untuk data lain (laporan harian, PADI/user purchase, approval dokumen, kebutuhan material & RAB, SLA, master direksi/program kerja/planner) atau analisis yang tidak bisa dijawab tool khusus, pakai query_data (SQL SELECT PostgreSQL). Kalau query gagal, baca pesan errornya lalu perbaiki query.
- Kalau query_data tidak tersedia dan tool khusus tidak mencakup pertanyaannya, katakan bahwa data itu belum bisa diakses chatbot dan arahkan ke menunya.
- Data akun pengguna (nama, email, password, log akses) TIDAK boleh dan tidak bisa diakses.
- Kalau ada keterangan [Konteks halaman], pertanyaan seperti ""kontrak ini"" atau ""tagihan ini"" merujuk ke data di konteks tersebut.
- Anda hanya bisa MEMBACA data. Kalau user minta menambah/mengubah/menghapus data, jelaskan bahwa chatbot tidak bisa melakukannya dan arahkan ke menu yang tepat.
- Sebutkan dasar data secara singkat (mis. ""berdasarkan 42 kontrak berstatus Aktif"").
- Pertanyaan yang ambigu: pilih tafsiran paling wajar, sebutkan asumsi Anda dalam satu kalimat.
- Hitungan (jumlah, total, rata-rata) ambil dari hasil tool; tool ringkasan sudah menghitungkan totalnya.

## Format jawaban
Jawab HANYA sebagai JSON sesuai schema: {""blocks"": [...]}. Jenis blok:
- text: paragraf/poin. Boleh **tebal** dan daftar dengan awalan ""- "". Singkat dan langsung ke inti.
- table: untuk daftar lebih dari 3 item atau perbandingan. Isi title, columns (judul kolom), rows (array baris, tiap sel berupa string). Maksimal 50 baris; kalau lebih, sebutkan totalnya di blok text.
- chart: untuk tren waktu (chartType ""line"" atau ""bar""), perbandingan antar kelompok (""bar""), atau komposisi dengan sedikit kategori (""pie"", maksimal 6 kategori, satu seri). Isi title, labels (sumbu X/kategori), series [{name, values}] dengan jumlah values sama dengan jumlah labels, dan unit (mis. ""Rp"", ""%"", ""kontrak""). Nilai berupa angka mentah (bukan teks).
- link: tautan ke halaman aplikasi, isi href (path diawali ""/"", mis. ""/contracts/{id}"") dan label.
Panduan: mulai dengan blok text berisi jawaban inti. Tambahkan table/chart hanya kalau membantu. Untuk satu kontrak spesifik, sertakan link ke detailnya. Jangan pakai tabel markdown di dalam blok text.
Format angka: Rupiah pakai titik ribuan (Rp 1.250.000.000); di teks boleh disingkat (Rp 1,25 M / Rp 850 jt). Tanggal format ""12 Okt 2026"". Persen satu desimal.

## Pengetahuan aplikasi MAESTRO
Menu utama (tampil sesuai hak akses role):
- Executive Dashboard (/dashboard): ringkasan kontrak, nilai, progress, dan status.
- Performance Monitoring (/contract-performance): pemantauan kinerja & SLA kontrak.
- Favorit (/favorites): daftar kontrak yang ditandai bintang oleh user (pribadi per user).
- Kontrak Lumpsum (/kontrak-lumpsum), Kontrak Unit Price (/kontrak-unit-price), Kontrak TSA/LTSA (/kontrak-tsa-ltsa): daftar kontrak per tipe. Detail kontrak di /contracts/{id}.
- Amandemen (/amandemen): perubahan kontrak (nilai/durasi/tanggal). Tanggal mulai/selesai kontrak otomatis mengikuti amandemen terakhir.
- Tagihan (/invoices): pengajuan tagihan vendor; detail di /invoices/{id}. Alur status umum: Pengajuan → Punchlist → BAST/BAPP → Payment/Selesai.
- User Purchase/PADI (/user-purchase), Approval Dokumen (/approval), Laporan Harian (/laporan-harian).
- Kebutuhan Material & Pekerjaan (/material-requirement): menyusun draft kebutuhan material & pekerjaan dari Rekomendasi/MRF dan gambar kerja, dicocokkan dengan RAB kontrak; ada fitur ""Auto Ekstrak & Generate dengan AI"" (perlu PIN) dan Export Excel.
- Menu khusus admin: Vendor, Pengguna, Manajemen Data, Pengaturan Admin, Pengaturan Role, Report Akses.
Status kontrak: Pre-KOM (belum Kick Off Meeting), Aktif/Active, Selesai/Completed, Terminated. Kontrak dianggap ""Done"" bila statusnya selesai atau progress aktual sudah 100%; kontrak terlambat = belum Done dan tanggal selesai (setelah amandemen) sudah lewat.
Role: Admin (akses penuh), Manager/Section Head/Supervisor/Technician (staf: tambah/ubah data), Head Office dan Guest (hanya melihat), External (vendor, hanya kontraknya sendiri). Hak akses tiap role bisa diatur admin di Pengaturan Role.
Kalau ditanya langkah teknis penggunaan menu yang tidak Anda ketahui pasti, jelaskan secara umum dan sarankan menghubungi admin; jangan mengarang tombol atau langkah.";

    private static readonly Dictionary<string, JsonElement> AnswerSchema = BuildAnswerSchema();

    private static Dictionary<string, JsonElement> BuildAnswerSchema()
    {
        var block = new
        {
            type = "object",
            properties = new
            {
                type = new { type = "string", @enum = new[] { "text", "table", "chart", "link" } },
                text = new { type = "string" },
                title = new { type = "string" },
                columns = new { type = "array", items = new { type = "string" } },
                rows = new { type = "array", items = new { type = "array", items = new { type = "string" } } },
                chartType = new { type = "string", @enum = new[] { "bar", "line", "pie" } },
                labels = new { type = "array", items = new { type = "string" } },
                series = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            values = new { type = "array", items = new { type = "number" } },
                        },
                        required = new[] { "name", "values" },
                        additionalProperties = false,
                    },
                },
                unit = new { type = "string" },
                href = new { type = "string" },
                label = new { type = "string" },
            },
            required = new[] { "type" },
            additionalProperties = false,
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new { blocks = new { type = "array", items = block } }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "blocks" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }

    // ===================================================================== TOOLS

    private static JsonElement P(object schema) => JsonSerializer.SerializeToElement(schema);

    private static readonly object StatusKontrakParam = new
    {
        type = "string",
        @enum = new[] { "pre-kom", "aktif", "selesai", "terminated" },
        description = "Kelompok status kontrak (Aktif mencakup Active; Selesai mencakup Completed)",
    };

    private static readonly List<ToolUnion> Tools = new()
    {
        new Tool
        {
            Name = "cari_kontrak",
            Description = "Cari dan saring daftar kontrak. Mengembalikan ringkasan per kontrak (id, judul, tipe, status, vendor, tanggal mulai/selesai setelah amandemen, sisa hari, nilai kontrak terkini, progress plan/actual, terlambat). Semua parameter opsional; tanpa parameter mengembalikan kontrak terbaru.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["kata_kunci"] = P(new { type = "string", description = "Cari di judul, nomor dokumen, nomor PO/PR, nomor IRKAP, atau nama vendor" }),
                    ["status"] = P(StatusKontrakParam),
                    ["tipe"] = P(new { type = "string", description = "Tipe kontrak: Lumpsum, Unit Price, TSA, LTSA" }),
                    ["vendor"] = P(new { type = "string", description = "Sebagian nama vendor" }),
                    ["direksi_pekerjaan"] = P(new { type = "string", description = "Sebagian nama direksi pekerjaan/bagian" }),
                    ["tahun_mulai"] = P(new { type = "integer", description = "Tahun tanggal mulai kontrak" }),
                    ["berakhir_dalam_hari"] = P(new { type = "integer", description = "Hanya kontrak belum Done yang tanggal selesainya dalam N hari ke depan" }),
                    ["hanya_terlambat"] = P(new { type = "boolean", description = "Hanya kontrak yang belum Done dan sudah melewati tanggal selesai" }),
                    ["deviasi_minimal"] = P(new { type = "number", description = "Hanya kontrak dengan (progress plan - progress actual) minimal sekian persen" }),
                    ["urutkan"] = P(new { type = "string", @enum = new[] { "terbaru", "nilai_tertinggi", "tanggal_selesai", "deviasi_terbesar" } }),
                    ["batas"] = P(new { type = "integer", description = "Jumlah maksimum kontrak yang dikembalikan (default 50, maks 200)" }),
                },
            },
        },
        new Tool
        {
            Name = "ringkasan_kontrak",
            Description = "Statistik kontrak yang dikelompokkan: jumlah kontrak, total nilai, rata-rata progress actual, dan jumlah terlambat per kelompok. Pakai untuk pertanyaan agregat/komposisi.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["kelompokkan_berdasarkan"] = P(new { type = "string", @enum = new[] { "status", "tipe", "vendor", "direksi_pekerjaan", "tahun_mulai", "disiplin", "program_kerja" } }),
                    ["status"] = P(StatusKontrakParam),
                    ["tipe"] = P(new { type = "string" }),
                    ["tahun_mulai"] = P(new { type = "integer" }),
                },
                Required = ["kelompokkan_berdasarkan"],
            },
        },
        new Tool
        {
            Name = "detail_kontrak",
            Description = "Detail lengkap satu kontrak: data kontrak, daftar amandemen, progress (milestone lumpsum, item unit price, monitoring LTSA), serta ringkasan dan daftar tagihannya. Isi id_kontrak bila sudah diketahui; kalau belum, isi kata_kunci (bila ada beberapa yang cocok, akan dikembalikan daftar kandidat).",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["id_kontrak"] = P(new { type = "string" }),
                    ["kata_kunci"] = P(new { type = "string", description = "Judul/nomor dokumen/nomor PO/vendor" }),
                },
            },
        },
        new Tool
        {
            Name = "cari_tagihan",
            Description = "Cari dan saring daftar tagihan (invoice) vendor. Mengembalikan nomor, tanggal, kontrak, vendor, tipe kontrak, termin, nilai, status.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["id_kontrak"] = P(new { type = "string" }),
                    ["kata_kunci"] = P(new { type = "string", description = "Cari di nomor tagihan, judul kontrak, atau nama vendor" }),
                    ["status"] = P(new { type = "string", description = "Sebagian teks status tagihan, mis. Pengajuan, Punchlist, BAST, Payment" }),
                    ["tipe_kontrak"] = P(new { type = "string" }),
                    ["dari_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd, tanggal tagihan minimal" }),
                    ["sampai_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd, tanggal tagihan maksimal" }),
                    ["urutkan"] = P(new { type = "string", @enum = new[] { "terbaru", "nilai_tertinggi" } }),
                    ["batas"] = P(new { type = "integer", description = "Default 50, maks 200" }),
                },
            },
        },
        new Tool
        {
            Name = "ringkasan_tagihan",
            Description = "Statistik tagihan yang dikelompokkan: jumlah dan total nilai per kelompok. Pakai untuk tren per bulan/tahun, komposisi status, atau peringkat vendor/kontrak.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["kelompokkan_berdasarkan"] = P(new { type = "string", @enum = new[] { "bulan", "tahun", "status", "tipe_kontrak", "vendor", "kontrak" } }),
                    ["id_kontrak"] = P(new { type = "string" }),
                    ["status"] = P(new { type = "string" }),
                    ["tipe_kontrak"] = P(new { type = "string" }),
                    ["dari_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd" }),
                    ["sampai_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd" }),
                },
                Required = ["kelompokkan_berdasarkan"],
            },
        },
        new Tool
        {
            Name = "cari_amandemen",
            Description = "Daftar amandemen kontrak beserta kontraknya: nomor, tanggal, jenis, nilai kontrak baru, durasi, tanggal mulai/selesai baru, alasan.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["id_kontrak"] = P(new { type = "string" }),
                    ["kata_kunci"] = P(new { type = "string", description = "Cari di nomor amandemen, judul kontrak, vendor, atau alasan" }),
                    ["jenis"] = P(new { type = "string" }),
                    ["dari_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd, tanggal amandemen minimal" }),
                    ["sampai_tanggal"] = P(new { type = "string", description = "yyyy-MM-dd" }),
                    ["batas"] = P(new { type = "integer", description = "Default 50, maks 200" }),
                },
            },
        },
    };

    private List<ToolUnion> BuildTools()
    {
        var tools = new List<ToolUnion>(Tools);
        if (_readOnlyDb.Enabled)
        {
            tools.Add(new Tool
            {
                Name = "query_data",
                Description =
                    "Jalankan SATU query SQL SELECT (PostgreSQL, boleh WITH/JOIN/GROUP BY) yang hanya bisa membaca view berikut (tulis nama tabel langsung, tanpa nama schema):\n" +
                    _readOnlyDb.SchemaDescription +
                    "\n\nCatatan: status kontrak bervariasi (Aktif/Active, Selesai/Completed) - pakai ILIKE atau IN. Kolom *_baru di kontrak berisi nilai/tanggal hasil amandemen terakhir; pakai COALESCE(kolom_baru, kolom_awal) untuk nilai terkini. Hasil maksimal " + ChatReadOnlyDb.MaxRows +
                    " baris - utamakan agregasi (COUNT/SUM/AVG + GROUP BY) dan LIMIT. Tanpa titik koma dan komentar.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["sql"] = P(new { type = "string", description = "Query SELECT" }),
                    },
                    Required = ["sql"],
                },
            });
        }
        return tools;
    }

    private static string? DescribeContext(ChatPageContext? ctx)
    {
        if (ctx == null || string.IsNullOrWhiteSpace(ctx.Path)) return null;
        // Nilai dari frontend: buang baris baru/kurung siku dan batasi panjang supaya tidak bisa menyamar sebagai instruksi
        static string Clean(string? value, int max)
        {
            var c = (value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Replace('[', '(').Replace(']', ')').Trim();
            return c.Length > max ? c[..max] : c;
        }

        var parts = new List<string> { $"user sedang membuka halaman {Clean(ctx.Path, 120)}" };
        if (!string.IsNullOrWhiteSpace(ctx.PageTitle)) parts.Add($"judul halaman: {Clean(ctx.PageTitle, 120)}");
        if (!string.IsNullOrWhiteSpace(ctx.KontrakId)) parts.Add($"id_kontrak yang dibuka: {Clean(ctx.KontrakId, 64)}");
        if (!string.IsNullOrWhiteSpace(ctx.TagihanId)) parts.Add($"id_tagihan yang dibuka: {Clean(ctx.TagihanId, 64)}");
        return $"[Konteks halaman: {string.Join("; ", parts)}]";
    }

    // ===================================================================== LOOP

    /// <param name="onStatus">Dipanggil dengan keterangan langkah yang sedang berjalan (untuk progress di layar).</param>
    public async Task<ChatAnswer> AskAsync(string question, List<ChatHistoryItem> history, ChatPageContext? context,
        Func<string, Task>? onStatus, CancellationToken ct)
    {
        async Task Status(string text)
        {
            if (onStatus != null) await onStatus(text);
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Anthropic API key belum dikonfigurasi. Hubungi admin untuk mengatur environment variable Anthropic__ApiKey.");

        var client = new AnthropicClient { ApiKey = _apiKey };
        var contextNote = DescribeContext(context);
        var messages = BuildMessages(history, contextNote == null ? question : $"{contextNote}\n\n{question}");
        var tools = BuildTools();

        // Blok pertama (instruksi tetap) di-cache; tanggal hari ini di blok terpisah supaya cache tidak pecah.
        var system = new List<TextBlockParam>
        {
            new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() },
            new() { Text = $"Tanggal hari ini: {Today:yyyy-MM-dd} (WIB)." },
        };

        await Status("Memahami pertanyaan...");
        var requestId = Guid.NewGuid().ToString();

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 16000,
                System = system,
                Tools = tools,
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new JsonOutputFormat { Schema = AnswerSchema },
                },
                Messages = messages,
            });
            await _usage.RecordAsync(requestId, "chatbot", _model,
                response.Usage.InputTokens, response.Usage.OutputTokens,
                response.Usage.CacheReadInputTokens, response.Usage.CacheCreationInputTokens);

            if (response.StopReason == "refusal")
                return TextAnswer("Maaf, pertanyaan ini tidak dapat saya proses. Coba ajukan dengan kalimat lain.");
            if (response.StopReason == "max_tokens")
                return TextAnswer("Maaf, jawabannya terlalu panjang untuk ditampilkan. Coba persempit pertanyaannya (mis. per tahun, per tipe, atau per vendor).");

            var assistantContent = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();
            string? finalText = null;

            foreach (ContentBlock block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                    finalText = text.Text;
                }
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                {
                    assistantContent.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                {
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });

                    string result;
                    var isError = false;
                    try
                    {
                        await Status(DescribeTool(toolUse.Name, toolUse.Input));
                        result = await RunToolAsync(toolUse.Name, toolUse.Input, ct);
                    }
                    catch (Exception ex)
                    {
                        result = $"Gagal menjalankan tool: {ex.Message}";
                        isError = true;
                    }
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = result, IsError = isError });
                }
            }

            if (toolResults.Count == 0)
                return ParseAnswer(finalText);

            if (round == MaxToolRounds)
                break;

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
            await Status("Menyusun jawaban...");
        }

        return TextAnswer("Maaf, pertanyaan ini butuh terlalu banyak langkah pengambilan data. Coba pecah menjadi beberapa pertanyaan yang lebih spesifik.");
    }

    /// <summary>Riwayat dari frontend dirapikan supaya selalu bergantian user/assistant dan diawali user.</summary>
    private static List<MessageParam> BuildMessages(List<ChatHistoryItem> history, string question)
    {
        var messages = new List<MessageParam>();
        string? lastRole = null;
        foreach (var h in history.TakeLast(12))
        {
            var role = h.Role == "assistant" ? "assistant" : "user";
            if (string.IsNullOrWhiteSpace(h.Content) || role == lastRole) continue;
            if (lastRole == null && role != "user") continue;

            var content = h.Content.Length > 8000 ? h.Content[..8000] : h.Content;
            messages.Add(new MessageParam { Role = role == "assistant" ? Role.Assistant : Role.User, Content = content });
            lastRole = role;
        }
        if (lastRole == "user") messages.RemoveAt(messages.Count - 1);

        messages.Add(new MessageParam { Role = Role.User, Content = question });
        return messages;
    }

    private static ChatAnswer ParseAnswer(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
                    return new ChatAnswer(doc.RootElement.Clone(), text);
            }
            catch (JsonException)
            {
                // bukan JSON: tampilkan apa adanya sebagai teks
            }
            return TextAnswer(text);
        }
        return TextAnswer("Maaf, saya tidak mendapatkan jawaban. Silakan coba lagi.");
    }

    private static ChatAnswer TextAnswer(string text)
    {
        var raw = JsonSerializer.Serialize(new { blocks = new[] { new { type = "text", text } } });
        using var doc = JsonDocument.Parse(raw);
        return new ChatAnswer(doc.RootElement.Clone(), raw);
    }

    /// <summary>Keterangan singkat langkah tool untuk ditampilkan ke user selama menunggu.</summary>
    private static string DescribeTool(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        var by = Str(input, "kelompokkan_berdasarkan")?.Replace('_', ' ');
        var keyword = Str(input, "kata_kunci");
        var suffix = keyword != null ? $" \"{(keyword.Length > 40 ? keyword[..40] : keyword)}\"" : string.Empty;
        return name switch
        {
            "cari_kontrak" => $"Mencari kontrak{suffix}...",
            "ringkasan_kontrak" => $"Menghitung statistik kontrak per {by ?? "kelompok"}...",
            "detail_kontrak" => $"Membuka detail kontrak{suffix}...",
            "cari_tagihan" => $"Mencari tagihan{suffix}...",
            "ringkasan_tagihan" => $"Menghitung statistik tagihan per {by ?? "kelompok"}...",
            "cari_amandemen" => $"Mencari amandemen{suffix}...",
            "query_data" => "Menjalankan query data...",
            _ => "Mengambil data...",
        };
    }

    // ===================================================================== TOOL IMPLEMENTATIONS

    private static DateTime Today => DateTime.UtcNow.AddHours(7).Date;

    private static readonly JsonSerializerOptions ToolJson = new() { WriteIndented = false };

    private async Task<string> RunToolAsync(string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        object result = name switch
        {
            "cari_kontrak" => await CariKontrakAsync(input, ct),
            "ringkasan_kontrak" => await RingkasanKontrakAsync(input, ct),
            "detail_kontrak" => await DetailKontrakAsync(input, ct),
            "cari_tagihan" => await CariTagihanAsync(input, ct),
            "ringkasan_tagihan" => await RingkasanTagihanAsync(input, ct),
            "cari_amandemen" => await CariAmandemenAsync(input, ct),
            // Multi-RU: query bebas hanya membaca data RU pengguna (Super Admin/Head Office: semua RU)
            "query_data" => await _readOnlyDb.QueryAsync(Str(input, "sql") ?? string.Empty, _currentRu.Filter, ct),
            _ => new { error = $"Tool tidak dikenal: {name}" },
        };

        var json = JsonSerializer.Serialize(result, ToolJson);
        return json.Length > MaxToolResultChars
            ? json[..MaxToolResultChars] + " ... [hasil dipotong, persempit filter]"
            : json;
    }

    // ---------- kontrak

    private class KontrakRow
    {
        public string Id = "";
        public string Judul = "";
        public string? NoDokumen, NoPoPr, NoIrkap, Tipe, Status, Vendor, Direksi, ProgramKerja, Planner, Disiplin, KboBagian;
        public string? AktivitasSaatIni, Kendala;
        public DateTime? TanggalMulai, TanggalSelesai, TanggalKom;
        public decimal? NilaiAwal, Nilai;
        public double? ProgressPlan, ProgressActual;
        public bool HasAmendment;
        public DateTime CreatedAt;

        public string StatusGroup => NormalizeStatus(Status);
        public bool Done => StatusGroup == "selesai" || (ProgressActual ?? 0) >= 100;
        public int? SisaHari => TanggalSelesai.HasValue ? (int)(TanggalSelesai.Value.Date - Today).TotalDays : null;
        public bool Terlambat => !Done && StatusGroup != "terminated" && TanggalSelesai.HasValue && TanggalSelesai.Value.Date < Today;
        public double? Deviasi => ProgressPlan.HasValue && ProgressActual.HasValue ? ProgressPlan - ProgressActual : null;
    }

    private static string NormalizeStatus(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        if (s is "active" or "aktif") return "aktif";
        if (s is "completed" or "selesai") return "selesai";
        if (s.StartsWith("pre")) return "pre-kom";
        if (s.StartsWith("termin")) return "terminated";
        return s;
    }

    private async Task<List<KontrakRow>> LoadKontrakAsync(CancellationToken ct)
    {
        if (_kontrakCache != null) return _kontrakCache;

        var raw = await _db.Kontraks.AsNoTracking()
            .Select(k => new
            {
                k.IdKontrak, k.JudulKontrak, k.NoDokumenKontrak, k.NoPoPr, k.NoIrkap, k.TipeKontrak, k.StatusKontrak,
                VendorNama = k.Vendor != null ? k.Vendor.NamaVendor : null,
                k.DireksiPekerjaan, k.ProgramKerja, k.Planner, k.Disiplin, k.KboBagian, k.AktivitasSaatIni, k.Kendala,
                k.TanggalMulai, k.TanggalSelesai, k.TanggalMulaiBaru, k.TanggalSelesaiBaru, k.TanggalKom,
                k.NilaiAwal, k.NilaiKontrakBaru, k.ProgressPlan, k.ProgressActual, k.HasAmendment, k.CreatedAt,
            })
            .ToListAsync(ct);

        _kontrakCache = raw.Select(k => new KontrakRow
        {
            Id = k.IdKontrak, Judul = k.JudulKontrak, NoDokumen = k.NoDokumenKontrak, NoPoPr = k.NoPoPr, NoIrkap = k.NoIrkap,
            Tipe = k.TipeKontrak, Status = k.StatusKontrak, Vendor = k.VendorNama, Direksi = k.DireksiPekerjaan,
            ProgramKerja = k.ProgramKerja, Planner = k.Planner, Disiplin = k.Disiplin, KboBagian = k.KboBagian,
            AktivitasSaatIni = k.AktivitasSaatIni, Kendala = k.Kendala,
            TanggalMulai = k.TanggalMulaiBaru ?? k.TanggalMulai, TanggalSelesai = k.TanggalSelesaiBaru ?? k.TanggalSelesai,
            TanggalKom = k.TanggalKom, NilaiAwal = k.NilaiAwal, Nilai = k.NilaiKontrakBaru ?? k.NilaiAwal,
            ProgressPlan = k.ProgressPlan, ProgressActual = k.ProgressActual, HasAmendment = k.HasAmendment, CreatedAt = k.CreatedAt,
        }).ToList();
        return _kontrakCache;
    }

    private static object KontrakSummary(KontrakRow k) => new
    {
        id = k.Id,
        judul = k.Judul,
        no_dokumen = k.NoDokumen,
        no_po_pr = k.NoPoPr,
        tipe = k.Tipe,
        status = k.Status,
        vendor = k.Vendor,
        direksi_pekerjaan = k.Direksi,
        tanggal_mulai = D(k.TanggalMulai),
        tanggal_selesai = D(k.TanggalSelesai),
        sisa_hari = k.Done ? null : k.SisaHari,
        nilai_kontrak = k.Nilai,
        progress_plan = k.ProgressPlan,
        progress_actual = k.ProgressActual,
        done = k.Done,
        terlambat = k.Terlambat,
        ada_amandemen = k.HasAmendment,
        link = $"/contracts/{k.Id}",
    };

    private IEnumerable<KontrakRow> FilterKontrak(IEnumerable<KontrakRow> rows, IReadOnlyDictionary<string, JsonElement> input)
    {
        var q = Str(input, "kata_kunci");
        if (q != null)
            rows = rows.Where(k => Has(k.Judul, q) || Has(k.NoDokumen, q) || Has(k.NoPoPr, q) || Has(k.NoIrkap, q) || Has(k.Vendor, q));

        var status = Str(input, "status");
        if (status != null) rows = rows.Where(k => k.StatusGroup == NormalizeStatus(status));

        var tipe = Str(input, "tipe");
        if (tipe != null) rows = rows.Where(k => Has(k.Tipe, tipe));

        var vendor = Str(input, "vendor");
        if (vendor != null) rows = rows.Where(k => Has(k.Vendor, vendor));

        var direksi = Str(input, "direksi_pekerjaan");
        if (direksi != null) rows = rows.Where(k => Has(k.Direksi, direksi));

        var tahun = Int(input, "tahun_mulai");
        if (tahun != null) rows = rows.Where(k => k.TanggalMulai?.Year == tahun);

        var berakhir = Int(input, "berakhir_dalam_hari");
        if (berakhir != null) rows = rows.Where(k => !k.Done && k.SisaHari is { } s && s >= 0 && s <= berakhir);

        if (Bool(input, "hanya_terlambat") == true) rows = rows.Where(k => k.Terlambat);

        var deviasi = Num(input, "deviasi_minimal");
        if (deviasi != null) rows = rows.Where(k => k.Deviasi >= deviasi);

        return rows;
    }

    private async Task<object> CariKontrakAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var rows = FilterKontrak(await LoadKontrakAsync(ct), input).ToList();

        rows = Str(input, "urutkan") switch
        {
            "nilai_tertinggi" => rows.OrderByDescending(k => k.Nilai ?? 0).ToList(),
            "tanggal_selesai" => rows.OrderBy(k => k.TanggalSelesai ?? DateTime.MaxValue).ToList(),
            "deviasi_terbesar" => rows.OrderByDescending(k => k.Deviasi ?? double.MinValue).ToList(),
            _ => rows.OrderByDescending(k => k.CreatedAt).ToList(),
        };

        var batas = Limit(input);
        return new
        {
            total_cocok = rows.Count,
            total_nilai_cocok = rows.Sum(k => k.Nilai ?? 0),
            ditampilkan = Math.Min(batas, rows.Count),
            kontrak = rows.Take(batas).Select(KontrakSummary),
        };
    }

    private async Task<object> RingkasanKontrakAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var rows = FilterKontrak(await LoadKontrakAsync(ct), input).ToList();
        var by = Str(input, "kelompokkan_berdasarkan") ?? "status";

        Func<KontrakRow, string> key = by switch
        {
            "tipe" => k => k.Tipe ?? "(kosong)",
            "vendor" => k => k.Vendor ?? "(kosong)",
            "direksi_pekerjaan" => k => k.Direksi ?? "(kosong)",
            "tahun_mulai" => k => k.TanggalMulai?.Year.ToString() ?? "(kosong)",
            "disiplin" => k => k.Disiplin ?? "(kosong)",
            "program_kerja" => k => k.ProgramKerja ?? "(kosong)",
            _ => k => k.Status ?? "(kosong)",
        };

        var groups = rows.GroupBy(key)
            .Select(g => new
            {
                kelompok = g.Key,
                jumlah_kontrak = g.Count(),
                total_nilai = g.Sum(k => k.Nilai ?? 0),
                rata_progress_actual = g.Any(k => k.ProgressActual.HasValue)
                    ? Math.Round(g.Where(k => k.ProgressActual.HasValue).Average(k => k.ProgressActual!.Value), 1)
                    : (double?)null,
                jumlah_terlambat = g.Count(k => k.Terlambat),
                jumlah_done = g.Count(k => k.Done),
            });

        groups = by == "tahun_mulai"
            ? groups.OrderBy(g => g.kelompok)
            : groups.OrderByDescending(g => g.jumlah_kontrak);

        var list = groups.ToList();
        return new
        {
            dikelompokkan_berdasarkan = by,
            total_kontrak = rows.Count,
            total_nilai = rows.Sum(k => k.Nilai ?? 0),
            jumlah_kelompok = list.Count,
            kelompok = list.Take(60),
        };
    }

    private async Task<object> DetailKontrakAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var all = await LoadKontrakAsync(ct);
        KontrakRow? k = null;

        var id = Str(input, "id_kontrak");
        if (id != null) k = all.FirstOrDefault(x => x.Id == id);

        if (k == null)
        {
            var q = Str(input, "kata_kunci") ?? id;
            if (q == null) return new { error = "Isi id_kontrak atau kata_kunci." };

            var matches = all.Where(x => Has(x.Judul, q) || Has(x.NoDokumen, q) || Has(x.NoPoPr, q) || Has(x.NoIrkap, q) || Has(x.Vendor, q)).ToList();
            if (matches.Count == 0) return new { error = $"Tidak ada kontrak yang cocok dengan '{q}'." };
            if (matches.Count > 1)
                return new
                {
                    pesan = "Ada beberapa kontrak yang cocok; pilih salah satu dan panggil lagi dengan id_kontrak.",
                    jumlah_kandidat = matches.Count,
                    kandidat = matches.Take(20).Select(KontrakSummary),
                };
            k = matches[0];
        }

        var kid = k.Id;

        var amandemen = await _db.AmandemenKontraks.AsNoTracking()
            .Where(a => a.IdKontrak == kid)
            .OrderBy(a => a.NomorUrut)
            .Select(a => new { a.NomorUrut, a.NoAmandemen, a.TanggalAmandemen, a.JenisAmandemen, a.NilaiKontrakBaru, a.DurasiAmandemen, a.TanggalMulaiBaru, a.TanggalSelesaiBaru, a.AlasanPerubahan })
            .ToListAsync(ct);

        var lumpsum = await _db.ProgressLumpsums.AsNoTracking()
            .Where(p => p.IdKontrak == kid)
            .OrderBy(p => p.TanggalUpdate)
            .Select(p => new { p.Milestone, p.Persen, p.TanggalUpdate })
            .ToListAsync(ct);

        var unitPrice = await _db.ProgressUnitPrices.AsNoTracking()
            .Where(p => p.IdKontrak == kid)
            .OrderBy(p => p.TanggalUpdate)
            .Select(p => new { p.NamaItem, p.Satuan, p.QtyRencana, p.QtyAktual, p.HargaSatuan, p.TanggalUpdate })
            .ToListAsync(ct);

        var ltsa = await _db.MonitoringLtsas.AsNoTracking()
            .Where(m => m.IdKontrak == kid)
            .OrderBy(m => m.TanggalKunjungan)
            .Select(m => new { m.TanggalKunjungan, m.JenisLayanan, m.DurasiJam, m.SlaTerpenuhi, m.Keterangan })
            .ToListAsync(ct);

        var tagihan = (await LoadTagihanAsync(ct)).Where(t => t.IdKontrak == kid).OrderByDescending(t => t.Tanggal).ToList();

        return new
        {
            kontrak = KontrakSummary(k),
            no_irkap = k.NoIrkap,
            program_kerja = k.ProgramKerja,
            planner = k.Planner,
            disiplin = k.Disiplin,
            kbo_bagian = k.KboBagian,
            tanggal_kom = D(k.TanggalKom),
            nilai_awal = k.NilaiAwal,
            aktivitas_saat_ini = k.AktivitasSaatIni,
            kendala = k.Kendala,
            amandemen = amandemen.Select(a => new
            {
                nomor_urut = a.NomorUrut,
                no_amandemen = a.NoAmandemen,
                tanggal = D(a.TanggalAmandemen),
                jenis = a.JenisAmandemen,
                nilai_kontrak_baru = a.NilaiKontrakBaru,
                durasi_hari = a.DurasiAmandemen,
                tanggal_mulai_baru = D(a.TanggalMulaiBaru),
                tanggal_selesai_baru = D(a.TanggalSelesaiBaru),
                alasan = a.AlasanPerubahan,
            }),
            progress_lumpsum = lumpsum.Select(p => new { milestone = p.Milestone, persen = p.Persen, tanggal = D(p.TanggalUpdate) }),
            progress_unit_price = unitPrice.Take(MaxRows).Select(p => new
            {
                item = p.NamaItem,
                satuan = p.Satuan,
                qty_rencana = p.QtyRencana,
                qty_aktual = p.QtyAktual,
                harga_satuan = p.HargaSatuan,
                tanggal = D(p.TanggalUpdate),
            }),
            monitoring_ltsa = ltsa.Take(MaxRows).Select(m => new
            {
                tanggal = D(m.TanggalKunjungan),
                jenis_layanan = m.JenisLayanan,
                durasi_jam = m.DurasiJam,
                sla_terpenuhi = m.SlaTerpenuhi,
                keterangan = m.Keterangan,
            }),
            tagihan_ringkas = new
            {
                jumlah = tagihan.Count,
                total_nilai = tagihan.Sum(t => t.Nilai),
                per_status = tagihan.GroupBy(t => t.Status).Select(g => new { status = g.Key, jumlah = g.Count(), total_nilai = g.Sum(t => t.Nilai) }),
            },
            tagihan = tagihan.Take(50).Select(TagihanSummary),
        };
    }

    // ---------- tagihan

    private class TagihanRow
    {
        public string Id = "", IdKontrak = "", Nomor = "", Status = "", TipeKontrak = "";
        public string? Termin, JudulKontrak, Vendor;
        public DateTime Tanggal;
        public decimal Nilai;
    }

    private async Task<List<TagihanRow>> LoadTagihanAsync(CancellationToken ct)
    {
        if (_tagihanCache != null) return _tagihanCache;

        var raw = await _db.Tagihans.AsNoTracking()
            .Select(t => new
            {
                t.IdTagihan, t.IdKontrak, t.NomorTagihan, t.StatusTagihan, t.TipeKontrak, t.Termin, t.TanggalTagihan, t.NilaiTagihan,
                Judul = t.Kontrak != null ? t.Kontrak.JudulKontrak : null,
                VendorNama = t.Kontrak != null && t.Kontrak.Vendor != null ? t.Kontrak.Vendor.NamaVendor : null,
            })
            .ToListAsync(ct);

        _tagihanCache = raw.Select(t => new TagihanRow
        {
            Id = t.IdTagihan, IdKontrak = t.IdKontrak, Nomor = t.NomorTagihan, Status = t.StatusTagihan, TipeKontrak = t.TipeKontrak,
            Termin = t.Termin, Tanggal = t.TanggalTagihan, Nilai = t.NilaiTagihan, JudulKontrak = t.Judul, Vendor = t.VendorNama,
        }).ToList();
        return _tagihanCache;
    }

    private static object TagihanSummary(TagihanRow t) => new
    {
        id = t.Id,
        nomor = t.Nomor,
        tanggal = D(t.Tanggal),
        id_kontrak = t.IdKontrak,
        kontrak = t.JudulKontrak,
        vendor = t.Vendor,
        tipe_kontrak = t.TipeKontrak,
        termin = t.Termin,
        nilai = t.Nilai,
        status = t.Status,
        link = $"/invoices/{t.Id}",
    };

    private IEnumerable<TagihanRow> FilterTagihan(IEnumerable<TagihanRow> rows, IReadOnlyDictionary<string, JsonElement> input)
    {
        var idKontrak = Str(input, "id_kontrak");
        if (idKontrak != null) rows = rows.Where(t => t.IdKontrak == idKontrak);

        var q = Str(input, "kata_kunci");
        if (q != null) rows = rows.Where(t => Has(t.Nomor, q) || Has(t.JudulKontrak, q) || Has(t.Vendor, q));

        var status = Str(input, "status");
        if (status != null) rows = rows.Where(t => Has(t.Status, status));

        var tipe = Str(input, "tipe_kontrak");
        if (tipe != null) rows = rows.Where(t => Has(t.TipeKontrak, tipe));

        var dari = Date(input, "dari_tanggal");
        if (dari != null) rows = rows.Where(t => t.Tanggal.Date >= dari);

        var sampai = Date(input, "sampai_tanggal");
        if (sampai != null) rows = rows.Where(t => t.Tanggal.Date <= sampai);

        return rows;
    }

    private async Task<object> CariTagihanAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var rows = FilterTagihan(await LoadTagihanAsync(ct), input).ToList();
        rows = Str(input, "urutkan") == "nilai_tertinggi"
            ? rows.OrderByDescending(t => t.Nilai).ToList()
            : rows.OrderByDescending(t => t.Tanggal).ToList();

        var batas = Limit(input);
        return new
        {
            total_cocok = rows.Count,
            total_nilai_cocok = rows.Sum(t => t.Nilai),
            ditampilkan = Math.Min(batas, rows.Count),
            tagihan = rows.Take(batas).Select(TagihanSummary),
        };
    }

    private async Task<object> RingkasanTagihanAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var rows = FilterTagihan(await LoadTagihanAsync(ct), input).ToList();
        var by = Str(input, "kelompokkan_berdasarkan") ?? "status";

        Func<TagihanRow, string> key = by switch
        {
            "bulan" => t => t.Tanggal.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            "tahun" => t => t.Tanggal.Year.ToString(),
            "tipe_kontrak" => t => t.TipeKontrak,
            "vendor" => t => t.Vendor ?? "(kosong)",
            "kontrak" => t => t.JudulKontrak ?? t.IdKontrak,
            _ => t => t.Status,
        };

        var groups = rows.GroupBy(key)
            .Select(g => new { kelompok = g.Key, jumlah_tagihan = g.Count(), total_nilai = g.Sum(t => t.Nilai) });

        groups = by is "bulan" or "tahun"
            ? groups.OrderBy(g => g.kelompok)
            : groups.OrderByDescending(g => g.total_nilai);

        var list = groups.ToList();
        return new
        {
            dikelompokkan_berdasarkan = by,
            total_tagihan = rows.Count,
            total_nilai = rows.Sum(t => t.Nilai),
            jumlah_kelompok = list.Count,
            kelompok = list.Take(60),
        };
    }

    // ---------- amandemen

    private async Task<object> CariAmandemenAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var raw = await _db.AmandemenKontraks.AsNoTracking()
            .Select(a => new
            {
                a.IdKontrak, a.NomorUrut, a.NoAmandemen, a.TanggalAmandemen, a.JenisAmandemen, a.NilaiKontrakBaru,
                a.DurasiAmandemen, a.TanggalMulaiBaru, a.TanggalSelesaiBaru, a.AlasanPerubahan,
                Judul = a.Kontrak != null ? a.Kontrak.JudulKontrak : null,
                VendorNama = a.Kontrak != null && a.Kontrak.Vendor != null ? a.Kontrak.Vendor.NamaVendor : null,
            })
            .ToListAsync(ct);

        var rows = raw.AsEnumerable();

        var idKontrak = Str(input, "id_kontrak");
        if (idKontrak != null) rows = rows.Where(a => a.IdKontrak == idKontrak);

        var q = Str(input, "kata_kunci");
        if (q != null) rows = rows.Where(a => Has(a.NoAmandemen, q) || Has(a.Judul, q) || Has(a.VendorNama, q) || Has(a.AlasanPerubahan, q));

        var jenis = Str(input, "jenis");
        if (jenis != null) rows = rows.Where(a => Has(a.JenisAmandemen, jenis));

        var dari = Date(input, "dari_tanggal");
        if (dari != null) rows = rows.Where(a => a.TanggalAmandemen?.Date >= dari);

        var sampai = Date(input, "sampai_tanggal");
        if (sampai != null) rows = rows.Where(a => a.TanggalAmandemen?.Date <= sampai);

        var list = rows.OrderByDescending(a => a.TanggalAmandemen ?? DateTime.MinValue).ToList();
        var batas = Limit(input);
        return new
        {
            total_cocok = list.Count,
            ditampilkan = Math.Min(batas, list.Count),
            amandemen = list.Take(batas).Select(a => new
            {
                id_kontrak = a.IdKontrak,
                kontrak = a.Judul,
                vendor = a.VendorNama,
                nomor_urut = a.NomorUrut,
                no_amandemen = a.NoAmandemen,
                tanggal = D(a.TanggalAmandemen),
                jenis = a.JenisAmandemen,
                nilai_kontrak_baru = a.NilaiKontrakBaru,
                durasi_hari = a.DurasiAmandemen,
                tanggal_mulai_baru = D(a.TanggalMulaiBaru),
                tanggal_selesai_baru = D(a.TanggalSelesaiBaru),
                alasan = a.AlasanPerubahan,
                link = $"/contracts/{a.IdKontrak}",
            }),
        };
    }

    // ===================================================================== HELPERS

    private static string? D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool Has(string? value, string term) =>
        value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static int? Int(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)d;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) return i;
        return null;
    }

    private static double? Num(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static bool? Bool(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return null;
    }

    private static DateTime? Date(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        var s = Str(input, key);
        return s != null && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date
            : null;
    }

    private static int Limit(IReadOnlyDictionary<string, JsonElement> input) =>
        Math.Clamp(Int(input, "batas") ?? 50, 1, MaxRows);
}
