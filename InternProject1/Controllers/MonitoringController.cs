using ClosedXML.Excel;
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
                HttpContext.Session.SetString("IsAdminAuthenticated", "true");
                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Invalid admin credentials or insufficient permissions.");
            return View();
        }

        // GET: Monitoring/Index (Monitoring Dashboard)
        public async Task<IActionResult> Index()
        {
            var isAdmin = HttpContext.Session.GetString("IsAdminAuthenticated");

            if (isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            // Get all employees with their departments and shifts
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Shift)
                    .ThenInclude(s => s.Schedules)
                .OrderBy(e => e.Department != null ? e.Department.Department_Name : "ZZZ")
                .ThenBy(e => e.First_Name)
                .ToListAsync();

            // Get today's attendance for ALL employees
            var today = DateTime.Today;
            var todayAttendance = await _context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.Shift)
                        .ThenInclude(s => s.Schedules)
                .Where(a => a.Date.Date == today)
                .ToListAsync();

            // ========== TIME-OFF CALCULATION ==========
            var timeOffCount = todayAttendance
                .Where(a => a.Status != null &&
                       (a.Status.ToLower().Contains("leave") || a.Status.ToLower() == "on leave" || a.Status.ToLower().Contains("time off")))
                .Select(a => a.Employee_ID)
                .Distinct()
                .Count();

            // Get employee IDs on leave for filtering
            var onLeaveEmployeeIds = todayAttendance
                .Where(a => a.Status != null &&
                       (a.Status.ToLower().Contains("leave") || a.Status.ToLower() == "on leave" || a.Status.ToLower().Contains("time off")))
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            // ========== TODAY'S CALCULATIONS ==========
            var totalEmployees = employees.Count;

            // Get clocked-in employees (excluding those on leave)
            var clockedInEmployeeIds = todayAttendance
                .Where(a => a.ClockInTime != null && !onLeaveEmployeeIds.Contains(a.Employee_ID))
                .Select(a => a.Employee_ID)
                .Distinct()
                .ToList();

            var presentCount = clockedInEmployeeIds.Count;

            // Calculate absent count ONLY for employees who are NOT on leave
            int absentCount;

            if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                // Sunday: Everyone is "not working", not "absent"
                absentCount = 0;
            }
            else
            {
                // Working day: Calculate absent count only for employees NOT on leave
                var employeesExpectedToWork = totalEmployees - timeOffCount;
                absentCount = Math.Max(0, employeesExpectedToWork - presentCount);
            }

            // Calculate on-time and late counts using shift times WITH SCHEDULE OVERRIDES
            int onTimeCount = 0, lateCount = 0;

            foreach (var employeeId in clockedInEmployeeIds)
            {
                var firstClockIn = todayAttendance
                    .Where(a => a.Employee_ID == employeeId && a.ClockInTime != null)
                    .OrderBy(a => a.ClockInTime)
                    .FirstOrDefault();

                if (firstClockIn != null && firstClockIn.Employee?.Shift != null)
                {
                    var shift = firstClockIn.Employee.Shift;
                    var clockInTime = firstClockIn.ClockInTime.Value;
                    var attendanceDate = firstClockIn.Date.Date;
                    var (shiftStartTimeSpan, shiftEndTimeSpan) = shift.GetTimeForDate(attendanceDate);
                    var shiftStartTime = attendanceDate.Add(shiftStartTimeSpan);
                    var clockInDateTime = attendanceDate.Add(new TimeSpan(clockInTime.Hours, clockInTime.Minutes, clockInTime.Seconds));

                    // Employee is on time if they clock in at or before shift start time
                    var graceEndTime = shiftStartTime.AddMinutes(1);
                    if (clockInDateTime < graceEndTime)
                    {
                        onTimeCount++;
                    }
                    else
                    {
                        lateCount++;
                    }
                }
            }

            // Calculate early departures using shift times WITH SCHEDULE OVERRIDES
            var earlyDeparturesCount = 0;

            foreach (var attendance in todayAttendance.Where(a =>
                a.ClockInTime != null &&
                a.ClockOutTime != null &&
                !onLeaveEmployeeIds.Contains(a.Employee_ID)))
            {
                if (attendance.Employee?.Shift != null)
                {
                    var shift = attendance.Employee.Shift;
                    var clockOutTime = attendance.ClockOutTime.Value;
                    var attendanceDate = attendance.Date.Date;
                    var (shiftStartTimeSpan, shiftEndTimeSpan) = shift.GetTimeForDate(attendanceDate);
                    var shiftEndTime = attendanceDate.Add(shiftEndTimeSpan);
                    var clockOutDateTime = attendanceDate.Add(new TimeSpan(clockOutTime.Hours, clockOutTime.Minutes, clockOutTime.Seconds));

                    // Employee left early if they clocked out before shift end time
                    if (clockOutDateTime < shiftEndTime)
                    {
                        earlyDeparturesCount++;
                    }
                }
            }
            // ========== WEEKLY DATA FOR CHARTS ==========

            // 1. Get LAST week data (Monday to Sunday of last week)
            int daysFromMondayLast = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var startOfLastWeek = today.AddDays(-daysFromMondayLast - 7);

            var lastWeekData = new List<DailyAttendance>();

            for (int i = 0; i < 7; i++)
            {
                var date = startOfLastWeek.AddDays(i);

                // Get attendance for this specific date
                var dayAttendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e.Shift)
                            .ThenInclude(s => s.Schedules)
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
                           (a.Status.ToLower().Contains("leave") || a.Status.ToLower() == "on leave" || a.Status.ToLower().Contains("time off")))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .ToList();

                // Use ONLY attendance table for leave (since you said leave status is in attendance)
                var allOnLeaveThisDay = onLeaveFromAttendance.Distinct().ToList();

                // Count unique employees who clocked in (excluding those on leave)
                var uniquePresentEmployees = dayAttendance
                    .Where(a => a.ClockInTime != null && !allOnLeaveThisDay.Contains(a.Employee_ID))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .ToList();

                var presentThisDay = uniquePresentEmployees.Count;

                // Calculate late count for this day using shift times
                var lateThisDay = 0;
                foreach (var employeeId in uniquePresentEmployees)
                {
                    var firstClockIn = dayAttendance
                        .Where(a => a.Employee_ID == employeeId && a.ClockInTime != null)
                        .OrderBy(a => a.ClockInTime)
                        .FirstOrDefault();

                    if (firstClockIn != null && firstClockIn.Employee?.Shift != null)
                    {
                        var shift = firstClockIn.Employee.Shift;
                        var clockInTime = firstClockIn.ClockInTime.Value;
                        var attendanceDate = firstClockIn.Date.Date;
                        var (shiftStartTimeSpan, shiftEndTimeSpan) = shift.GetTimeForDate(attendanceDate);
                        var shiftStartTime = attendanceDate.Add(shiftStartTimeSpan);
                        var clockInDateTime = attendanceDate.Add(new TimeSpan(clockInTime.Hours, clockInTime.Minutes, clockInTime.Seconds));

                        var graceEndTime = shiftStartTime.AddMinutes(1);
                        if (clockInDateTime >= graceEndTime)
                        {
                            lateThisDay++;
                        }
                    }
                }

                // Absent = not on leave, not present
                var absentThisDay = (date.DayOfWeek == DayOfWeek.Sunday) ? 0 :
                    totalEmployees - allOnLeaveThisDay.Count - presentThisDay;

                lastWeekData.Add(new DailyAttendance
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    Present = presentThisDay,
                    Late = lateThisDay,
                    Absent = Math.Max(0, absentThisDay)
                });
            }

            // 2. Get CURRENT week data (Monday of this week to today)
            int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var startOfCurrentWeek = today.AddDays(-daysFromMonday);

            var currentWeekData = new List<DailyAttendance>();

            for (int i = 0; i <= daysFromMonday; i++)
            {
                var date = startOfCurrentWeek.AddDays(i);

                var dayAttendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e.Shift)
                            .ThenInclude(s => s.Schedules)
                    .Where(a => a.Date.Date == date.Date)
                    .ToListAsync();

                var onLeaveThisDay = await _context.LeaveRequests
                    .Where(l => (l.Status.ToLower() == "approve") &&
                               l.Start_Date.Date <= date.Date &&
                               l.End_Date.Date >= date.Date)
                    .Select(l => l.Employee_ID)
                    .Distinct()
                    .ToListAsync();

                var onLeaveFromAttendance = dayAttendance
                    .Where(a => a.Status != null &&
                           (a.Status.ToLower().Contains("leave") || a.Status.ToLower() == "on leave" || a.Status.ToLower().Contains("time off")))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .ToList();

                // Use ONLY attendance table for leave
                var allOnLeaveThisDay = onLeaveFromAttendance.Distinct().ToList();

                var uniquePresentEmployees = dayAttendance
                    .Where(a => a.ClockInTime != null && !allOnLeaveThisDay.Contains(a.Employee_ID))
                    .Select(a => a.Employee_ID)
                    .Distinct()
                    .ToList();

                var presentThisDay = uniquePresentEmployees.Count;

                var lateThisDay = 0;
                foreach (var employeeId in uniquePresentEmployees)
                {
                    var firstClockIn = dayAttendance
                        .Where(a => a.Employee_ID == employeeId && a.ClockInTime != null)
                        .OrderBy(a => a.ClockInTime)
                        .FirstOrDefault();

                    if (firstClockIn != null && firstClockIn.Employee?.Shift != null)
                    {
                        var shift = firstClockIn.Employee.Shift;
                        var clockInTime = firstClockIn.ClockInTime.Value;
                        var attendanceDate = firstClockIn.Date.Date;
                        var (shiftStartTimeSpan, shiftEndTimeSpan) = shift.GetTimeForDate(attendanceDate);
                        var shiftStartTime = attendanceDate.Add(shiftStartTimeSpan);
                        var clockInDateTime = attendanceDate.Add(new TimeSpan(clockInTime.Hours, clockInTime.Minutes, clockInTime.Seconds));

                        var graceEndTime = shiftStartTime.AddMinutes(1);
                        if (clockInDateTime >= graceEndTime)
                        {
                            lateThisDay++;
                        }
                    }
                }

                var absentThisDay = (date.DayOfWeek == DayOfWeek.Sunday) ? 0 :
                    totalEmployees - allOnLeaveThisDay.Count - presentThisDay;

                currentWeekData.Add(new DailyAttendance
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    Present = presentThisDay,
                    Late = lateThisDay,
                    Absent = Math.Max(0, absentThisDay)
                });
            }

            // ========== HISTORICAL ATTENDANCE RATES ==========
            // Last week attendance rate
            var lastWeekTotalDays = lastWeekData.Count(d => d.Date.DayOfWeek != DayOfWeek.Sunday);
            var lastWeekPresentCount = lastWeekData.Sum(d => d.Present);
            var lastWeekExpectedAttendance = lastWeekTotalDays * totalEmployees;
            var lastWeekAttendanceRate = lastWeekExpectedAttendance > 0 ?
                Math.Round((double)lastWeekPresentCount / lastWeekExpectedAttendance * 100, 1) : 0;

            // Last month attendance rate
            var lastMonthStart = today.AddMonths(-1).Date;
            var lastMonthEnd = today.AddDays(-1).Date;

            var lastMonthAttendance = await _context.Attendances
                .Where(a => a.Date >= lastMonthStart && a.Date <= lastMonthEnd && a.ClockInTime != null)
                .ToListAsync();

            var lastMonthOnLeave = await _context.LeaveRequests
                .Where(l => l.Status.ToLower() == "approve" &&
                           l.Start_Date <= lastMonthEnd &&
                           l.End_Date >= lastMonthStart)
                .ToListAsync();

            var lastMonthWorkingDays = Enumerable.Range(0, (lastMonthEnd - lastMonthStart).Days + 1)
                .Select(offset => lastMonthStart.AddDays(offset))
                .Count(d => d.DayOfWeek != DayOfWeek.Sunday);

            var lastMonthPresentCount = lastMonthAttendance
                .Select(a => a.Employee_ID)
                .Distinct()
                .Count();

            var lastMonthExpectedAttendance = lastMonthWorkingDays * totalEmployees;
            var lastMonthAttendanceRate = lastMonthExpectedAttendance > 0 ?
                Math.Round((double)lastMonthPresentCount / lastMonthExpectedAttendance * 100, 1) : 0;

            // ========== EMPLOYEE TREND ==========
            var lastEmployeeCount = HttpContext.Session.GetInt32("LastEmployeeCount");
            string trendText;
            string trendDirection;

            if (lastEmployeeCount.HasValue && lastEmployeeCount.Value != totalEmployees)
            {
                var difference = totalEmployees - lastEmployeeCount.Value;
                trendText = difference > 0
                    ? $"+{difference} from last check"
                    : $"{difference} from last check";
                trendDirection = difference > 0 ? "up" : "down";
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
            try
            {
                var attendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e.Department)
                    .Where(a => a.Date.Date == date.Date)
                    .OrderBy(a => a.Employee.First_Name)
                    .ToListAsync();

                var result = attendance.Select(a => new
                {
                    employeeName = $"{a.Employee.First_Name} {a.Employee.Last_Name}",
                    department = a.Employee.Department?.Department_Name ?? "N/A",
                    clockInTime = a.ClockInTime?.ToString(@"hh\:mm tt") ?? "N/A",
                    clockOutTime = a.ClockOutTime?.ToString(@"hh\:mm tt") ?? "N/A",
                    status = a.Status ?? "N/A"
                }).ToList();

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // GET: Monitoring/AttendanceOverview
        public async Task<IActionResult> AttendanceOverview(DateTime? dateFrom, DateTime? dateTo, string searchTerm = "", string sortBy = "Date", string sortOrder = "desc", int pageSize = 10, int currentPage = 1)
        {
            // ADDED: Admin check
            var isAdmin = HttpContext.Session.GetString("IsAdminAuthenticated");

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
                    var allRecords = sortedWithHours.Concat(withoutHours).ToList();

                    totalRecords = allRecords.Count;

                    // Apply pagination
                    attendanceRecords = allRecords
                        .Skip((currentPage - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
                }
                else
                {
                    // Normal sorting
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

                    // Get total count before pagination
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
        public async Task<IActionResult> ExportToExcel(DateTime? dateFrom, DateTime? dateTo, string searchTerm = "", string sortBy = "Date", string sortOrder = "desc")
        {
            try
            {
                // Get the same filtered data as the view (but all records, no pagination)
                var attendanceRecords = await GetFilteredAttendanceData(dateFrom, dateTo, searchTerm, sortBy, sortOrder);

                if (attendanceRecords == null || !attendanceRecords.Any())
                {
                    TempData["Error"] = "No records found to export.";
                    return RedirectToAction("AttendanceOverview", new { dateFrom, dateTo, searchTerm, sortBy, sortOrder });
                }

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Attendance Records");

                    // Add title
                    worksheet.Cell(1, 1).Value = "Attendance Overview Report";
                    worksheet.Cell(1, 1).Style.Font.Bold = true;
                    worksheet.Cell(1, 1).Style.Font.FontSize = 16;
                    worksheet.Range(1, 1, 1, 8).Merge();

                    // Add generation date
                    worksheet.Cell(2, 1).Value = $"Generated on: {DateTime.Now:MMMM dd, yyyy HH:mm}";
                    worksheet.Range(2, 1, 2, 8).Merge();

                    // Add headers
                    int currentRow = 4;
                    worksheet.Cell(currentRow, 1).Value = "Employee ID";
                    worksheet.Cell(currentRow, 2).Value = "Employee Name";
                    worksheet.Cell(currentRow, 3).Value = "Department";
                    worksheet.Cell(currentRow, 4).Value = "Date";
                    worksheet.Cell(currentRow, 5).Value = "Status";
                    worksheet.Cell(currentRow, 6).Value = "Clock In";
                    worksheet.Cell(currentRow, 7).Value = "Clock Out";
                    worksheet.Cell(currentRow, 8).Value = "Work Hours";

                    // Style header row
                    var headerRange = worksheet.Range(currentRow, 1, currentRow, 8);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    // Add data rows
                    currentRow++;
                    foreach (var record in attendanceRecords)
                    {
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

                        currentRow++;
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
        private async Task<List<Attendance>> GetFilteredAttendanceData(DateTime? dateFrom, DateTime? dateTo, string searchTerm = "", string sortBy = "Date", string sortOrder = "desc")
        {
            // If no dates provided, default to current month
            if (!dateFrom.HasValue)
                dateFrom = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            if (!dateTo.HasValue)
                dateTo = DateTime.Now.Date;

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

            if (sortBy == "WorkHours")
            {
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

            // Apply sorting for other columns
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