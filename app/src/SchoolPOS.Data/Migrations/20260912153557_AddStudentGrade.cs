using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentGrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "Students",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Grade",
                table: "Students");
        }
    }
}
