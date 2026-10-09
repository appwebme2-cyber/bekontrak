using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using RefineryContractAPI.DTOs;

namespace RefineryContractAPI.Services;

public record AiDocumentRef(string Key, string MimeType);
public record AiRabItemContext(string IdRabItem, string KodeItem, string UraianPekerjaan, string Satuan);

public class AiExtractionService
{
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly R2StorageService _r2;

    public AiExtractionService(IConfiguration config, R2StorageService r2)
    {
        _apiKey = config["Anthropic:ApiKey"];
        _model = string.IsNullOrWhiteSpace(config["Anthropic:Model"]) ? "claude-sonnet-5-5" : config["Anthropic:Model"]!;
        _r2 = r2;
    }

    private const string SystemPrompt = @"Anda adalah Field Engineer & Planner yang membantu menyusun draft kebutuhan material dan pekerjaan dari sebuah Rekomendasi/MRF (laporan kerusakan + solusi perbaikan) dan/atau gambar sketsa rencana kerja.

Tugas Anda:
1. Baca dokumen yang dilampirkan (PDF Rekomendasi dan/atau gambar sketsa).
2. Ekstrak: problem (masalah di lapangan), rekomendasiSolusi (perbaikan yang direkomendasikan), dan tagUnit (nomor tag/unit peralatan) kalau disebutkan.
3. Susun daftar baris pekerjaan (jenis=""Pekerjaan"") dan material/BOM (jenis=""Material"") yang dibutuhkan berdasarkan sketsa/dimensi yang terlihat, dengan estimasi volume numerik.
4. Untuk perhitungan volume pekerjaan berbasis pipa/geometri, pakai rumus teknik standar: keliling pipa = π×D, luas permukaan cat = π×D×panjang, volume potong plat = ((π×OD)+(π×ID))×jumlah/1000, dan sejenisnya — tuliskan rumus/alasan yang dipakai di catatanKalkulasi.
5. Kalau ada bagian yang tidak terbaca jelas, tulisan tangan ambigu, atau Anda tidak yakin, SEBUTKAN eksplisit di catatanKalkulasi (mis. ""dimensi tidak terbaca jelas, mohon verifikasi manual"") alih-alih menebak tanpa keterangan.
6. Jangan mengarang data yang sama sekali tidak ada di dokumen — kosongkan field tersebut kalau memang tidak tersedia.
7. Kalau daftar item RAB kontrak disertakan, tiap item punya NOMOR REFERENSI di dalam kurung siku (mis. [R12]). Untuk SETIAP baris pekerjaan/material yang Anda hasilkan, cek apakah ada item RAB yang jenis pekerjaan & satuannya paling cocok. Kalau ada yang cocok, isi field rabRef dengan nomor referensi itu persis (mis. ""R12""). Kalau tidak ada yang cukup cocok, KOSONGKAN rabRef — JANGAN PERNAH mengarang nomor referensi yang tidak ada di daftar. Pencocokan hanya berdasarkan uraian dan satuan; abaikan kode item untuk menentukan kecocokan karena kode bisa kembar.
7b. Kalau dokumen sendiri mencantumkan kode item untuk pekerjaan itu (mis. di rincian biaya/tagihan, seperti 2.1.1.2.13), isi kodeDokumen dengan kode tersebut persis seperti tertulis. Kosongkan kalau tidak ada. Ini hanya informasi tambahan, terpisah dari rabRef.

8. Kalau dokumen memuat rincian biaya/tagihan kontraktor (mis. ""PERINCIAN BIAYA TAGIHAN PEKERJAAN"", checklist actual pekerjaan, atau laporan unit price) berisi volume per item, isi volumeKlaim dengan volume yang DITAGIHKAN pada item yang sesuai. Pakai lembar tagihan final/aktual, BUKAN lembar prognosa/rencana. Kalau item itu tidak ada di rincian tagihan, atau dokumen tidak memuat rincian tagihan sama sekali, KOSONGKAN volumeKlaim. JANGAN menyalin volumeKalkulasi ke volumeKlaim dan jangan menebak.

Jawab HANYA dalam format JSON sesuai schema yang diberikan, dalam Bahasa Indonesia.";

