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

        // GET: Monitoring/Index (Monitoring Dashboard)
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

            // ========== TIME-OFF CALCULATION ==========
            var onLeaveEmployeeIds = await _context.LeaveRequests
                .Where(l => l.Status.ToLower() == "approve" &&
                           l.Start_Date.Date <= today.Date &&
                           l.End_Date.Date >= today.Date)
                .Select(l => l.Employee_ID)
                .Distinct()
                .ToListAsync();

            var timeOffCount = onLeaveEmployeeIds.Count;

            // ========== TODAY'S CALCULATIONS ==========
            var totalEmployees = employees.Count;

            var clockedInEmployeeIds = todayAttendance
                .Where(a => a.ClockInTime != null && !onLeaveEmployeeIds.Contains(a.Employee_ID))
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            var presentCount = clockedInEmployeeIds.Count;

            // Calculate absent count ONLY if today is a working day
            int absentCount;

            if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                // Sunday: Everyone is "not working", not "absent"
                absentCount = 0;
            }
            else
            {
                // Working day: Calculate normal absent count
                absentCount = totalEmployees - timeOffCount - presentCount;
            }

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
                !onLeaveEmployeeIds.Contains(a.Employee_ID) &&
                (
                    (today.DayOfWeek >= DayOfWeek.Monday && today.DayOfWeek <= DayOfWeek.Friday &&
                     a.ClockOutTime.Value.Hours < 18) ||
                    (today.DayOfWeek == DayOfWeek.Saturday &&
                     a.ClockOutTime.Value.Hours < 13)
                ));

            // ========== WEEKLY DATA FOR CHARTS (FIXED) ==========

            // 1. Get LAST week data (Monday to Sunday of last week)
            int daysFromMondayLast = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var startOfLastWeek = today.AddDays(-daysFromMondayLast - 7);

            var lastWeekData = new List<DailyAttendance>();

            for (int i = 0; i < 7; i++)
            {
                var date = startOfLastWeek.AddDays(i);

                // Get attendance for this specific date
                var dayAttendance = await _context.Attendances
                    .Where(a => a.Date.Date == date.Date)
                    .ToListAsync();

                // Get employees on leave for this specific date
                var onLeaveThisDay = await _context.LeaveRequests
                    .Where(l => (l.Status.ToLower() == "approve") &&
                               l.Start_Date.Date <= date.Date &&
                               l.End_Date.Date >= date.Date)
                    .Select(l => l.Employee_ID)
                    .Distinct()
                    .ToListAsync();

                // ALSO check Attendance table status for leave
                var onLeaveFromAttendance = dayAttendance
                    .Where(a => a.Status != null &&
                           (a.Status.ToLower().Contains("leave")))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .ToList();

                // Combine both sources
                var allOnLeaveThisDay = onLeaveThisDay.Union(onLeaveFromAttendance).Distinct().ToList();

                // Count unique employees who clocked in (excluding those on leave)
                var uniquePresentEmployees = dayAttendance
                    .Where(a => a.ClockInTime != null && !allOnLeaveThisDay.Contains(a.Employee_ID))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .Count();

                var uniqueLateEmployees = dayAttendance
                    .Where(a => a.ClockInTime != null &&
                        !allOnLeaveThisDay.Contains(a.Employee_ID) &&
                        (a.ClockInTime.Value.Hours > 9 ||
                        (a.ClockInTime.Value.Hours == 9 && a.ClockInTime.Value.Minutes > 0)))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .Count();

                // FIXED: Absent = Total - Present - On Leave
                var absent = totalEmployees - uniquePresentEmployees - allOnLeaveThisDay.Count;

                // If it's Sunday, set absent = 0
                if (date.DayOfWeek == DayOfWeek.Sunday)
                {
                    absent = 0;
                }

                lastWeekData.Add(new DailyAttendance
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    Present = uniquePresentEmployees,
                    Late = uniqueLateEmployees,
                    Absent = absent
                });
            }

            // 2. Get CURRENT week data (Monday to Sunday of this week)
            int daysFromMondayCurrent = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var startOfCurrentWeek = today.AddDays(-daysFromMondayCurrent);

            var currentWeekData = new List<DailyAttendance>();

            for (int i = 0; i < 7; i++)
            {
                var date = startOfCurrentWeek.AddDays(i);

                int uniquePresentEmployees = 0;
                int uniqueLateEmployees = 0;
                int absent = 0;

                // ONLY process data for past dates and today
                if (date.Date <= today.Date)
                {
                    var dayAttendance = await _context.Attendances
                        .Where(a => a.Date.Date == date.Date)
                        .ToListAsync();

                    // Get employees on leave for this specific date
                    var onLeaveThisDay = await _context.LeaveRequests
                        .Where(l => (l.Status.ToLower() == "approve") &&
                                   l.Start_Date.Date <= date.Date &&
                                   l.End_Date.Date >= date.Date)
                        .Select(l => l.Employee_ID)
                        .Distinct()
                        .ToListAsync();

                    // ALSO check Attendance table status for leave
                    var onLeaveFromAttendance = dayAttendance
                        .Where(a => a.Status != null &&
                               (a.Status.ToLower().Contains("leave") ||
                                a.Status.ToLower() == "on leave" ||
                                a.Status.ToLower() == "time off"))
                        .Select(a => a.Employee_ID)
                        .Distinct()
                        .ToList();

                    // Combine both sources
                    var allOnLeaveThisDay = onLeaveThisDay.Union(onLeaveFromAttendance).Distinct().ToList();

                    uniquePresentEmployees = dayAttendance
                        .Where(a => a.ClockInTime != null && !allOnLeaveThisDay.Contains(a.Employee_ID))
                        .Select(a => a.Employee_ID)
                        .Distinct()
                        .Count();

                    uniqueLateEmployees = dayAttendance
                        .Where(a => a.ClockInTime != null &&
                            !allOnLeaveThisDay.Contains(a.Employee_ID) &&
                            (a.ClockInTime.Value.Hours > 9 ||
                            (a.ClockInTime.Value.Hours == 9 && a.ClockInTime.Value.Minutes > 0)))
                        .Select(a => a.Employee_ID)
                        .Distinct()
                        .Count();

                    // Absent = Total - Present - On Leave
                    absent = totalEmployees - uniquePresentEmployees - allOnLeaveThisDay.Count;

                    // If it's Sunday, absent = 0
                    if (date.DayOfWeek == DayOfWeek.Sunday)
                    {
                        absent = 0;
                    }
                }
                // Future dates: all values remain at defaults (0 present, 0 late, all absent)

                currentWeekData.Add(new DailyAttendance
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    Present = uniquePresentEmployees,
                    Late = uniqueLateEmployees,
                    Absent = absent
                });
            }

            // 3. Get last month's data
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var startOfLastMonth = startOfMonth.AddMonths(-1);
            var endOfLastMonth = startOfMonth.AddDays(-1);

            var lastMonthAttendance = await _context.Attendances
                .Where(a => a.Date >= startOfLastMonth && a.Date <= endOfLastMonth)
                .ToListAsync();

            var lastMonthPresentCount = lastMonthAttendance
                .Where(a => a.ClockInTime != null)
                .Select(a => new { a.Employee_ID, a.Date.Date })
                .Distinct()
                .Count();

            var daysInLastMonth = (endOfLastMonth - startOfLastMonth).Days + 1;
            var lastMonthTotalPossible = daysInLastMonth * totalEmployees;

            var lastMonthAttendanceRate = lastMonthTotalPossible > 0 ?
                Math.Round((double)lastMonthPresentCount / lastMonthTotalPossible * 100, 1) : 0;

            // ========== CALCULATE LAST WEEK METRICS ==========
            var lastWeekPresentCount = lastWeekData.Sum(d => d.Present);
            var lastWeekWorkingDays = lastWeekData.Count(d => d.Date.DayOfWeek != DayOfWeek.Sunday);
            var lastWeekTotalPossible = lastWeekWorkingDays * totalEmployees;

            var lastWeekAttendanceRate = lastWeekTotalPossible > 0 ?
                Math.Round((double)lastWeekPresentCount / lastWeekTotalPossible * 100, 1) : 0;

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
                TotalEmployees = totalEmployees,
                PresentCount = presentCount,
                AbsentCount = absentCount,
                LateCount = lateCount,
                OnTimeCount = onTimeCount,
                EarlyDeparturesCount = earlyDeparturesCount,
                TimeOffCount = timeOffCount,
                EmployeeTrend = trendText,
                TrendDirection = trendDirection,
                TodayAttendanceRate = totalEmployees > 0 ?
                    Math.Round((double)presentCount / totalEmployees * 100, 1) : 0,
                LastWeekAttendanceRate = lastWeekAttendanceRate,
                LastMonthAttendanceRate = lastMonthAttendanceRate,
                LastWeekPresentCount = lastWeekPresentCount,
                LastMonthPresentCount = lastMonthPresentCount,
                WeeklyData = currentWeekData,
                LastWeekData = lastWeekData
            };

            return View(viewModel);
        }

        // AJAX: Get attendance details for specific date
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

                // Declare variables for results
                int totalRecords;
                List<Attendance> attendanceRecords;
                int totalPages;

                // SPECIAL HANDLING FOR WORKHOURS SORTING
                if (sortBy == "WorkHours")
                {
                    // Load all filtered data first (before sorting)
                    var allData = await query.ToListAsync();

                    // Separate records with and without work hours
                    var withHours = allData.Where(a => a.ClockInTime.HasValue && a.ClockOutTime.HasValue).ToList();
                    var withoutHours = allData.Where(a => !a.ClockInTime.HasValue || !a.ClockOutTime.HasValue).ToList();

                    // Sort records with hours
                    List<Attendance> sortedWithHours;
                    if (sortOrder == "asc")
                    {
                        sortedWithHours = withHours
                            .OrderBy(a => (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours)
                            .ToList();
                    }
                    else
                    {
                        sortedWithHours = withHours
                            .OrderByDescending(a => (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours)
                            .ToList();
                    }

                    // Combine: sorted records first, then nulls at the end
                    var sortedData = sortedWithHours.Concat(withoutHours).ToList();

                    // Get total count for pagination
                    totalRecords = sortedData.Count;

                    // Apply pagination to sorted data
                    attendanceRecords = sortedData
                        .Skip((currentPage - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    // Calculate total pages
                    totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
                }
                else
                {
                    // Apply sorting for all OTHER columns (not WorkHours)
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

                        default:
                            query = query.OrderByDescending(a => a.Date);
                            break;
                    }

                    // Get total count for pagination
                    totalRecords = await query.CountAsync();

                    // Get paginated data
                    attendanceRecords = await query
                        .Skip((currentPage - 1) * pageSize)
                        .Take(pageSize)
                        .ToListAsync();

                    // Calculate total pages
                    totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
                }

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
    string searchTerm = "",
    string sortBy = "Date",
    string sortOrder = "desc")
        {
            // ADDED: Admin check
            var isAdmin = HttpContext.Session.GetString("IsAdmin");
            if (isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            try
            {
                // Get filtered attendance data with sorting (now includes sortBy and sortOrder)
                var attendanceRecords = await GetFilteredAttendanceData(dateFrom, dateTo, searchTerm, sortBy, sortOrder);

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

                        // Calculate work hours - FIXED to show "-" for null
                        if (record.ClockInTime.HasValue && record.ClockOutTime.HasValue)
                        {
                            double workHours = Math.Max(0, (record.ClockOutTime.Value - record.ClockInTime.Value).TotalHours);
                            workHours = Math.Round(workHours, 1);
                            worksheet.Cell(currentRow, 8).Value = $"{workHours:0.0} hrs";
                        }
                        else
                        {
                            worksheet.Cell(currentRow, 8).Value = "-";
                        }
                    }

                    // Auto-fit columns
                    worksheet.Columns().AdjustToContents();

                    // Add filter info at the bottom
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

                    // ADDED: Show sort information
                    if (!string.IsNullOrEmpty(sortBy) && sortBy != "Date")
                    {
                        worksheet.Cell(currentRow, 1).Value = $"Sorted By: {sortBy} ({sortOrder.ToUpper()})";
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
                return RedirectToAction("AttendanceOverview", new { dateFrom, dateTo, searchTerm, sortBy, sortOrder });
            }
        }

        // Helper method to get filtered data
        private async Task<List<Attendance>> GetFilteredAttendanceData(
            DateTime? dateFrom,
            DateTime? dateTo,
            string searchTerm = "",
            string sortBy = "Date",
            string sortOrder = "desc")
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

            // SPECIAL HANDLING FOR WORKHOURS SORTING
            if (sortBy == "WorkHours")
            {
                // Load all data first
                var allData = await query.ToListAsync();

                // Separate records with and without work hours
                var withHours = allData.Where(a => a.ClockInTime.HasValue && a.ClockOutTime.HasValue).ToList();
                var withoutHours = allData.Where(a => !a.ClockInTime.HasValue || !a.ClockOutTime.HasValue).ToList();

                // Sort records with hours
                List<Attendance> sortedWithHours;
                if (sortOrder == "asc")
                {
                    sortedWithHours = withHours
                        .OrderBy(a => (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours)
                        .ToList();
                }
                else
                {
                    sortedWithHours = withHours
                        .OrderByDescending(a => (a.ClockOutTime.Value - a.ClockInTime.Value).TotalHours)
                        .ToList();
                }

                // Combine: sorted records first, then nulls at the end
                return sortedWithHours.Concat(withoutHours).ToList();
            }

            // Apply sorting for other columns (same logic as in AttendanceOverview)
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
                default:
                    query = query.OrderByDescending(a => a.Date);
                    break;
            }

            // Get all records
            return await query.ToListAsync();
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