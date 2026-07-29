using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuardianNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianNotificationPreferences",
                columns: table => new
                {
                    GuardianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LowBalance = table.Column<bool>(type: "bit", nullable: false),
                    TopUpConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PurchaseMade = table.Column<bool>(type: "bit", nullable: false),
                    DailySummary = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianNotificationPreferences", x => x.GuardianId);
                    table.ForeignKey(
                        name: "FK_GuardianNotificationPreferences_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuardianNotificationPreferences");
        }
    }
}
