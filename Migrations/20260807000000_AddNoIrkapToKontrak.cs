using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RefineryContractAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddNoIrkapToKontrak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "no_irkap",
                table: "kontrak",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "no_irkap",
                table: "kontrak");
        }
    }
}
