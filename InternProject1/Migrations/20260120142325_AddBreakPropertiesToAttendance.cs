using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakPropertiesToAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_Employee_ID",
                table: "Attendances");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_Employee_ID",
                table: "Attendances");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Attendances",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Location_Lat_Long",
                table: "Attendances",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "ClockInTime",
                table: "Attendances",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeSpan),
                oldType: "time");

            migrationBuilder.AddColumn<DateTime>(
                name: "BreakStartTime",
                table: "Attendances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasTakenBreak",
                table: "Attendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnBreak",
                table: "Attendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TotalBreakTime",
                table: "Attendances",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakStartTime",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "HasTakenBreak",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "IsOnBreak",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "TotalBreakTime",
                table: "Attendances");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Location_Lat_Long",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "ClockInTime",
                table: "Attendances",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0),
                oldClrType: typeof(TimeSpan),
                oldType: "time",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_Employee_ID",
                table: "Attendances",
                column: "Employee_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_Employee_ID",
                table: "Attendances",
                column: "Employee_ID",
                principalTable: "Employees",
                principalColumn: "Employee_ID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
