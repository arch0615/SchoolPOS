using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleSyncedToCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedToCloudAtUtc",
                table: "Sales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SyncedToCloudAtUtc_CreatedAtUtc",
                table: "Sales",
                columns: new[] { "SyncedToCloudAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_SyncedToCloudAtUtc_CreatedAtUtc",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SyncedToCloudAtUtc",
                table: "Sales");
        }
    }
}
