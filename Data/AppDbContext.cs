using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Profile> Profiles { get; set; }
    public DbSet<Vendor> Vendors { get; set; }
    public DbSet<Kontrak> Kontraks { get; set; }
    public DbSet<AmandemenKontrak> AmandemenKontraks { get; set; }
    public DbSet<Tagihan> Tagihans { get; set; }
    public DbSet<ProgressLumpsum> ProgressLumpsums { get; set; }
    public DbSet<ProgressUnitPrice> ProgressUnitPrices { get; set; }
    public DbSet<MonitoringLtsa> MonitoringLtsas { get; set; }
    public DbSet<Padi> Padis { get; set; }
    public DbSet<KonfigurasiSistem> KonfigurasiSistems { get; set; }
    public DbSet<DokumenApproval> DokumenApprovals { get; set; }
    public DbSet<DailyReport> DailyReports { get; set; }
    public DbSet<SlaSetting> SlaSettings { get; set; }
    public DbSet<SlaTagihan> SlaTagihans { get; set; }
    public DbSet<DireksiPekerjaan> DireksiPekerjaans { get; set; }
    public DbSet<ProgramKerja> ProgramKerjas { get; set; }
    public DbSet<Planner> Planners { get; set; }
    public DbSet<TokenBlacklist> TokenBlacklists { get; set; }



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Table names mapping
        modelBuilder.Entity<Profile>().ToTable("profiles");
        modelBuilder.Entity<SlaSetting>().ToTable("sla_setting");
        modelBuilder.Entity<SlaTagihan>().ToTable("sla_tagihan");
        modelBuilder.Entity<Vendor>().ToTable("vendor");
        modelBuilder.Entity<Kontrak>().ToTable("kontrak");
        modelBuilder.Entity<AmandemenKontrak>().ToTable("amandemen_kontrak");
        modelBuilder.Entity<Tagihan>().ToTable("tagihan");
        modelBuilder.Entity<ProgressLumpsum>().ToTable("progress_lumpsum");
        modelBuilder.Entity<ProgressUnitPrice>().ToTable("progress_unit_price");
        modelBuilder.Entity<MonitoringLtsa>().ToTable("monitoring_ltsa");
        modelBuilder.Entity<Padi>().ToTable("padi");
        modelBuilder.Entity<KonfigurasiSistem>().ToTable("konfigurasi_sistem");
        modelBuilder.Entity<DokumenApproval>().ToTable("dokumen_approval");
        modelBuilder.Entity<DailyReport>().ToTable("daily_report");
        modelBuilder.Entity<DireksiPekerjaan>().ToTable("direksi_pekerjaan");
        modelBuilder.Entity<ProgramKerja>().ToTable("program_kerja");
        modelBuilder.Entity<Planner>().ToTable("planner");
        modelBuilder.Entity<DokumenApproval>(e => {
            e.Property(p => p.IdDokumen).HasColumnName("id_dokumen");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.TipeDokumen).HasColumnName("tipe_dokumen");
            e.Property(p => p.NamaDokumen).HasColumnName("nama_dokumen");
            e.Property(p => p.DeskripsiDokumen).HasColumnName("deskripsi_dokumen");
            e.Property(p => p.FilePath).HasColumnName("file_path");
            e.Property(p => p.FileUrl).HasColumnName("file_url");
            e.Property(p => p.NamaFile).HasColumnName("nama_file");
            e.Property(p => p.TipeFile).HasColumnName("tipe_file");
            e.Property(p => p.UkuranFile).HasColumnName("ukuran_file");
            e.Property(p => p.StatusApproval).HasColumnName("status_approval");
            e.Property(p => p.CatatanReviewer).HasColumnName("catatan_reviewer");
            e.Property(p => p.UploadedBy).HasColumnName("uploaded_by");
            e.Property(p => p.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(p => p.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - Profile
        modelBuilder.Entity<Profile>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Email).HasColumnName("email");
            e.Property(p => p.FullName).HasColumnName("full_name");
            e.Property(p => p.Role).HasColumnName("role");
            e.Property(p => p.PasswordHash).HasColumnName("password_hash");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - SlaSetting
        modelBuilder.Entity<SlaSetting>(e => {
            e.Property(p => p.KodeTahap).HasColumnName("kode_tahap");
            e.Property(p => p.BatasHari).HasColumnName("batas_hari");
            e.Property(p => p.WarningPersen).HasColumnName("warning_persen");
        });

        // Column name mapping - SlaTagihan
        modelBuilder.Entity<SlaTagihan>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.IdTagihan).HasColumnName("id_tagihan");
            e.Property(p => p.TglMasukLkp).HasColumnName("tgl_masuk_lkp");
            e.Property(p => p.TglSelesaiLkp).HasColumnName("tgl_selesai_lkp");
            e.Property(p => p.TglMasukPunchlist).HasColumnName("tgl_masuk_punchlist");
            e.Property(p => p.TglSelesaiPunchlist).HasColumnName("tgl_selesai_punchlist");
            e.Property(p => p.TglMasukBast).HasColumnName("tgl_masuk_bast");
            e.Property(p => p.TglSelesaiBast).HasColumnName("tgl_selesai_bast");
            e.Property(p => p.TglMasukBakp).HasColumnName("tgl_masuk_bakp");
            e.Property(p => p.TglSelesaiBakp).HasColumnName("tgl_selesai_bakp");
            e.Property(p => p.TglMasukIvendor).HasColumnName("tgl_masuk_ivendor");
            e.Property(p => p.TglSelesaiIvendor).HasColumnName("tgl_selesai_ivendor");
            e.Property(p => p.TglMasukSa).HasColumnName("tgl_masuk_sa");
            e.Property(p => p.TglSelesaiSa).HasColumnName("tgl_selesai_sa");
            e.Property(p => p.TglMasukPa).HasColumnName("tgl_masuk_pa");
            e.Property(p => p.TglSelesaiPa).HasColumnName("tgl_selesai_pa");
            e.Property(p => p.TglMasukVerifikasi).HasColumnName("tgl_masuk_verifikasi");
            e.Property(p => p.TglSelesaiVerifikasi).HasColumnName("tgl_selesai_verifikasi");
            e.Property(p => p.TglMasukPayment).HasColumnName("tgl_masuk_payment");
            e.Property(p => p.TglSelesaiPayment).HasColumnName("tgl_selesai_payment");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - Vendor
        modelBuilder.Entity<Vendor>(e => {
            e.Property(p => p.IdVendor).HasColumnName("id_vendor");
            e.Property(p => p.NamaVendor).HasColumnName("nama_vendor");
            e.Property(p => p.Npwp).HasColumnName("npwp");
            e.Property(p => p.Alamat).HasColumnName("alamat");
            e.Property(p => p.PicNama).HasColumnName("pic_nama");
            e.Property(p => p.PicKontak).HasColumnName("pic_kontak");
            e.Property(p => p.StatusVendor).HasColumnName("status_vendor");
            e.Property(p => p.Score).HasColumnName("score");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - Kontrak
        modelBuilder.Entity<Kontrak>(e => {
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.IdVendor).HasColumnName("id_vendor");
            e.Property(p => p.JudulKontrak).HasColumnName("judul_kontrak");
            e.Property(p => p.NoDokumenKontrak).HasColumnName("no_dokumen_kontrak");
            e.Property(p => p.NoPoPr).HasColumnName("no_po_pr");
            e.Property(p => p.NoIrkap).HasColumnName("no_irkap");
            e.Property(p => p.DireksiPekerjaan).HasColumnName("direksi_pekerjaan");
            e.Property(p => p.ProgramKerja).HasColumnName("program_kerja");
            e.Property(p => p.Planner).HasColumnName("planner");
            e.Property(p => p.KboBagian).HasColumnName("kbo_bagian");
            e.Property(p => p.TipeKontrak).HasColumnName("tipe_kontrak");
            e.Property(p => p.StatusKontrak).HasColumnName("status_kontrak");
            e.Property(p => p.TanggalSpbDiterima).HasColumnName("tanggal_spb_diterima");
            e.Property(p => p.TanggalTerimaDokumen).HasColumnName("tanggal_terima_dokumen");
            e.Property(p => p.TanggalMaksimalKom).HasColumnName("tanggal_maksimal_kom");
            e.Property(p => p.TanggalMulai).HasColumnName("tanggal_mulai");
            e.Property(p => p.TanggalSelesai).HasColumnName("tanggal_selesai");
            e.Property(p => p.SlaKomHari).HasColumnName("sla_kom_hari");
            e.Property(p => p.EstimasiTanggalKom).HasColumnName("estimasi_tanggal_kom");
            e.Property(p => p.TanggalKom).HasColumnName("tanggal_kom");
            e.Property(p => p.KomTerlambat).HasColumnName("kom_terlambat");
            e.Property(p => p.NilaiAwal).HasColumnName("nilai_awal");
            e.Property(p => p.DurasiKontrakHari).HasColumnName("durasi_kontrak_hari");
            e.Property(p => p.ProgressPlan).HasColumnName("progress_plan");
            e.Property(p => p.ProgressActual).HasColumnName("progress_actual");
            e.Property(p => p.AktivitasSaatIni).HasColumnName("aktivitas_saat_ini");
            e.Property(p => p.Kendala).HasColumnName("kendala");
            e.Property(p => p.Disiplin).HasColumnName("disiplin");
            e.Property(p => p.TkdnPercentage).HasColumnName("tkdn_percentage");
            e.Property(p => p.TanggalLkp).HasColumnName("tanggal_lkp");
            e.Property(p => p.HasAmendment).HasColumnName("has_amendment");
            e.Property(p => p.NoAmandemen).HasColumnName("no_amandemen");
            e.Property(p => p.TanggalAmandemen).HasColumnName("tanggal_amandemen");
            e.Property(p => p.JenisAmandemen).HasColumnName("jenis_amandemen");
            e.Property(p => p.NilaiKontrakBaru).HasColumnName("nilai_kontrak_baru");
            e.Property(p => p.DurasiAmandemen).HasColumnName("durasi_amandemen");
            e.Property(p => p.TanggalMulaiBaru).HasColumnName("tanggal_mulai_baru");
            e.Property(p => p.TanggalSelesaiBaru).HasColumnName("tanggal_selesai_baru");
            e.Property(p => p.AlasanPerubahan).HasColumnName("alasan_perubahan");
            e.Property(p => p.ContractDocuments).HasColumnName("contract_documents");
            e.Property(p => p.AmendmentDocuments).HasColumnName("amendment_documents");
            e.Property(p => p.SCurveData).HasColumnName("s_curve_data");
            e.Property(p => p.TanggalMpl).HasColumnName("tanggal_mpl");
            e.Property(p => p.TanggalMpa).HasColumnName("tanggal_mpa");
            e.Property(p => p.MasaPemeliharaanHari).HasColumnName("masa_pemeliharaan_hari");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - AmandemenKontrak
        modelBuilder.Entity<AmandemenKontrak>(e => {
            e.Property(p => p.IdAmandemen).HasColumnName("id_amandemen");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.NomorUrut).HasColumnName("nomor_urut");
            e.Property(p => p.NoAmandemen).HasColumnName("no_amandemen");
            e.Property(p => p.TanggalAmandemen).HasColumnName("tanggal_amandemen");
            e.Property(p => p.JenisAmandemen).HasColumnName("jenis_amandemen");
            e.Property(p => p.NilaiKontrakBaru).HasColumnName("nilai_kontrak_baru");
            e.Property(p => p.DurasiAmandemen).HasColumnName("durasi_amandemen");
            e.Property(p => p.TanggalMulaiBaru).HasColumnName("tanggal_mulai_baru");
            e.Property(p => p.TanggalSelesaiBaru).HasColumnName("tanggal_selesai_baru");
            e.Property(p => p.AlasanPerubahan).HasColumnName("alasan_perubahan");
            e.Property(p => p.AmendmentDocuments).HasColumnName("amendment_documents");

            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - Tagihan
        modelBuilder.Entity<Tagihan>(e => {
            e.Property(p => p.IdTagihan).HasColumnName("id_tagihan");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.NomorTagihan).HasColumnName("nomor_tagihan");
            e.Property(p => p.TanggalTagihan).HasColumnName("tanggal_tagihan");
            e.Property(p => p.TipeKontrak).HasColumnName("tipe_kontrak");
            e.Property(p => p.Termin).HasColumnName("termin");
            e.Property(p => p.NilaiTagihan).HasColumnName("nilai_tagihan");
            e.Property(p => p.StatusTagihan).HasColumnName("status_tagihan");
            e.Property(p => p.MemoRequired).HasColumnName("memo_required");
            e.Property(p => p.TanggalPengirimanMemo).HasColumnName("tanggal_pengiriman_memo");
            e.Property(p => p.DokumenMemo).HasColumnName("dokumen_memo");
            e.Property(p => p.DokumenTagihan).HasColumnName("dokumen_tagihan");
            e.Property(p => p.Catatan).HasColumnName("catatan");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - ProgressLumpsum
        modelBuilder.Entity<ProgressLumpsum>(e => {
            e.Property(p => p.IdProgress).HasColumnName("id_progress");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.Milestone).HasColumnName("milestone");
            e.Property(p => p.Persen).HasColumnName("persen");
            e.Property(p => p.TanggalUpdate).HasColumnName("tanggal_update");
            e.Property(p => p.Evidence).HasColumnName("evidence");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
        });

        // Column name mapping - ProgressUnitPrice
        modelBuilder.Entity<ProgressUnitPrice>(e => {
            e.Property(p => p.IdProgress).HasColumnName("id_progress");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.NamaItem).HasColumnName("nama_item");
            e.Property(p => p.Satuan).HasColumnName("satuan");
            e.Property(p => p.QtyRencana).HasColumnName("qty_rencana");
            e.Property(p => p.QtyAktual).HasColumnName("qty_aktual");
            e.Property(p => p.HargaSatuan).HasColumnName("harga_satuan");
            e.Property(p => p.TanggalUpdate).HasColumnName("tanggal_update");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
        });

        // Column name mapping - MonitoringLtsa
        modelBuilder.Entity<MonitoringLtsa>(e => {
            e.Property(p => p.IdLog).HasColumnName("id_log");
            e.Property(p => p.IdKontrak).HasColumnName("id_kontrak");
            e.Property(p => p.TanggalKunjungan).HasColumnName("tanggal_kunjungan");
            e.Property(p => p.JenisLayanan).HasColumnName("jenis_layanan");
            e.Property(p => p.DurasiJam).HasColumnName("durasi_jam");
            e.Property(p => p.SlaTerpenuhi).HasColumnName("sla_terpenuhi");
            e.Property(p => p.Keterangan).HasColumnName("keterangan");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
        });

        // Column name mapping - Padi
        modelBuilder.Entity<Padi>(e => {
            e.Property(p => p.IdPadi).HasColumnName("id_padi");
            e.Property(p => p.NoPembelian).HasColumnName("no_pembelian");
            e.Property(p => p.Tanggal).HasColumnName("tanggal");
            e.Property(p => p.JudulPembelian).HasColumnName("judul_pembelian");
            e.Property(p => p.NoPoPr).HasColumnName("no_po_pr");
            e.Property(p => p.Nilai).HasColumnName("nilai");
            e.Property(p => p.IdVendor).HasColumnName("id_vendor");
            e.Property(p => p.LinkPembelian).HasColumnName("link_pembelian");
            e.Property(p => p.Bagian).HasColumnName("bagian");
            e.Property(p => p.DokumenPendukung).HasColumnName("dokumen_pendukung");
            e.Property(p => p.StatusPurchase).HasColumnName("status_purchase");
            e.Property(p => p.TanggalBast).HasColumnName("tanggal_bast");
            e.Property(p => p.TanggalSaGr).HasColumnName("tanggal_sa_gr");
            e.Property(p => p.TanggalInvoice).HasColumnName("tanggal_invoice");
            e.Property(p => p.TanggalPaymentApproval).HasColumnName("tanggal_payment_approval");
            e.Property(p => p.TanggalPaid).HasColumnName("tanggal_paid");
            e.Property(p => p.CatatanStatus).HasColumnName("catatan_status");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - DailyReport
        modelBuilder.Entity<DailyReport>(e => {
            e.Property(p => p.IdReport).HasColumnName("id_report");
            e.Property(p => p.TanggalLaporan).HasColumnName("tanggal_laporan");
            e.Property(p => p.Disiplin).HasColumnName("disiplin");
            e.Property(p => p.Kategori).HasColumnName("kategori");
            e.Property(p => p.Direksi).HasColumnName("direksi");
            e.Property(p => p.TagNumber).HasColumnName("tag_number");
            e.Property(p => p.Deskripsi).HasColumnName("deskripsi");
            e.Property(p => p.StatusPekerjaan).HasColumnName("status_pekerjaan");
            e.Property(p => p.Catatan).HasColumnName("catatan");
            e.Property(p => p.PengirimWa).HasColumnName("pengirim_wa");
            e.Property(p => p.RawText).HasColumnName("raw_text");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
        });

        // Column name mapping - KonfigurasiSistem
        modelBuilder.Entity<KonfigurasiSistem>(e => {
            e.Property(p => p.IdSetting).HasColumnName("id_setting");
            e.Property(p => p.NamaSetting).HasColumnName("nama_setting");
            e.Property(p => p.NilaiSetting).HasColumnName("nilai_setting");
            e.Property(p => p.Deskripsi).HasColumnName("deskripsi");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - DireksiPekerjaan
        modelBuilder.Entity<DireksiPekerjaan>(e => {
            e.Property(p => p.IdDireksiPekerjaan).HasColumnName("id_direksi_pekerjaan");
            e.Property(p => p.Nama).HasColumnName("nama");
            e.Property(p => p.Jabatan).HasColumnName("jabatan");
            e.Property(p => p.SubArea).HasColumnName("sub_area");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - ProgramKerja
        modelBuilder.Entity<ProgramKerja>(e => {
            e.Property(p => p.IdProgramKerja).HasColumnName("id_program_kerja");
            e.Property(p => p.Nama).HasColumnName("nama");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        // Column name mapping - Planner
        modelBuilder.Entity<Planner>(e => {
            e.Property(p => p.IdPlanner).HasColumnName("id_planner");
            e.Property(p => p.Nama).HasColumnName("nama");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<TokenBlacklist>().ToTable("token_blacklist");
        modelBuilder.Entity<TokenBlacklist>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Jti).HasColumnName("jti");
            e.Property(p => p.ExpiresAt).HasColumnName("expires_at");
            e.Property(p => p.RevokedAt).HasColumnName("revoked_at");
        });
    }
}