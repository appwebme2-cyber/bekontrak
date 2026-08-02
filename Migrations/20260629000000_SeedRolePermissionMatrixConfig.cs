using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RefineryContractAPI.Migrations
{
    /// <inheritdoc />
    public partial class SeedRolePermissionMatrixConfig : Migration
    {
        private const string RolePermissionMatrixId = "f47e2b5e-2a3b-4c1d-9a6e-0b1c2d3e4f5a";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "konfigurasi_sistem",
                columns: new[] { "id_setting", "nama_setting", "nilai_setting", "deskripsi", "updated_at" },
                values: new object[]
                {
                    RolePermissionMatrixId,
                    "Role_Permission_Matrix",
                    "{}",
                    "Matriks hak akses per role, dikelola dari halaman Pengaturan Role",
                    DateTime.UtcNow
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "konfigurasi_sistem",
                keyColumn: "id_setting",
                keyValue: RolePermissionMatrixId);
        }
    }
}
