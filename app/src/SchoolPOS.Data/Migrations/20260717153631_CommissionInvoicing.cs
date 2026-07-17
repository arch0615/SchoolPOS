using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommissionInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CfdiUse",
                table: "Schools",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Schools",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Schools",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rfc",
                table: "Schools",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegime",
                table: "Schools",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissionInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodToUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Uuid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    StampedXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StampedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionInvoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionInvoices_SchoolId_PeriodFromUtc_PeriodToUtc",
                table: "CommissionInvoices",
                columns: new[] { "SchoolId", "PeriodFromUtc", "PeriodToUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommissionInvoices");

            migrationBuilder.DropColumn(
                name: "CfdiUse",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Rfc",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "TaxRegime",
                table: "Schools");
        }
    }
}
