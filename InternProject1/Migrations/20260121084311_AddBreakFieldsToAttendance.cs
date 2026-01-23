using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakFieldsToAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_Employee_ID",
                table: "Attendances");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_Employee_ID",
                table: "Attendances");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "ClockInTime",
                table: "Attendances",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeSpan),
                oldType: "time");
        }
    }
}
