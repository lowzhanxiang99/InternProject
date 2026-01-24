using ClosedXML.Excel;
using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SelectPdf;

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
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (isAdmin == "true")
                return RedirectToAction("Index");

            return View();
        }

        // POST: Monitoring/AdminLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdminLogin(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email and password are required.");
                return View();
            }

            if (email == "admin@gmail.com" && password == "admin123")
            {
                HttpContext.Session.SetString("IsAdmin", "true");
                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Invalid admin credentials or insufficient permissions.");
            return View();
        }

        // GET: Monitoring/Index (Monitoring Dashboard) - FIXED: Removed AdminID check
        public async Task<IActionResult> Index()
        {
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (isAdmin != "true")
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

        // AJAX: Get attendance details for specific date - FIXED: Removed AdminID check
        [HttpGet]
        public async Task<IActionResult> GetAttendanceForDate(DateTime date)
        {
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (isAdmin != "true")
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

        public async Task<IActionResult> AttendanceOverview(
    DateTime? dateFrom,
    DateTime? dateTo,
    string searchTerm = "",
    string sortBy = "Date",
    string sortOrder = "desc",
    int pageSize = 10,
    int currentPage = 1)
        {
            // ADDED: Admin check
            var isAdmin = HttpContext.Session.GetString("IsAdmin");
            if (isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            // If no dates provided, default to current month
            if (!dateFrom.HasValue)
                dateFrom = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            if (!dateTo.HasValue)
                dateTo = DateTime.Now.Date;

            try
            {
                // Start with base query
                var query = _context.Attendances
                    .Include(a => a.Employee)
                    .ThenInclude(e => e.Department)
                    .AsQueryable();

                // Apply date filters
                query = query.Where(a => a.Date >= dateFrom.Value && a.Date <= dateTo.Value);

                // Apply search filter
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    var term = searchTerm.ToLower();
                    query = query.Where(a =>
                        (a.Employee.First_Name + " " + a.Employee.Last_Name).ToLower().Contains(term) ||
                        (a.Employee.Department != null && a.Employee.Department.Department_Name.ToLower().Contains(term)) ||
                        (a.Status != null && a.Status.ToLower().Contains(term)));
                }

                // Apply sorting
                switch (sortBy)
                {
                    case "Employee_ID":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.Employee_ID)
                            : query.OrderByDescending(a => a.Employee_ID);
                        break;

                    case "EmployeeName":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.Employee.First_Name).ThenBy(a => a.Employee.Last_Name)
                            : query.OrderByDescending(a => a.Employee.First_Name).ThenByDescending(a => a.Employee.Last_Name);
                        break;

                    case "Department":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.Employee.Department.Department_Name)
                            : query.OrderByDescending(a => a.Employee.Department.Department_Name);
                        break;

                    case "Date":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.Date)
                            : query.OrderByDescending(a => a.Date);
                        break;

                    case "Status":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.Status)
                            : query.OrderByDescending(a => a.Status);
                        break;

                    case "ClockInTime":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.ClockInTime)
                            : query.OrderByDescending(a => a.ClockInTime);
                        break;

                    case "ClockOutTime":
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.ClockOutTime)
                            : query.OrderByDescending(a => a.ClockOutTime);
                        break;

                    case "WorkHours":
                        // For WorkHours, we need to calculate the duration
                        query = sortOrder == "asc"
                            ? query.OrderBy(a => a.ClockOutTime.HasValue && a.ClockInTime.HasValue ?
                                (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours : 0)
                            : query.OrderByDescending(a => a.ClockOutTime.HasValue && a.ClockInTime.HasValue ?
                                (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours : 0);
                        break;

                    default:
                        query = query.OrderByDescending(a => a.Date);
                        break;
                }

                // Get total count for pagination
                var totalRecords = await query.CountAsync();

                // Get paginated data
                var attendanceRecords = await query
                    .Skip((currentPage - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Calculate total pages
                var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                // Pass data to view using ViewBag/ViewData
                ViewBag.DateFrom = dateFrom;
                ViewBag.DateTo = dateTo;
                ViewBag.SearchTerm = searchTerm;
                ViewBag.SortBy = sortBy;
                ViewBag.SortOrder = sortOrder;
                ViewBag.PageSize = pageSize;
                ViewBag.CurrentPage = currentPage;
                ViewBag.TotalRecords = totalRecords;
                ViewBag.TotalPages = totalPages;
                ViewBag.AttendanceRecords = attendanceRecords;
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Error loading attendance data: {ex.Message}");

                // Return empty data on error
                ViewBag.AttendanceRecords = new List<Attendance>();
                ViewBag.TotalRecords = 0;
                ViewBag.TotalPages = 0;
                ViewBag.SortBy = sortBy;
                ViewBag.SortOrder = sortOrder;
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ExportToExcel(
    DateTime? dateFrom,
    DateTime? dateTo,
    string searchTerm = "")
        {
            // ADDED: Admin check
            var isAdmin = HttpContext.Session.GetString("IsAdmin");
            if (isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            try
            {
                // Get filtered attendance data
                var attendanceRecords = await GetFilteredAttendanceData(dateFrom, dateTo, searchTerm);

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Attendance Report");
                    var currentRow = 1;

                    // Headers for YOUR data format
                    worksheet.Cell(currentRow, 1).Value = "Employee ID";
                    worksheet.Cell(currentRow, 2).Value = "Employee Name";
                    worksheet.Cell(currentRow, 3).Value = "Department";
                    worksheet.Cell(currentRow, 4).Value = "Date";
                    worksheet.Cell(currentRow, 5).Value = "Status";
                    worksheet.Cell(currentRow, 6).Value = "Check In";
                    worksheet.Cell(currentRow, 7).Value = "Check Out";
                    worksheet.Cell(currentRow, 8).Value = "Work Hours";

                    // Style headers (optional - like your friend's style)
                    var headerRange = worksheet.Range(currentRow, 1, currentRow, 8);
                    headerRange.Style.Font.Bold = true;

                    // Add data
                    foreach (var record in attendanceRecords)
                    {
                        currentRow++;
                        worksheet.Cell(currentRow, 1).Value = record.Employee_ID;
                        worksheet.Cell(currentRow, 2).Value = record.Employee?.First_Name + " " + record.Employee?.Last_Name;
                        worksheet.Cell(currentRow, 3).Value = record.Employee?.Department?.Department_Name ?? "-";
                        worksheet.Cell(currentRow, 4).Value = record.Date.ToString("MMM dd, yyyy");
                        worksheet.Cell(currentRow, 5).Value = record.Status ?? "-";
                        worksheet.Cell(currentRow, 6).Value = record.ClockInTime?.ToString(@"hh\:mm") ?? "-";
                        worksheet.Cell(currentRow, 7).Value = record.ClockOutTime?.ToString(@"hh\:mm") ?? "-";

                        // Calculate work hours
                        double workHours = 0;
                        if (record.ClockInTime.HasValue && record.ClockOutTime.HasValue)
                        {
                            workHours = Math.Max(0, (record.ClockOutTime.Value - record.ClockInTime.Value).TotalHours);
                            workHours = Math.Round(workHours, 1);
                        }
                        worksheet.Cell(currentRow, 8).Value = $"{workHours:0.0} hrs";
                    }

                    // Auto-fit columns (like your friend's code)
                    worksheet.Columns().AdjustToContents();

                    // Add filter info at the bottom (optional)
                    currentRow += 2;
                    if (dateFrom.HasValue && dateTo.HasValue)
                    {
                        worksheet.Cell(currentRow, 1).Value = $"Report Period: {dateFrom.Value:MMM dd, yyyy} to {dateTo.Value:MMM dd, yyyy}";
                        currentRow++;
                    }
                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        worksheet.Cell(currentRow, 1).Value = $"Search Term: {searchTerm}";
                        currentRow++;
                    }
                    worksheet.Cell(currentRow, 1).Value = $"Total Records: {attendanceRecords.Count}";
                    worksheet.Cell(currentRow, 2).Value = $"Generated: {DateTime.Now:MMM dd, yyyy HH:mm}";

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        // Simple filename like your friend's
                        var fileName = $"Attendance_Report_{DateTime.Now:yyyyMMdd}.xlsx";
                        return File(stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            fileName);
                    }
                }

            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error generating Excel file: {ex.Message}";
                return RedirectToAction("AttendanceOverview", new { dateFrom, dateTo, searchTerm });
            }
        }

        // Helper method to get filtered data
        private async Task<List<Attendance>> GetFilteredAttendanceData(
            DateTime? dateFrom,
            DateTime? dateTo,
            string searchTerm = "")
        {
            // If no dates provided, default to current month
            if (!dateFrom.HasValue)
                dateFrom = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            if (!dateTo.HasValue)
                dateTo = DateTime.Now.Date;

            // Start with base query
            var query = _context.Attendances
                .Include(a => a.Employee)
                .ThenInclude(e => e.Department)
                .AsQueryable();

            // Apply date filters
            query = query.Where(a => a.Date >= dateFrom.Value && a.Date <= dateTo.Value);

            // Apply search filter
            if (!string.IsNullOrEmpty(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(a =>
                    (a.Employee.First_Name + " " + a.Employee.Last_Name).ToLower().Contains(term) ||
                    (a.Employee.Department != null && a.Employee.Department.Department_Name.ToLower().Contains(term)) ||
                    (a.Status != null && a.Status.ToLower().Contains(term)));
            }

            // Get all records
            return await query
                .OrderByDescending(a => a.Date)
                .ThenBy(a => a.Employee.First_Name)
                .ToListAsync();
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