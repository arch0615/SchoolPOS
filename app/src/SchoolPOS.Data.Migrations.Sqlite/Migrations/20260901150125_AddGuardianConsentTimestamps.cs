using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardianConsentTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedPrivacyAtUtc",
                table: "Guardians",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedTermsAtUtc",
                table: "Guardians",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedPrivacyAtUtc",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AcceptedTermsAtUtc",
                table: "Guardians");
        }
    }
}
