using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InternProject1.Controllers
{
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AttendanceController> _logger;

        public AttendanceController(ApplicationDbContext context, ILogger<AttendanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private int GetCurrentUserId() => 1; // Testing default

        [HttpGet]
        public async Task<IActionResult> GetSortedAttendance(int page = 1, string sort = "date", string order = "desc")
        {
            try
            {
                int employeeId = GetCurrentUserId();
                var allAttendance = await _context.Attendances
                    .Where(a => a.Employee_ID == employeeId && a.Date.Date < DateTime.Today)
                    .ToListAsync();

                if (!string.IsNullOrEmpty(sort) && allAttendance.Any())
                {
                    allAttendance = sort.ToLower() switch
                    {
                        "date" => order == "asc" ? allAttendance.OrderBy(a => a.Date).ToList() : allAttendance.OrderByDescending(a => a.Date).ToList(),
                        // FIX: ClockInTime is TimeSpan. Removed .HasValue and .Value
                        "timein" => order == "asc"
                            ? allAttendance.OrderBy(a => a.ClockInTime.Ticks).ToList()
                            : allAttendance.OrderByDescending(a => a.ClockInTime.Ticks).ToList(),
                        "timeout" => order == "asc"
                            ? allAttendance.OrderBy(a => a.ClockOutTime.HasValue ? a.ClockOutTime.Value.Ticks : long.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a => a.ClockOutTime.HasValue ? a.ClockOutTime.Value.Ticks : long.MinValue).ToList(),
                        "workinghours" => order == "asc"
                            ? allAttendance.OrderBy(a => a.ClockOutTime.HasValue ? (a.ClockOutTime.Value - a.ClockInTime - (a.TotalBreakTime ?? TimeSpan.Zero)).TotalSeconds : double.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a => a.ClockOutTime.HasValue ? (a.ClockOutTime.Value - a.ClockInTime - (a.TotalBreakTime ?? TimeSpan.Zero)).TotalSeconds : double.MinValue).ToList(),
                        _ => allAttendance.OrderByDescending(a => a.Date).ToList()
                    };
                }

                int pageSize = 10;
                var data = allAttendance.Skip((page - 1) * pageSize).Take(pageSize).Select(a => new {
                    id = a.Attendance_ID,
                    date = a.Date.ToString("MMM dd, yyyy"),
                    clockInTime = FormatTime(a.ClockInTime), // Removed .Value
                    clockOutTime = a.ClockOutTime.HasValue ? FormatTime(a.ClockOutTime.Value) : "-",
                    workingTime = CalculateWorkingTime(a)
                }).ToList();

                return Json(new { success = true, data });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        private string FormatTime(TimeSpan time) => DateTime.Today.Add(time).ToString("hh:mm tt");

        private string CalculateWorkingTime(Attendance a)
        {
            // FIX: ClockInTime is accessed directly. ClockOutTime is checked for null.
            if (!a.ClockOutTime.HasValue) return "-";
            var workingTime = (a.ClockOutTime.Value - a.ClockInTime) - (a.TotalBreakTime ?? TimeSpan.Zero);
            return $"{(int)workingTime.TotalHours}h {workingTime.Minutes}m";
        }
    }
}