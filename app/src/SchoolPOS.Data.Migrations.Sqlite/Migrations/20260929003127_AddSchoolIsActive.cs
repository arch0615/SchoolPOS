using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Schools",
                type: "INTEGER",
                nullable: false,
                // true, no false: las escuelas ya existentes deben seguir activas tras aplicar esta
                // migración — el default de EF para una columna nueva es false, y eso habría
                // desactivado de golpe a cada escuela ya dada de alta.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Schools");
        }
    }
}
