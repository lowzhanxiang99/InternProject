using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Controllers
{
    public class MonitoringController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MonitoringController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Monitoring/AdminLogin
        public IActionResult AdminLogin()
        {
            // Check session instead of User.Identity
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId != null && isAdmin == "true")
                return RedirectToAction("Index");

            return View();
        }

        // POST: Monitoring/AdminLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminLogin(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email and password are required.");
                return View();
            }

            // Find admin user
            var admin = await _context.Employees
                .FirstOrDefaultAsync(e => e.Employee_Email == email && e.Password == password && e.Role == "Admin");

            if (admin != null)
            {
                HttpContext.Session.SetInt32("AdminID", admin.Employee_ID);
                HttpContext.Session.SetString("AdminName", $"{admin.First_Name} {admin.Last_Name}");
                HttpContext.Session.SetString("AdminEmail", admin.Employee_Email);
                HttpContext.Session.SetString("AdminRole", admin.Role);
                HttpContext.Session.SetString("IsAdmin", "true");

                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Invalid admin credentials or insufficient permissions.");
            return View();
        }

        // GET: Monitoring/Index (Monitoring Dashboard) - Protected - Admin only
        public async Task<IActionResult> Index()
        {
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId == null || isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            // Get all employees with their departments and shifts
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Shift)
                .OrderBy(e => e.Department != null ? e.Department.Department_Name : "ZZZ")
                .ThenBy(e => e.First_Name)
                .ToListAsync();

            // Get today's attendance for ALL employees
            var today = DateTime.Today;
            var todayAttendance = await _context.Attendances
                .Where(a => a.Date.Date == today)
                .ToListAsync();

            // ========== TODAY'S CALCULATIONS ==========
            var totalEmployees = employees.Count;

            // Get unique employee IDs who clocked in today
            var clockedInEmployeeIds = todayAttendance
                .Where(a => a.ClockInTime != null)
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            var presentCount = clockedInEmployeeIds.Count;
            var absentCount = totalEmployees - presentCount;

            // Calculate on-time and late counts
            int onTimeCount = 0, lateCount = 0;

            foreach (var employeeId in clockedInEmployeeIds)
            {
                var firstClockIn = todayAttendance
                    .Where(a => a.Employee_ID == employeeId && a.ClockInTime != null)
                    .OrderBy(a => a.ClockInTime)
                    .FirstOrDefault();

                if (firstClockIn != null)
                {
                    if (firstClockIn.ClockInTime.Value.Hours < 9 ||
                        (firstClockIn.ClockInTime.Value.Hours == 9 &&
                         firstClockIn.ClockInTime.Value.Minutes == 0))
                    {
                        onTimeCount++;
                    }
                    else
                    {
                        lateCount++;
                    }
                }
            }

            // Calculate early departures
            var earlyDeparturesCount = todayAttendance.Count(a =>
            a.ClockInTime != null &&
            a.ClockOutTime != null &&
            a.ClockOutTime.Value.Hours < 18); // Before 6:00 PM

            // ========== HISTORICAL DATA FOR CHARTS ==========

            // 1. Get last week's data (7 days ago to yesterday)
            var lastWeekStart = today.AddDays(-7);
            var lastWeekEnd = today.AddDays(-1);

            var lastWeekAttendance = await _context.Attendances
                .Where(a => a.Date >= lastWeekStart && a.Date <= lastWeekEnd)
                .ToListAsync();

            // Calculate last week statistics
            var lastWeekEmployeeIds = lastWeekAttendance
                .Where(a => a.ClockInTime != null)
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            var lastWeekPresentCount = lastWeekEmployeeIds.Count;
            var lastWeekTotalPossible = 7 * totalEmployees; // 7 days × total employees
            var lastWeekAttendanceRate = lastWeekTotalPossible > 0 ?
                Math.Round((double)lastWeekPresentCount / lastWeekTotalPossible * 100, 1) : 0;

            // 2. Get current week data for weekly chart (Monday to Sunday)
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            var weeklyData = new List<DailyAttendance>();

            for (int i = 0; i < 7; i++)
            {
                var date = startOfWeek.AddDays(i);
                var dayAttendance = await _context.Attendances
                    .Where(a => a.Date.Date == date.Date)
                    .ToListAsync();

                var present = dayAttendance.Count(a => a.ClockInTime != null);
                var late = dayAttendance.Count(a => a.ClockInTime != null &&
                    (a.ClockInTime.Value.Hours > 9 ||
                    (a.ClockInTime.Value.Hours == 9 && a.ClockInTime.Value.Minutes > 0)));
                var absent = totalEmployees - present;

                weeklyData.Add(new DailyAttendance
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    Present = present,
                    Late = late,
                    Absent = absent
                });
            }

            // 3. Get last month's data for comparison
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var startOfLastMonth = startOfMonth.AddMonths(-1);
            var endOfLastMonth = startOfMonth.AddDays(-1);

            var lastMonthAttendance = await _context.Attendances
                .Where(a => a.Date >= startOfLastMonth && a.Date <= endOfLastMonth)
                .ToListAsync();

            var lastMonthEmployeeIds = lastMonthAttendance
                .Where(a => a.ClockInTime != null)
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            var lastMonthPresentCount = lastMonthEmployeeIds.Count;
            var daysInLastMonth = (endOfLastMonth - startOfLastMonth).Days + 1;
            var lastMonthTotalPossible = daysInLastMonth * totalEmployees;
            var lastMonthAttendanceRate = lastMonthTotalPossible > 0 ?
                Math.Round((double)lastMonthPresentCount / lastMonthTotalPossible * 100, 1) : 0;

            // ========== TREND CALCULATIONS ==========
            string trendText;
            string trendDirection;

            var lastVisitCount = HttpContext.Session.GetInt32("LastEmployeeCount");

            if (lastVisitCount.HasValue)
            {
                var newEmployees = totalEmployees - lastVisitCount.Value;

                if (newEmployees > 0)
                {
                    trendText = $"+{newEmployees} new employee{(newEmployees > 1 ? "s" : "")}";
                    trendDirection = "up";
                }
                else if (newEmployees < 0)
                {
                    trendText = $"{newEmployees} employee{(Math.Abs(newEmployees) > 1 ? "s" : "")} removed";
                    trendDirection = "down";
                }
                else
                {
                    trendText = $"{totalEmployees} active employees";
                    trendDirection = "neutral";
                }
            }
            else
            {
                trendText = $"{totalEmployees} active employees";
                trendDirection = "neutral";
            }

            HttpContext.Session.SetInt32("LastEmployeeCount", totalEmployees);

            // ========== CREATE VIEW MODEL ==========
            var viewModel = new EmployeeMonitoringViewModel
            {
                Employees = employees,
                TodayAttendance = todayAttendance,
                SelectedDate = today,

                // Today's statistics
                TotalEmployees = totalEmployees,
                PresentCount = presentCount,
                AbsentCount = absentCount,
                LateCount = lateCount,
                OnTimeCount = onTimeCount,
                EarlyDeparturesCount = earlyDeparturesCount,

                // Trend data
                EmployeeTrend = trendText,
                TrendDirection = trendDirection,

                // Chart data
                TodayAttendanceRate = totalEmployees > 0 ?
                    Math.Round((double)presentCount / totalEmployees * 100, 1) : 0,
                LastWeekAttendanceRate = lastWeekAttendanceRate,
                LastMonthAttendanceRate = lastMonthAttendanceRate,
                LastWeekPresentCount = lastWeekPresentCount,
                LastMonthPresentCount = lastMonthPresentCount,
                WeeklyData = weeklyData
            };

            return View(viewModel);
        }

        // AJAX: Get attendance details for specific date
        [HttpGet]
        public async Task<IActionResult> GetAttendanceForDate(DateTime date)
        {
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId == null || isAdmin != "true")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                // First get all attendance for the date
                var attendance = await _context.Attendances
                    .Where(a => a.Date.Date == date.Date)
                    .ToListAsync();

                // Get all employee IDs from attendance
                var employeeIds = attendance.Select(a => a.Employee_ID).Distinct().ToList();

                // Get employees with their departments
                var employees = await _context.Employees
                    .Include(e => e.Department)
                    .Include(e => e.Shift)
                    .Where(e => employeeIds.Contains(e.Employee_ID))
                    .ToListAsync();

                // Combine the data
                var result = attendance.Select(a => new
                {
                    attendance = a,
                    employee = employees.FirstOrDefault(e => e.Employee_ID == a.Employee_ID)
                }).ToList();

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
    public class DailyAttendance
    {
        public DateTime Date { get; set; }
        public string DayName { get; set; }
        public int Present { get; set; }
        public int Late { get; set; }
        public int Absent { get; set; }
    }
}