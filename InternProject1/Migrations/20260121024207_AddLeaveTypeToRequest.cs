using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeToRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeaveType",
                table: "LeaveRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveType",
                table: "LeaveRequests");
        }
    }
}
