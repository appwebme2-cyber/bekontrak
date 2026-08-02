namespace RefineryContractAPI.DTOs;

// ===================== AUTH =====================
public class LoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = "user";
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class SeedAdminDto
{
    public string SeedToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

// ===================== VENDOR =====================
public class VendorDto
{
    public string IdVendor { get; set; } = string.Empty;
    public string NamaVendor { get; set; } = string.Empty;
    public string? Npwp { get; set; }
    public string? Alamat { get; set; }
    public string? PicNama { get; set; }
    public string? PicKontak { get; set; }
    public string StatusVendor { get; set; } = "Active";
    public double? Score { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateVendorDto
{
    public string NamaVendor { get; set; } = string.Empty;
    public string? Npwp { get; set; }
    public string? Alamat { get; set; }
    public string? PicNama { get; set; }
    public string? PicKontak { get; set; }
    public string StatusVendor { get; set; } = "Active";
    public double? Score { get; set; }
}

public class UpdateVendorDto : CreateVendorDto { }

// ===================== DIREKSI PEKERJAAN =====================
public class DireksiPekerjaanDto
{
    public string IdDireksiPekerjaan { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Jabatan { get; set; } = string.Empty;
    public string? SubArea { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateDireksiPekerjaanDto
{
    public string Nama { get; set; } = string.Empty;
    public string Jabatan { get; set; } = string.Empty;
    public string? SubArea { get; set; }
}

public class UpdateDireksiPekerjaanDto : CreateDireksiPekerjaanDto { }

// ===================== PROGRAM KERJA =====================
public class ProgramKerjaDto
{
    public string IdProgramKerja { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateProgramKerjaDto
{
    public string Nama { get; set; } = string.Empty;
}

public class UpdateProgramKerjaDto : CreateProgramKerjaDto { }

// ===================== PLANNER =====================
public class PlannerDto
{
    public string IdPlanner { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreatePlannerDto
{
    public string Nama { get; set; } = string.Empty;
}

public class UpdatePlannerDto : CreatePlannerDto { }

// ===================== KONTRAK =====================
public class KontrakDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public string IdVendor { get; set; } = string.Empty;
    public string JudulKontrak { get; set; } = string.Empty;
    public string? NoDokumenKontrak { get; set; }
    public string? NoPoPr { get; set; }
    public string? DireksiPekerjaan { get; set; }
    public string? ProgramKerja { get; set; }
    public string? Planner { get; set; }
    public string? KboBagian { get; set; }
    public string TipeKontrak { get; set; } = string.Empty;
    public string StatusKontrak { get; set; } = string.Empty;
    public DateTime TanggalSpbDiterima { get; set; }
    public DateTime? TanggalTerimaDokumen { get; set; }
    public DateTime? TanggalMaksimalKom { get; set; }
    public DateTime? TanggalMulai { get; set; }
    public DateTime? TanggalSelesai { get; set; }
    public int SlaKomHari { get; set; }
    public DateTime EstimasiTanggalKom { get; set; }
    public DateTime? TanggalKom { get; set; }
    public bool KomTerlambat { get; set; }
    public decimal? NilaiAwal { get; set; }
    public int? DurasiKontrakHari { get; set; }
    public double? ProgressPlan { get; set; }
    public double? ProgressActual { get; set; }
    public string? AktivitasSaatIni { get; set; }
    public string? Kendala { get; set; }
    public string? Disiplin { get; set; }
    public double? TkdnPercentage { get; set; }
    public DateTime? TanggalLkp { get; set; }
    public bool HasAmendment { get; set; }
    public string? NoAmandemen { get; set; }
    public DateTime? TanggalAmandemen { get; set; }
    public string? JenisAmandemen { get; set; }
    public decimal? NilaiKontrakBaru { get; set; }
    public int? DurasiAmandemen { get; set; }
    public DateTime? TanggalMulaiBaru { get; set; }
    public DateTime? TanggalSelesaiBaru { get; set; }
    public string? AlasanPerubahan { get; set; }

    public string? ContractDocuments { get; set; }   // ← tambah
    public string? AmendmentDocuments { get; set; }  // ← tambah
    public string? SCurveData { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? TanggalMpl { get; set; }
    public int? TanggalMpa { get; set; }
    public int? MasaPemeliharaanHari { get; set; }
    public int? JumlahTagihan { get; set; }
    public VendorDto? Vendor { get; set; }
}

public class CreateKontrakDto
{
    public string IdVendor { get; set; } = string.Empty;
    public string JudulKontrak { get; set; } = string.Empty;
    public string? NoDokumenKontrak { get; set; }
    public string? NoPoPr { get; set; }
    public string? DireksiPekerjaan { get; set; }
    public string? ProgramKerja { get; set; }
    public string? Planner { get; set; }
    public string? KboBagian { get; set; }
    public string TipeKontrak { get; set; } = string.Empty;
    public string StatusKontrak { get; set; } = "Pre-KOM";
    public DateTime TanggalSpbDiterima { get; set; }
    public DateTime? TanggalTerimaDokumen { get; set; }
    public DateTime? TanggalMaksimalKom { get; set; }
    public DateTime? TanggalMulai { get; set; }
    public DateTime? TanggalSelesai { get; set; }
    public int SlaKomHari { get; set; } = 14;
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
    public string? SCurveData { get; set; }
    public string? ContractDocuments { get; set; }
    public string? AmendmentDocuments { get; set; }
    public int? TanggalMpl { get; set; }
    public int? TanggalMpa { get; set; }
    public int? MasaPemeliharaanHari { get; set; }
    }

public class UpdateKontrakDto : CreateKontrakDto { }

// ===================== AMANDEMEN =====================
public class AmandemenDto
{
    public string IdAmandemen { get; set; } = string.Empty;
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
    public string? SCurveData { get; set; }
    public DateTime? CreatedAt { get; set; }
    
}

public class CreateAmandemenDto
{
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
}

public class UpdateAmandemenDto : CreateAmandemenDto { }

// ===================== TAGIHAN =====================
public class TagihanDto
{
    public string IdTagihan { get; set; } = string.Empty;
    public string IdKontrak { get; set; } = string.Empty;
    public string NomorTagihan { get; set; } = string.Empty;
    public DateTime TanggalTagihan { get; set; }
    public string TipeKontrak { get; set; } = string.Empty;
    public string? Termin { get; set; }
    public decimal NilaiTagihan { get; set; }
    public string StatusTagihan { get; set; } = string.Empty;
    public bool MemoRequired { get; set; }
    public DateTime? TanggalPengirimanMemo { get; set; }
    public string? Catatan { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public KontrakSummaryDto? Kontrak { get; set; }
    public string? DokumenTagihan { get; set; }
    public string? DokumenMemo { get; set; }
}

public class KontrakSummaryDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public string JudulKontrak { get; set; } = string.Empty;
    public string TipeKontrak { get; set; } = string.Empty;
    public VendorDto? Vendor { get; set; }
}

public class CreateTagihanDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public string NomorTagihan { get; set; } = string.Empty;
    public DateTime TanggalTagihan { get; set; }
    public string TipeKontrak { get; set; } = string.Empty;
    public string? Termin { get; set; }
    public decimal NilaiTagihan { get; set; }
    public string StatusTagihan { get; set; } = string.Empty;
    public bool MemoRequired { get; set; } = false;
    public DateTime? TanggalPengirimanMemo { get; set; }
    public string? Catatan { get; set; }
    public string? DokumenTagihan { get; set; }
    public string? DokumenMemo { get; set; }
}

public class UpdateTagihanDto : CreateTagihanDto { }

// ===================== PADI =====================
public class PadiDto
{
    public string IdPadi { get; set; } = string.Empty;
    public string NoPembelian { get; set; } = string.Empty;
    public DateTime Tanggal { get; set; }
    public string JudulPembelian { get; set; } = string.Empty;
    public string? NoPoPr { get; set; }
    public decimal Nilai { get; set; }
    public string? IdVendor { get; set; }
    public string? LinkPembelian { get; set; }
    public string? Bagian { get; set; }
    public string StatusPurchase { get; set; } = string.Empty;
    public DateTime? TanggalBast { get; set; }
    public DateTime? TanggalSaGr { get; set; }
    public DateTime? TanggalInvoice { get; set; }
    public DateTime? TanggalPaymentApproval { get; set; }
    public DateTime? TanggalPaid { get; set; }
    public string? CatatanStatus { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public VendorSummaryDto? Vendor { get; set; }
    public string? DokumenPendukung { get; set; }
}

public class VendorSummaryDto
{
    public string NamaVendor { get; set; } = string.Empty;
}

public class CreatePadiDto
{
    public string NoPembelian { get; set; } = string.Empty;
    public DateTime Tanggal { get; set; }
    public string JudulPembelian { get; set; } = string.Empty;
    public string? NoPoPr { get; set; }
    public decimal Nilai { get; set; }
    public string? IdVendor { get; set; }
    public string? LinkPembelian { get; set; }
    public string? Bagian { get; set; }
    public string StatusPurchase { get; set; } = "BAST";
    public DateTime? TanggalBast { get; set; }
    public DateTime? TanggalSaGr { get; set; }
    public DateTime? TanggalInvoice { get; set; }
    public DateTime? TanggalPaymentApproval { get; set; }
    public DateTime? TanggalPaid { get; set; }
    public string? CatatanStatus { get; set; }
    public string? DokumenPendukung { get; set; }
}

public class UpdatePadiDto : CreatePadiDto { }

// ===================== KONFIGURASI =====================
public class KonfigurasiDto
{
    public string IdSetting { get; set; } = string.Empty;
    public string NamaSetting { get; set; } = string.Empty;
    public string NilaiSetting { get; set; } = string.Empty;
    public string? Deskripsi { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpdateKonfigurasiDto
{
    public string NilaiSetting { get; set; } = string.Empty;
}

// ===================== PROGRESS LUMPSUM =====================
public class ProgressLumpsumDto
{
    public string IdProgress { get; set; } = string.Empty;
    public string IdKontrak { get; set; } = string.Empty;
    public string Milestone { get; set; } = string.Empty;
    public double Persen { get; set; }
    public DateTime TanggalUpdate { get; set; }
    public string? Evidence { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class CreateProgressLumpsumDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public string Milestone { get; set; } = string.Empty;
    public double Persen { get; set; }
    public DateTime TanggalUpdate { get; set; }
    public string? Evidence { get; set; }
}

public class UpdateProgressLumpsumDto : CreateProgressLumpsumDto { }

// ===================== PROGRESS UNIT PRICE =====================
public class ProgressUnitPriceDto
{
    public string IdProgress { get; set; } = string.Empty;
    public string IdKontrak { get; set; } = string.Empty;
    public string NamaItem { get; set; } = string.Empty;
    public string Satuan { get; set; } = string.Empty;
    public double QtyRencana { get; set; }
    public double QtyAktual { get; set; }
    public decimal HargaSatuan { get; set; }
    public DateTime TanggalUpdate { get; set; }
    public DateTime? CreatedAt { get; set; }
        public double TotalRencana { get; set; }   // ← tambah ini
    public double TotalAktual { get; set; }    // ← tambah ini
}

public class CreateProgressUnitPriceDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public string NamaItem { get; set; } = string.Empty;
    public string Satuan { get; set; } = string.Empty;
    public double QtyRencana { get; set; }
    public double QtyAktual { get; set; }
    public decimal HargaSatuan { get; set; }
    public DateTime TanggalUpdate { get; set; }
}

public class UpdateProgressUnitPriceDto : CreateProgressUnitPriceDto { }

// ===================== MONITORING LTSA =====================
public class MonitoringLtsaDto
{
    public string IdLog { get; set; } = string.Empty;
    public string IdKontrak { get; set; } = string.Empty;
    public DateTime TanggalKunjungan { get; set; }
    public string JenisLayanan { get; set; } = string.Empty;
    public double DurasiJam { get; set; }
    public string SlaTerpenuhi { get; set; } = string.Empty;
    public string? Keterangan { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class CreateMonitoringLtsaDto
{
    public string IdKontrak { get; set; } = string.Empty;
    public DateTime TanggalKunjungan { get; set; }
    public string JenisLayanan { get; set; } = string.Empty;
    public double DurasiJam { get; set; }
    public string SlaTerpenuhi { get; set; } = "Yes";
    public string? Keterangan { get; set; }
}

public class UpdateMonitoringLtsaDto : CreateMonitoringLtsaDto { }

// ===================== PROFILE =====================
public class ProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public string? IdVendor { get; set; }
    public bool IsActive { get; set; } = true;
}


public class AssignVendorDto
{
    public string IdVendor { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? CurrentPassword { get; set; }  // ← tambah ini
    public string? NewPassword { get; set; }       // ← tambah ini
}

public class UpdateRoleDto
{
    public string Role { get; set; } = string.Empty;
}

public class AdminUpdateUserDto
{
    public string? FullName { get; set; }
    public string? Role { get; set; }
}


public class UpdateSCurveDto
{
    public string? SCurveData { get; set; }
}



public class UpdateProgressDto
{
    public double? ProgressPlan { get; set; }
    public double? ProgressActual { get; set; }
}


public class DokumenApprovalDto
{
    public string IdDokumen { get; set; } = string.Empty;
    public string IdKontrak { get; set; } = string.Empty;
    public string TipeDokumen { get; set; } = string.Empty;
    public string NamaDokumen { get; set; } = string.Empty;
    public string? DeskripsiDokumen { get; set; }
    public string? FileUrl { get; set; }
    public string? NamaFile { get; set; }
    public string? TipeFile { get; set; }
    public long? UkuranFile { get; set; }
    public string StatusApproval { get; set; } = string.Empty;
    public string? CatatanReviewer { get; set; }
    public string? UploadedBy { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? JudulKontrak { get; set; }
}
 
public class ReviewDokumenDto
{
    public string StatusApproval { get; set; } = string.Empty; // Approved / Rejected
    public string? CatatanReviewer { get; set; }
    public string? ReviewedBy { get; set; }
}

public class SlaSettingDto
{
    public string KodeTahap { get; set; } = string.Empty;
    public int BatasHari { get; set; }
    public int WarningPersen { get; set; }
}

public class UpdateSlaSettingDto
{
    public int    BatasHari     { get; set; }
    public int WarningPersen { get; set; }
}

public class SlaTagihanDto
{
    public int Id { get; set; }
 
    // Samakan tipe dengan entity (string kalau IdKontrak/IdTagihan string, Guid kalau Guid)
    public string IdKontrak { get; set; } = string.Empty;
    public string? IdTagihan { get; set; }
 
    // Tahap 1: LKP
    public DateTime? TglMasukLkp { get; set; }
    public DateTime? TglSelesaiLkp { get; set; }
 
    // Tahap 2: PUNCHLIST
    public DateTime? TglMasukPunchlist { get; set; }
    public DateTime? TglSelesaiPunchlist { get; set; }
 
    // Tahap 3: BAST
    public DateTime? TglMasukBast { get; set; }
    public DateTime? TglSelesaiBast { get; set; }
 
    // Tahap 4: BAKP
    public DateTime? TglMasukBakp { get; set; }
    public DateTime? TglSelesaiBakp { get; set; }
 
    // Tahap 5: IVENDOR
    public DateTime? TglMasukIvendor { get; set; }
    public DateTime? TglSelesaiIvendor { get; set; }
 
    // Tahap 6: SA
    public DateTime? TglMasukSa { get; set; }
    public DateTime? TglSelesaiSa { get; set; }
 
    // Tahap 7: PA
    public DateTime? TglMasukPa { get; set; }
    public DateTime? TglSelesaiPa { get; set; }
 
    // Tahap 8: VERIFIKASI
    public DateTime? TglMasukVerifikasi { get; set; }
    public DateTime? TglSelesaiVerifikasi { get; set; }
 
    // Tahap 9: PAYMENT
    public DateTime? TglMasukPayment { get; set; }
    public DateTime? TglSelesaiPayment { get; set; }
 
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}