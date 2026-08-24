using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolPOS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolBillingEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingEmail",
                table: "Schools",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingEmail",
                table: "Schools");
        }
    }
}
