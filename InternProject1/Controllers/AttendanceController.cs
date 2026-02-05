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
        private int? GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserID");
        }

        public async Task<IActionResult> Index(int? month = null, int? year = null)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            int employeeId = userId.Value;

            // If month/year not specified, use current
            month = month ?? DateTime.Now.Month;
            year = year ?? DateTime.Now.Year;

            // Get all attendance records for the employee
            var allAttendance = await _context.Attendances
                .Where(a => a.Employee_ID == employeeId)
                .OrderByDescending(a => a.Date)
                .ToListAsync();

            ViewBag.SelectedMonth = month;
            ViewBag.SelectedYear = year;

            return View(allAttendance);
        }

        // AJAX endpoint for sorting and pagination
        [HttpGet]
        public async Task<IActionResult> GetSortedAttendance(
            int page = 1,
            string sort = "date",
            string order = "desc")
        {
            try
            {
                var userId = GetCurrentUserId();

                if (!userId.HasValue)
                {
                    // For AJAX calls, return JSON with redirect info instead of RedirectToAction
                    return Json(new
                    {
                        success = false,
                        message = "Session expired. Please log in again.",
                        redirect = "/Account/Login"
                    });
                }

                int employeeId = userId.Value;

                // Get all past attendance records for the employee
                var allAttendance = await _context.Attendances
                    .Where(a => a.Employee_ID == employeeId)
                    .ToListAsync();

                // Apply sorting
                if (!string.IsNullOrEmpty(sort) && allAttendance.Any())
                {
                    allAttendance = sort.ToLower() switch
                    {
                        "date" => order == "asc"
                            ? allAttendance.OrderBy(a => a.Date).ToList()
                            : allAttendance.OrderByDescending(a => a.Date).ToList(),
                        "timein" => order == "asc"
                            ? allAttendance.OrderBy(a => a.ClockInTime.HasValue ? a.ClockInTime.Value.Ticks : long.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a => a.ClockInTime.HasValue ? a.ClockInTime.Value.Ticks : long.MinValue).ToList(),
                        "timeout" => order == "asc"
                            ? allAttendance.OrderBy(a => a.ClockOutTime.HasValue ? a.ClockOutTime.Value.Ticks : long.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a => a.ClockOutTime.HasValue ? a.ClockOutTime.Value.Ticks : long.MinValue).ToList(),
                        "breakhours" => order == "asc"
                            ? allAttendance.OrderBy(a => a.TotalBreakTime.HasValue ? a.TotalBreakTime.Value.TotalSeconds : double.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a => a.TotalBreakTime.HasValue ? a.TotalBreakTime.Value.TotalSeconds : double.MinValue).ToList(),
                        "workinghours" => order == "asc"
                            ? allAttendance.OrderBy(a =>
                                (a.ClockInTime.HasValue && a.ClockOutTime.HasValue)
                                    ? (a.ClockOutTime.Value - a.ClockInTime.Value - (a.TotalBreakTime ?? TimeSpan.Zero)).TotalSeconds
                                    : double.MaxValue).ToList()
                            : allAttendance.OrderByDescending(a =>
                                (a.ClockInTime.HasValue && a.ClockOutTime.HasValue)
                                    ? (a.ClockOutTime.Value - a.ClockInTime.Value - (a.TotalBreakTime ?? TimeSpan.Zero)).TotalSeconds
                                    : double.MinValue).ToList(),
                        _ => allAttendance.OrderByDescending(a => a.Date).ToList()
                    };
                }

                // Pagination
                int pageSize = 10;
                int totalRecords = allAttendance.Count;
                int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                if (page < 1) page = 1;
                if (page > totalPages && totalPages > 0) page = totalPages;

                var recentAttendance = allAttendance
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new
                    {
                        id = a.Attendance_ID,
                        date = a.Date.ToString("MMM dd, yyyy"),
                        clockInTime = a.ClockInTime.HasValue ?
                            FormatTime(a.ClockInTime.Value) : "-",
                        clockOutTime = a.ClockOutTime.HasValue ?
                            FormatTime(a.ClockOutTime.Value) : "-",
                        breakTime = a.TotalBreakTime.HasValue ?
                            FormatDuration(a.TotalBreakTime.Value) : "-",
                        workingTime = CalculateWorkingTime(a),
                        status = a.Status
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = recentAttendance,
                    page = page,
                    totalPages = totalPages,
                    totalRecords = totalRecords,
                    sort = sort,
                    order = order
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error loading attendance data: " + ex.Message
                });
            }
        }

        private string FormatTime(TimeSpan time)
        {
            var dateTime = DateTime.Today.Add(time);
            return dateTime.ToString("hh:mm tt");
        }

        private string FormatDuration(TimeSpan duration)
        {
            // Round seconds to avoid decimal values
            int seconds = (int)Math.Round(duration.Seconds + (duration.Milliseconds / 1000.0));
            return $"{duration.Hours} Hr {duration.Minutes:00} Mins {seconds:00} Secs";
        }

        private string CalculateWorkingTime(Attendance attendance)
        {
            if (!attendance.ClockInTime.HasValue || !attendance.ClockOutTime.HasValue)
            {
                return "-";
            }

            var totalDuration = attendance.ClockOutTime.Value - attendance.ClockInTime.Value;
            var breakTime = attendance.TotalBreakTime ?? TimeSpan.Zero;
            var workingTime = totalDuration - breakTime;

            return FormatDuration(workingTime);
        }

        [HttpPost]
        public async Task<IActionResult> ClockIn(double latitude, double longitude)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            int employeeId = userId.Value;

            var todayAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == DateTime.Today);

            if (todayAttendance != null)
            {
                TempData["Error"] = "You have already clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            // Get employee with shift and include shift schedules
            var employee = await _context.Employees
                .Include(e => e.Shift)
                    .ThenInclude(s => s.Schedules)
                .FirstOrDefaultAsync(e => e.Employee_ID == employeeId);

            if (employee == null)
            {
                TempData["Error"] = "Employee not found!";
                return RedirectToAction(nameof(Index));
            }

            Shift shiftToUse;
            bool usingDefaultShift = false;

            // Check if employee has assigned shift
            if (employee.Shift != null)
            {
                shiftToUse = employee.Shift;
                usingDefaultShift = employee.UsingDefaultShift;
            }
            else
            {
                // Get default shift from database with schedules
                shiftToUse = await _context.Shifts
                    .Include(s => s.Schedules)
                    .FirstOrDefaultAsync(s => s.Is_Default);

                // If no default exists, create one
                if (shiftToUse == null)
                {
                    shiftToUse = new Shift
                    {
                        Shift_Name = "Standard Office Hours",
                        Start_Time = new TimeSpan(9, 0, 0),
                        End_Time = new TimeSpan(18, 0, 0),
                        Is_Default = true,
                        Description = "System-generated default shift"
                    };
                    _context.Shifts.Add(shiftToUse);
                    await _context.SaveChangesAsync();
                }

                // Auto-assign default shift to employee
                employee.Shift_ID = shiftToUse.Shift_ID;
                employee.UsingDefaultShift = true;
                employee.ShiftAssignedDate = null;
                usingDefaultShift = true;

                _context.Employees.Update(employee);
            }

            // GET SCHEDULED TIMES FOR TODAY (considers ShiftSchedule)
            var (scheduledStartTime, scheduledEndTime) = shiftToUse.GetTimeForDate(DateTime.Today);

            // SHIFT-BASED ON TIME/LATE LOGIC using scheduled times with 1-minute grace period
            TimeSpan currentTime = DateTime.Now.TimeOfDay;

            // Add 1-minute grace period: On Time if within 59 seconds after scheduled start
            var graceEndTime = scheduledStartTime.Add(TimeSpan.FromMinutes(1));
            string status = currentTime < graceEndTime ? "On Time" : "Late";

            // CREATE ATTENDANCE RECORD with scheduled times
            var attendance = new Attendance
            {
                Employee_ID = employeeId,
                Date = DateTime.Today,
                ClockInTime = currentTime,
                Location_Lat_Long = $"{latitude},{longitude}",
                Status = status,
                IsOnBreak = false,
                HasTakenBreak = false,
                TotalBreakTime = TimeSpan.Zero,
                Expected_Start = scheduledStartTime,  // Use scheduled time (may differ from base shift)
                Expected_End = scheduledEndTime,      // Use scheduled time (may differ from base shift)
                Shift_Used = shiftToUse.Shift_Name,
                Shift_ID_Used = shiftToUse.Shift_ID,
                Used_Default_Shift = usingDefaultShift
            };

            _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();

            // Show message with scheduled times
            string timeMessage = $"Clocked in at {currentTime:hh\\:mm\\:ss}";
            string shiftMessage = $"{shiftToUse.Shift_Name} ({scheduledStartTime:hh\\:mm}-{scheduledEndTime:hh\\:mm})";

            // Check if times differ from base shift
            bool isDifferentSchedule = scheduledStartTime != shiftToUse.Start_Time ||
                                       scheduledEndTime != shiftToUse.End_Time;
            string scheduleNote = isDifferentSchedule ? " [Custom Schedule]" : "";

            if (status == "Late")
            {
                // Calculate late time from grace end (not from scheduled start)
                TimeSpan lateBy = currentTime - graceEndTime;
                TempData["Warning"] = $"{timeMessage}. You arrived late for your shift: {shiftMessage}{scheduleNote}";
            }
            else
            {
                TempData["Success"] = $"{timeMessage}. Great! You're right on time for your shift: {shiftMessage}{scheduleNote}";
            }

            if (usingDefaultShift)
            {
                if (TempData["Warning"] != null)
                    TempData["Warning"] += " (Using default shift)";
                else if (TempData["Success"] != null)
                    TempData["Success"] += " (Using default shift)";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ClockOut(double latitude, double longitude)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            int employeeId = userId.Value;

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
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            int employeeId = userId.Value;

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
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            int employeeId = userId.Value;

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

            await _context.SaveChangesAsync();
            TempData["Success"] = "Break ended successfully!";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetAttendanceDetails(DateTime date)
        {
            try
            {
                var userId = GetCurrentUserId();

                if (!userId.HasValue)
                {
                    return RedirectToAction("Login", "Account");
                }

                int employeeId = userId.Value;

                // Find attendance for the specific date and user
                var attendance = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.Employee_ID == employeeId && a.Date.Date == date.Date);

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