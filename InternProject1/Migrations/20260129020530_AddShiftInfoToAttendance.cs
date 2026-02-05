using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternProject1.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftInfoToAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
             migrationBuilder.AddColumn<TimeSpan>(
                name: "Expected_End",
                table: "Attendances",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Expected_Start",
                table: "Attendances",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Shift_ID_Used",
                table: "Attendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift_Used",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Used_Default_Shift",
                table: "Attendances",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Expected_End",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Expected_Start",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Shift_ID_Used",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Shift_Used",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Used_Default_Shift",
                table: "Attendances");
            
        }
    }
}
