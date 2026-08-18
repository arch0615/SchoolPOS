using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class CashTopUpOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "TopUps",
                type: "int",
                nullable: false,
                // 1 = TopUpOrigin.Gateway: todo lo que existe hoy vino de la pasarela. Con el 0 por
                // omisión quedarían en un valor inválido y saldrían de los reportes de comisión.
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                table: "TopUps");
        }
    }
}
