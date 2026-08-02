using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RefineryContractAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSCurveData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "konfigurasi_sistem",
                columns: table => new
                {
                    id_setting = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    nama_setting = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    nilai_setting = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    deskripsi = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_konfigurasi_sistem", x => x.id_setting);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendor",
                columns: table => new
                {
                    id_vendor = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    nama_vendor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    npwp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    alamat = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pic_nama = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pic_kontak = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status_vendor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    score = table.Column<double>(type: "float", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor", x => x.id_vendor);
                });

            migrationBuilder.CreateTable(
                name: "kontrak",
                columns: table => new
                {
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_vendor = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    judul_kontrak = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    no_dokumen_kontrak = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    no_po_pr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    direksi_pekerjaan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tipe_kontrak = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status_kontrak = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tanggal_spb_diterima = table.Column<DateTime>(type: "datetime2", nullable: false),
                    tanggal_terima_dokumen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_maksimal_kom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_mulai = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_selesai = table.Column<DateTime>(type: "datetime2", nullable: true),
                    sla_kom_hari = table.Column<int>(type: "int", nullable: false),
                    estimasi_tanggal_kom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    tanggal_kom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    kom_terlambat = table.Column<bool>(type: "bit", nullable: false),
                    nilai_awal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    durasi_kontrak_hari = table.Column<int>(type: "int", nullable: true),
                    progress_plan = table.Column<double>(type: "float", nullable: true),
                    progress_actual = table.Column<double>(type: "float", nullable: true),
                    aktivitas_saat_ini = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    kendala = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    disiplin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tkdn_percentage = table.Column<double>(type: "float", nullable: true),
                    tanggal_lkp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    has_amendment = table.Column<bool>(type: "bit", nullable: false),
                    no_amandemen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tanggal_amandemen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    jenis_amandemen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nilai_kontrak_baru = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    durasi_amandemen = table.Column<int>(type: "int", nullable: true),
                    tanggal_mulai_baru = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_selesai_baru = table.Column<DateTime>(type: "datetime2", nullable: true),
                    alasan_perubahan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    contract_documents = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    amendment_documents = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    s_curve_data = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kontrak", x => x.id_kontrak);
                    table.ForeignKey(
                        name: "FK_kontrak_vendor_id_vendor",
                        column: x => x.id_vendor,
                        principalTable: "vendor",
                        principalColumn: "id_vendor",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "padi",
                columns: table => new
                {
                    id_padi = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    no_pembelian = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tanggal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    judul_pembelian = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    no_po_pr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nilai = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    id_vendor = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    link_pembelian = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    bagian = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dokumen_pendukung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status_purchase = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tanggal_bast = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_sa_gr = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_invoice = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_payment_approval = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_paid = table.Column<DateTime>(type: "datetime2", nullable: true),
                    catatan_status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_padi", x => x.id_padi);
                    table.ForeignKey(
                        name: "FK_padi_vendor_id_vendor",
                        column: x => x.id_vendor,
                        principalTable: "vendor",
                        principalColumn: "id_vendor");
                });

            migrationBuilder.CreateTable(
                name: "amandemen_kontrak",
                columns: table => new
                {
                    id_amandemen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    nomor_urut = table.Column<int>(type: "int", nullable: false),
                    no_amandemen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tanggal_amandemen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    jenis_amandemen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nilai_kontrak_baru = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    durasi_amandemen = table.Column<int>(type: "int", nullable: true),
                    tanggal_mulai_baru = table.Column<DateTime>(type: "datetime2", nullable: true),
                    tanggal_selesai_baru = table.Column<DateTime>(type: "datetime2", nullable: true),
                    alasan_perubahan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    amendment_documents = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_amandemen_kontrak", x => x.id_amandemen);
                    table.ForeignKey(
                        name: "FK_amandemen_kontrak_kontrak_id_kontrak",
                        column: x => x.id_kontrak,
                        principalTable: "kontrak",
                        principalColumn: "id_kontrak",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_ltsa",
                columns: table => new
                {
                    id_log = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    tanggal_kunjungan = table.Column<DateTime>(type: "datetime2", nullable: false),
                    jenis_layanan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    durasi_jam = table.Column<double>(type: "float", nullable: false),
                    sla_terpenuhi = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    keterangan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_ltsa", x => x.id_log);
                    table.ForeignKey(
                        name: "FK_monitoring_ltsa_kontrak_id_kontrak",
                        column: x => x.id_kontrak,
                        principalTable: "kontrak",
                        principalColumn: "id_kontrak",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "progress_lumpsum",
                columns: table => new
                {
                    id_progress = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    milestone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    persen = table.Column<double>(type: "float", nullable: false),
                    tanggal_update = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress_lumpsum", x => x.id_progress);
                    table.ForeignKey(
                        name: "FK_progress_lumpsum_kontrak_id_kontrak",
                        column: x => x.id_kontrak,
                        principalTable: "kontrak",
                        principalColumn: "id_kontrak",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "progress_unit_price",
                columns: table => new
                {
                    id_progress = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    nama_item = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    satuan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    qty_rencana = table.Column<double>(type: "float", nullable: false),
                    qty_aktual = table.Column<double>(type: "float", nullable: false),
                    harga_satuan = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    tanggal_update = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress_unit_price", x => x.id_progress);
                    table.ForeignKey(
                        name: "FK_progress_unit_price_kontrak_id_kontrak",
                        column: x => x.id_kontrak,
                        principalTable: "kontrak",
                        principalColumn: "id_kontrak",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tagihan",
                columns: table => new
                {
                    id_tagihan = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_kontrak = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    nomor_tagihan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tanggal_tagihan = table.Column<DateTime>(type: "datetime2", nullable: false),
                    tipe_kontrak = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    termin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nilai_tagihan = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status_tagihan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    memo_required = table.Column<bool>(type: "bit", nullable: false),
                    tanggal_pengiriman_memo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    dokumen_memo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dokumen_tagihan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    catatan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tagihan", x => x.id_tagihan);
                    table.ForeignKey(
                        name: "FK_tagihan_kontrak_id_kontrak",
                        column: x => x.id_kontrak,
                        principalTable: "kontrak",
                        principalColumn: "id_kontrak",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_amandemen_kontrak_id_kontrak",
                table: "amandemen_kontrak",
                column: "id_kontrak");

            migrationBuilder.CreateIndex(
                name: "IX_kontrak_id_vendor",
                table: "kontrak",
                column: "id_vendor");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_ltsa_id_kontrak",
                table: "monitoring_ltsa",
                column: "id_kontrak");

            migrationBuilder.CreateIndex(
                name: "IX_padi_id_vendor",
                table: "padi",
                column: "id_vendor");

            migrationBuilder.CreateIndex(
                name: "IX_progress_lumpsum_id_kontrak",
                table: "progress_lumpsum",
                column: "id_kontrak");

            migrationBuilder.CreateIndex(
                name: "IX_progress_unit_price_id_kontrak",
                table: "progress_unit_price",
                column: "id_kontrak");

            migrationBuilder.CreateIndex(
                name: "IX_tagihan_id_kontrak",
                table: "tagihan",
                column: "id_kontrak");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "amandemen_kontrak");

            migrationBuilder.DropTable(
                name: "konfigurasi_sistem");

            migrationBuilder.DropTable(
                name: "monitoring_ltsa");

            migrationBuilder.DropTable(
                name: "padi");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "progress_lumpsum");

            migrationBuilder.DropTable(
                name: "progress_unit_price");

            migrationBuilder.DropTable(
                name: "tagihan");

            migrationBuilder.DropTable(
                name: "kontrak");

            migrationBuilder.DropTable(
                name: "vendor");
        }
    }
}
