using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiSchoolPortalAndSyncWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntilUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RefreshToken",
                table: "SchoolPaymentAccounts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "SchoolPaymentAccounts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedToCloudAtUtc",
                table: "BalanceMovements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BalanceMovements_SyncedToCloudAtUtc_CreatedAtUtc",
                table: "BalanceMovements",
                columns: new[] { "SyncedToCloudAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BalanceMovements_SyncedToCloudAtUtc_CreatedAtUtc",
                table: "BalanceMovements");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SyncedToCloudAtUtc",
                table: "BalanceMovements");

            migrationBuilder.AlterColumn<string>(
                name: "RefreshToken",
                table: "SchoolPaymentAccounts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "SchoolPaymentAccounts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);
        }
    }
}
