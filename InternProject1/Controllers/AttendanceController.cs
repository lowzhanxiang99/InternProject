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

        // Helper method to get current user ID
        private int GetCurrentUserId()
        {
            // For now, return a default user ID (e.g., 1)
            // You should replace this with your actual authentication logic
            var userIdClaim = HttpContext?.User?.FindFirst("UserId");
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }

            // Default to user ID 1 for testing
            return 1;
        }

        public async Task<IActionResult> Index()
        {
            int employeeId = GetCurrentUserId();

            var attendanceRecords = await _context.Attendances
                .Where(a => a.Employee_ID == employeeId)
                .OrderByDescending(a => a.Date)
                .Take(20)
                .ToListAsync();

            return View(attendanceRecords);
        }

        [HttpPost]
        public async Task<IActionResult> ClockIn(double latitude, double longitude)
        {
            int employeeId = GetCurrentUserId();

            var todayAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == DateTime.Today);

            if (todayAttendance != null)
            {
                TempData["Error"] = "You have already clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            var attendance = new Attendance
            {
                Employee_ID = employeeId,
                Date = DateTime.Today,
                ClockInTime = DateTime.Now.TimeOfDay,
                Location_Lat_Long = $"{latitude},{longitude}",
                Status = DateTime.Now.TimeOfDay.Hours >= 9 ? "Late" : "Present",
                IsOnBreak = false,
                HasTakenBreak = false,
                TotalBreakTime = TimeSpan.Zero
            };

            _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Successfully clocked in!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ClockOut(double latitude, double longitude)
        {
            int employeeId = GetCurrentUserId();

            var todayAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == DateTime.Today);

            if (todayAttendance == null)
            {
                TempData["Error"] = "You haven't clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            if (todayAttendance.ClockOutTime != null)
            {
                TempData["Error"] = "You have already clocked out today!";
                return RedirectToAction(nameof(Index));
            }

            todayAttendance.ClockOutTime = DateTime.Now.TimeOfDay;
            todayAttendance.Location_Lat_Long += $";{latitude},{longitude}";

            // If user was on break when checking out, end the break
            if (todayAttendance.IsOnBreak)
            {
                var breakDuration = DateTime.Now - todayAttendance.BreakStartTime.Value;
                todayAttendance.TotalBreakTime = (todayAttendance.TotalBreakTime ?? TimeSpan.Zero) + breakDuration;
                todayAttendance.IsOnBreak = false;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Successfully clocked out!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> StartBreak()
        {
            int employeeId = GetCurrentUserId();

            var todayAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == DateTime.Today);

            if (todayAttendance == null)
            {
                TempData["Error"] = "You haven't clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            if (todayAttendance.ClockOutTime != null)
            {
                TempData["Error"] = "You have already clocked out today!";
                return RedirectToAction(nameof(Index));
            }

            // Only allow starting break if not already on break
            if (todayAttendance.IsOnBreak)
            {
                TempData["Error"] = "You are already on break!";
                return RedirectToAction(nameof(Index));
            }

            todayAttendance.IsOnBreak = true;
            todayAttendance.BreakStartTime = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Break started successfully!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> EndBreak()
        {
            int employeeId = GetCurrentUserId();

            var todayAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == DateTime.Today);

            if (todayAttendance == null)
            {
                TempData["Error"] = "You haven't clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            if (!todayAttendance.IsOnBreak)
            {
                TempData["Error"] = "You are not currently on break!";
                return RedirectToAction(nameof(Index));
            }

            // Calculate break duration
            var breakDuration = DateTime.Now - todayAttendance.BreakStartTime.Value;

            // Update attendance record
            todayAttendance.IsOnBreak = false;
            todayAttendance.TotalBreakTime = (todayAttendance.TotalBreakTime ?? TimeSpan.Zero) + breakDuration;
            todayAttendance.HasTakenBreak = true;
            todayAttendance.BreakStartTime = null; // Clear break start time

            await _context.SaveChangesAsync();
            TempData["Success"] = "Break ended successfully!";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetAttendanceDetails(DateTime date)
        {
            try
            {
                int userId = GetCurrentUserId();

                // Find attendance for the specific date and user
                var attendance = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.Employee_ID == userId && a.Date.Date == date.Date);

                if (attendance == null)
                {
                    return Json(new
                    {
                        success = true,
                        hasAttendance = false,
                        message = "No attendance record found"
                    });
                }

                // Calculate working hours if both clock in and out exist
                TimeSpan? workingHours = null;
                if (attendance.ClockInTime.HasValue && attendance.ClockOutTime.HasValue)
                {
                    var totalDuration = attendance.ClockOutTime.Value - attendance.ClockInTime.Value;
                    var breakTime = attendance.TotalBreakTime ?? TimeSpan.Zero;
                    workingHours = totalDuration - breakTime;
                }

                return Json(new
                {
                    success = true,
                    hasAttendance = true,
                    attendance = new
                    {
                        id = attendance.Attendance_ID,
                        clockInTime = attendance.ClockInTime?.ToString(@"hh\:mm\:ss"),
                        clockOutTime = attendance.ClockOutTime?.ToString(@"hh\:mm\:ss"),
                        totalBreakTime = attendance.TotalBreakTime.HasValue ? new
                        {
                            hours = attendance.TotalBreakTime.Value.Hours,
                            minutes = attendance.TotalBreakTime.Value.Minutes,
                            seconds = attendance.TotalBreakTime.Value.Seconds
                        } : null,
                        workingHours = workingHours.HasValue ? new
                        {
                            hours = workingHours.Value.Hours,
                            minutes = workingHours.Value.Minutes,
                            seconds = workingHours.Value.Seconds
                        } : null,
                        status = attendance.Status,
                        isOnBreak = attendance.IsOnBreak
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}