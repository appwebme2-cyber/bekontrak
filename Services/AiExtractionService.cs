using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using RefineryContractAPI.DTOs;

namespace RefineryContractAPI.Services;

public record AiDocumentRef(string Key, string MimeType);
public record AiRabItemContext(string KodeItem, string UraianPekerjaan, string Satuan);

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
7. Kalau daftar item RAB kontrak disertakan di bawah ini, untuk SETIAP baris pekerjaan/material yang Anda hasilkan, cek apakah ada item RAB yang jenis pekerjaan & satuannya paling cocok. Kalau ada yang cocok, isi field kodeItem dengan KODE PERSIS (sama persis, case-sensitive) dari daftar itu. Kalau tidak ada yang cukup cocok, KOSONGKAN kodeItem — JANGAN PERNAH mengarang kode yang tidak ada di daftar.

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

        if (existingRabItems is { Count: > 0 })
        {
            var rabList = string.Join("\n", existingRabItems.Select(r => $"- {r.KodeItem} | {r.UraianPekerjaan} | satuan: {r.Satuan}"));
            contentBlocks.Add(new TextBlockParam
            {
                Text = $"Daftar item RAB kontrak ini yang sudah tersedia (kode | uraian | satuan):\n{rabList}\n\nCocokkan baris yang Anda hasilkan ke kode di atas kalau relevan (lihat instruksi poin 7)."
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
                            kodeItem = new { type = "string", description = "Kode item RAB yang cocok dari daftar yang diberikan, kosongkan kalau tidak ada yang cocok" },
                            uraianPekerjaan = new { type = "string" },
                            satuan = new { type = "string" },
                            volumeKalkulasi = new { type = "number" },
                            catatanKalkulasi = new { type = "string" }
                        },
                        required = new[] { "jenis", "uraianPekerjaan", "satuan", "volumeKalkulasi" }
                    }
                }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "problem", "rekomendasiSolusi", "lines" })
        };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 8000,
            System = SystemPrompt,
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema }
            },
            Messages = [new() { Role = Role.User, Content = contentBlocks }]
        });

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

        return result ?? new ExtractMaterialRequirementResultDto();
    }
}