    public async Task<ExtractMaterialRequirementResultDto> ExtractAsync(List<AiDocumentRef> documents, List<AiRabItemContext>? existingRabItems = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Anthropic API key belum dikonfigurasi. Hubungi admin untuk mengatur environment variable Anthropic__ApiKey.");

        if (documents.Count == 0)
            throw new InvalidOperationException("Tidak ada dokumen untuk diekstrak.");

        var client = new AnthropicClient { ApiKey = _apiKey };

        var contentBlocks = new List<ContentBlockParam>();
        foreach (var doc in documents)
        {
            var (stream, contentType, _) = await _r2.GetAsync(doc.Key);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var mime = string.IsNullOrWhiteSpace(doc.MimeType) ? contentType : doc.MimeType;

            if (mime == "application/pdf")
            {
                contentBlocks.Add(new DocumentBlockParam
                {
                    Source = new Base64PdfSource { Data = base64 }
                });
            }
            else if (mime is "image/jpeg" or "image/png" or "image/jpg")
            {
                contentBlocks.Add(new ImageBlockParam
                {
                    Source = new Base64ImageSource { MediaType = mime, Data = base64 }
                });
            }
            // tipe file lain (doc/xls) dilewati - Claude hanya baca PDF/gambar di sini
        }

        if (contentBlocks.Count == 0)
            throw new InvalidOperationException("Tidak ada dokumen PDF/gambar yang bisa dibaca AI (hanya PDF/JPEG/PNG yang didukung).");

        // Tiap item RAB diberi nomor referensi unik (R1, R2, ...) karena kode item bisa kembar
        // dalam satu RAB (penomoran dimulai ulang di tiap bagian). AI mengembalikan nomor ini,
        // lalu diterjemahkan kembali ke item RAB yang tepat setelah respons diterima.
        Dictionary<string, AiRabItemContext>? refMap = null;
        if (existingRabItems is { Count: > 0 })
        {
            refMap = new Dictionary<string, AiRabItemContext>();
            for (var i = 0; i < existingRabItems.Count; i++)
                refMap[$"R{i + 1}"] = existingRabItems[i];

            var rabList = string.Join("\n", refMap.Select(kv =>
                $"[{kv.Key}] {kv.Value.KodeItem} | {kv.Value.UraianPekerjaan} | satuan: {kv.Value.Satuan}"));
            contentBlocks.Add(new TextBlockParam
            {
                Text = $"Daftar item RAB kontrak ini yang sudah tersedia ([nomor referensi] kode | uraian | satuan):\n{rabList}\n\nCocokkan baris yang Anda hasilkan ke nomor referensi di atas kalau relevan (lihat instruksi poin 7)."
            });
        }

        contentBlocks.Add(new TextBlockParam
        {
            Text = "Tolong analisis dokumen di atas dan hasilkan draft kebutuhan material & pekerjaan sesuai instruksi."
        });

        var schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                problem = new { type = "string" },
                rekomendasiSolusi = new { type = "string" },
                tagUnit = new { type = "string" },
                lines = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            jenis = new { type = "string", @enum = new[] { "Pekerjaan", "Material" } },
                            rabRef = new { type = "string", description = "Nomor referensi item RAB yang cocok dari daftar yang diberikan (mis. R12), kosongkan kalau tidak ada yang cocok" },
                            kodeDokumen = new { type = "string", description = "Kode item yang tertulis di dokumen untuk pekerjaan ini (mis. di rincian biaya), kosongkan kalau tidak ada" },
                            uraianPekerjaan = new { type = "string" },
                            satuan = new { type = "string" },
                            volumeKalkulasi = new { type = "number" },
                            volumeKlaim = new { type = "number", description = "Volume yang ditagihkan kontraktor pada item ini menurut rincian tagihan di dokumen, kosongkan kalau tidak ada" },
                            catatanKalkulasi = new { type = "string" }
                        },
                        required = new[] { "jenis", "uraianPekerjaan", "satuan", "volumeKalkulasi" },
                        additionalProperties = false
                    }
                }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "problem", "rekomendasiSolusi", "lines" }),
            // Structured output Claude mewajibkan additionalProperties:false di setiap objek
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            // Token "berpikir" Claude ikut terhitung di batas ini; 8000 terbukti kurang untuk dokumen
            // dengan puluhan baris sehingga JSON terpotong di tengah.
            MaxTokens = 16000,
            System = SystemPrompt,
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema }
            },
            Messages = [new() { Role = Role.User, Content = contentBlocks }]
        });

        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("Hasil AI terpotong karena dokumen terlalu panjang/kompleks. Coba pecah dokumen menjadi bagian yang lebih kecil, atau upload hanya halaman yang relevan.");

        var text = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("AI tidak mengembalikan hasil yang bisa dibaca.");

        var result = JsonSerializer.Deserialize<ExtractMaterialRequirementResultDto>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result ??= new ExtractMaterialRequirementResultDto();

        // Terjemahkan nomor referensi dari AI ke item RAB sebenarnya; nomor yang tidak dikenal diabaikan
        foreach (var line in result.Lines)
        {
            var key = line.RabRef?.Trim().Trim('[', ']').ToUpperInvariant();
            if (refMap != null && !string.IsNullOrEmpty(key) && refMap.TryGetValue(key, out var item))
            {
                line.IdRabItem = item.IdRabItem;
                line.KodeItem = item.KodeItem;
            }
            line.RabRef = null;
        }

        return result;
    }
}
