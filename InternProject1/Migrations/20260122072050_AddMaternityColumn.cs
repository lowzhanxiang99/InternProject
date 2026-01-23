using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddMaternityColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaternityLeaveDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaternityLeaveDays",
                table: "Employees");
        }
    }
}
