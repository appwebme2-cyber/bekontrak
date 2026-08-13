using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefineryContractAPI.Models;

public class Profile
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    [Required]
    public string Role { get; set; } = "user"; // admin, pic, user
    public string? PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("is_active")]
    public bool IsActive { get; set; } = true;
    [Column("id_vendor")]
    public string? IdVendor { get; set; }
    [ForeignKey("IdVendor")]
    public Vendor? Vendor { get; set; }
}

public class Vendor
{
    [Key]
    public string IdVendor { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string NamaVendor { get; set; } = string.Empty;
    public string? Npwp { get; set; }
    public string? Alamat { get; set; }
    public string? PicNama { get; set; }
    public string? PicKontak { get; set; }
    [Required]
    public string StatusVendor { get; set; } = "Active"; // Active, Inactive, Blacklist
    public double? Score { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Kontrak> Kontraks { get; set; } = new List<Kontrak>();
    public ICollection<Padi> Padis { get; set; } = new List<Padi>();
}


public class DireksiPekerjaan
{
    [Key]
    public string IdDireksiPekerjaan { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string Nama { get; set; } = string.Empty;
    [Required]
    public string Jabatan { get; set; } = string.Empty; // Manager Maintenance Execution I, Manager Maintenance Execution II, Pengawas Pekerjaan
    public string? SubArea { get; set; } // SH Maintenance Area 5/6/7, Workshop (untuk Pengawas Pekerjaan)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ProgramKerja
{
    [Key]
    public string IdProgramKerja { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string Nama { get; set; } = string.Empty; // Rutin, Non Rutin, TA, OH
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Planner
{
    [Key]
    public string IdPlanner { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string Nama { get; set; } = string.Empty; // P&S, OH, TA
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SlaSetting
{
    [Key]
    [Column("kode_tahap")]
    public string KodeTahap { get; set; } = string.Empty;
 
    [Column("batas_hari")]
    public int BatasHari { get; set; } = 5;
 
    [Column("warning_persen")]
    public int WarningPersen { get; set; } = 80;
}
 

public class Kontrak
{
    [Key]
    public string IdKontrak { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdVendor { get; set; } = string.Empty;
    [Required]
    public string JudulKontrak { get; set; } = string.Empty;
    public string? NoDokumenKontrak { get; set; }
    public string? NoPoPr { get; set; }
    public string? NoIrkap { get; set; }
    public string? DireksiPekerjaan { get; set; }
    public string? ProgramKerja { get; set; }
    public string? Planner { get; set; }
    public string? KboBagian { get; set; }
    [Required]
    public string TipeKontrak { get; set; } = string.Empty; // Lumpsum, Unit Price, TSA, LTSA, TSA/LTSA
    [Required]
    public string StatusKontrak { get; set; } = "Pre-KOM"; // Pre-KOM, Active, Aktif, Completed, Selesai, Terminated
    [Required]
    public DateTime TanggalSpbDiterima { get; set; }
    public DateTime? TanggalTerimaDokumen { get; set; }
    public DateTime? TanggalMaksimalKom { get; set; }
    public DateTime? TanggalMulai { get; set; }
    public DateTime? TanggalSelesai { get; set; }
    public int SlaKomHari { get; set; } = 14;
    [Required]
    public DateTime EstimasiTanggalKom { get; set; }
    public DateTime? TanggalKom { get; set; }
    public bool KomTerlambat { get; set; } = false;
    public decimal? NilaiAwal { get; set; }
    public int? DurasiKontrakHari { get; set; }
    public double? ProgressPlan { get; set; }
    public double? ProgressActual { get; set; }
    public string? AktivitasSaatIni { get; set; }
    public string? Kendala { get; set; }
    public string? Disiplin { get; set; }
    public double? TkdnPercentage { get; set; }
    public DateTime? TanggalLkp { get; set; }
    // Amendment inline
    public bool HasAmendment { get; set; } = false;
    public string? NoAmandemen { get; set; }
    public DateTime? TanggalAmandemen { get; set; }
    public string? JenisAmandemen { get; set; }
    public decimal? NilaiKontrakBaru { get; set; }
    public int? DurasiAmandemen { get; set; }
    public DateTime? TanggalMulaiBaru { get; set; }
    public DateTime? TanggalSelesaiBaru { get; set; }
    public string? AlasanPerubahan { get; set; }
    // Documents as JSON string
    public string? ContractDocuments { get; set; }
    public string? AmendmentDocuments { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? SCurveData { get; set; }
    // MPL, MPA, Masa Pemeliharaan
    public int? TanggalMpl { get; set; }
    public int? TanggalMpa { get; set; }
    public int? MasaPemeliharaanHari { get; set; }

    // Navigation
    [ForeignKey("IdVendor")]
    public Vendor? Vendor { get; set; }
    public ICollection<AmandemenKontrak> Amandemen { get; set; } = new List<AmandemenKontrak>();
    public ICollection<Tagihan> Tagihans { get; set; } = new List<Tagihan>();
    public ICollection<ProgressLumpsum> ProgressLumpsums { get; set; } = new List<ProgressLumpsum>();
    public ICollection<ProgressUnitPrice> ProgressUnitPrices { get; set; } = new List<ProgressUnitPrice>();
    public ICollection<MonitoringLtsa> MonitoringLtsas { get; set; } = new List<MonitoringLtsa>();
    public ICollection<DokumenApproval> DokumenApprovals { get; set; } = new List<DokumenApproval>();
}

public class AmandemenKontrak
{
    [Key]
    public string IdAmandemen { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    public int NomorUrut { get; set; }
    public string? NoAmandemen { get; set; }
    public DateTime? TanggalAmandemen { get; set; }
    public string? JenisAmandemen { get; set; }
    public decimal? NilaiKontrakBaru { get; set; }
    public int? DurasiAmandemen { get; set; }
    public DateTime? TanggalMulaiBaru { get; set; }
    public DateTime? TanggalSelesaiBaru { get; set; }
    public string? AlasanPerubahan { get; set; }
    public string? AmendmentDocuments { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}

public class Tagihan
{
    [Key]
    public string IdTagihan { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    [Required]
    public string NomorTagihan { get; set; } = string.Empty;
    [Required]
    public DateTime TanggalTagihan { get; set; }
    [Required]
    public string TipeKontrak { get; set; } = string.Empty;
    public string? Termin { get; set; }
    [Required]
    public decimal NilaiTagihan { get; set; }
    [Required]
    public string StatusTagihan { get; set; } = string.Empty;
    public bool MemoRequired { get; set; } = false;
    public DateTime? TanggalPengirimanMemo { get; set; }
    public string? DokumenMemo { get; set; }
    public string? DokumenTagihan { get; set; }
    public string? Catatan { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}

public class ProgressLumpsum
{
    [Key]
    public string IdProgress { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    [Required]
    public string Milestone { get; set; } = string.Empty;
    public double Persen { get; set; }
    [Required]
    public DateTime TanggalUpdate { get; set; }
    public string? Evidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}

public class ProgressUnitPrice
{
    [Key]
    public string IdProgress { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    [Required]
    public string NamaItem { get; set; } = string.Empty;
    [Required]
    public string Satuan { get; set; } = string.Empty;
    public double QtyRencana { get; set; }
    public double QtyAktual { get; set; }
    public decimal HargaSatuan { get; set; }
    [Required]
    public DateTime TanggalUpdate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}

public class MonitoringLtsa
{
    [Key]
    public string IdLog { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    [Required]
    public DateTime TanggalKunjungan { get; set; }
    [Required]
    public string JenisLayanan { get; set; } = string.Empty; // Preventive, Corrective, Standby
    public double DurasiJam { get; set; }
    [Required]
    public string SlaTerpenuhi { get; set; } = "Yes"; // Yes, No
    public string? Keterangan { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}

public class Padi
{
    [Key]
    public string IdPadi { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string NoPembelian { get; set; } = string.Empty;
    [Required]
    public DateTime Tanggal { get; set; }
    [Required]
    public string JudulPembelian { get; set; } = string.Empty;
    public string? NoPoPr { get; set; }
    [Required]
    public decimal Nilai { get; set; }
    public string? IdVendor { get; set; }
    public string? LinkPembelian { get; set; }
    public string? Bagian { get; set; }
    public string? DokumenPendukung { get; set; }
    public string StatusPurchase { get; set; } = "BAST";
    public DateTime? TanggalBast { get; set; }
    public DateTime? TanggalSaGr { get; set; }
    public DateTime? TanggalInvoice { get; set; }
    public DateTime? TanggalPaymentApproval { get; set; }
    public DateTime? TanggalPaid { get; set; }
    public string? CatatanStatus { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("IdVendor")]
    public Vendor? Vendor { get; set; }
}



public class DokumenApproval
{
    [Key]
    public string IdDokumen { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string IdKontrak { get; set; } = string.Empty;
    [Required]
    public string TipeDokumen { get; set; } = string.Empty; // Evident, Report, Persetujuan
    [Required]
    public string NamaDokumen { get; set; } = string.Empty;
    public string? DeskripsiDokumen { get; set; }
    [Required]
    public string FilePath { get; set; } = string.Empty; // path file di server
    public string? FileUrl { get; set; } // URL untuk akses file
    public string? NamaFile { get; set; } // nama file asli
    public string? TipeFile { get; set; } // mime type
    public long? UkuranFile { get; set; } // bytes
    [Required]
    public string StatusApproval { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string? CatatanReviewer { get; set; }
    public string? UploadedBy { get; set; } // nama/id uploader
    public string? ReviewedBy { get; set; } // nama/id reviewer
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
 
    [ForeignKey("IdKontrak")]
    public Kontrak? Kontrak { get; set; }
}




public class DailyReport
{
    [Key]
    public string IdReport { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public DateTime TanggalLaporan { get; set; }
    [Required]
    public string Disiplin { get; set; } = string.Empty;
    [Required]
    public string Kategori { get; set; } = string.Empty;
    [Required]
    public string Deskripsi { get; set; } = string.Empty;
    public string? Direksi { get; set; }
    public string? TagNumber { get; set; }
    public string? StatusPekerjaan { get; set; }
    public string? Catatan { get; set; }
    public string? PengirimWa { get; set; }
    public string? RawText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class KonfigurasiSistem
{
    [Key]
    public string IdSetting { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string NamaSetting { get; set; } = string.Empty;
    [Required]
    public string NilaiSetting { get; set; } = string.Empty;
    public string? Deskripsi { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SlaTagihan
{
    public int Id { get; set; }
 

    public string IdKontrak { get; set; } = null!;
    public string? IdTagihan { get; set; }
 
    // Tahap 1: BA Joint Inspection
    public DateTime? TglMasukBaJointInspection { get; set; }
    public DateTime? TglSelesaiBaJointInspection { get; set; }

    // Tahap 2: BA Commissioning
    public DateTime? TglMasukBaCommissioning { get; set; }
    public DateTime? TglSelesaiBaCommissioning { get; set; }

    // Tahap 3: BA Penerimaan Material
    public DateTime? TglMasukBaPenerimaanMaterial { get; set; }
    public DateTime? TglSelesaiBaPenerimaanMaterial { get; set; }

    // Tahap 4: LKP
    public DateTime? TglMasukLkp { get; set; }
    public DateTime? TglSelesaiLkp { get; set; }

    // Tahap 5: BASTP (was BAST)
    public DateTime? TglMasukBast { get; set; }
    public DateTime? TglSelesaiBast { get; set; }

    // Tahap 6: BAKP
    public DateTime? TglMasukBakp { get; set; }
    public DateTime? TglSelesaiBakp { get; set; }
 
    // Tahap 7: IVENDOR
    public DateTime? TglMasukIvendor { get; set; }
    public DateTime? TglSelesaiIvendor { get; set; }

    // Tahap 8: SA
    public DateTime? TglMasukSa { get; set; }
    public DateTime? TglSelesaiSa { get; set; }

    // Tahap 9: PA
    public DateTime? TglMasukPa { get; set; }
    public DateTime? TglSelesaiPa { get; set; }

    // Tahap 10: VERIFIKASI
    public DateTime? TglMasukVerifikasi { get; set; }
    public DateTime? TglSelesaiVerifikasi { get; set; }

    // Tahap 11: PAYMENT
    public DateTime? TglMasukPayment { get; set; }
    public DateTime? TglSelesaiPayment { get; set; }
 
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
public class TokenBlacklist
{
    [Key]
    public int Id { get; set; }
    public string Jti { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;
}
