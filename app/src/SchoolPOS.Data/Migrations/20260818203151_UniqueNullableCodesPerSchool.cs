using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueNullableCodesPerSchool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_SchoolId_CardCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Products_SchoolId_Barcode",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Students_SchoolId_CardCode",
                table: "Students",
                columns: new[] { "SchoolId", "CardCode" },
                unique: true,
                filter: "[CardCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SchoolId_Barcode",
                table: "Products",
                columns: new[] { "SchoolId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_SchoolId_CardCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Products_SchoolId_Barcode",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Students_SchoolId_CardCode",
                table: "Students",
                columns: new[] { "SchoolId", "CardCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SchoolId_Barcode",
                table: "Products",
                columns: new[] { "SchoolId", "Barcode" },
                unique: true);
        }
    }
}
