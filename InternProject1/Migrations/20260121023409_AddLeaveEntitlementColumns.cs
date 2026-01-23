using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveEntitlementColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnnualLeaveDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmergencyLeaveDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MCDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OtherLeaveDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualLeaveDays",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmergencyLeaveDays",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MCDays",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OtherLeaveDays",
                table: "Employees");
        }
    }
}
