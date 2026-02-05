using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using ClosedXML.Excel;
using System.IO;
using SelectPdf;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Globalization;
using System.Diagnostics;

namespace InternProject1.Controllers;

public class AttendanceReportController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public AttendanceReportController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
    {
        _context = context;
        _webHostEnvironment = webHostEnvironment;
    }

    private HashSet<DateTime> GetMalaysiaHolidays(int year)
    {
        var baseHolidays = new List<DateTime>
        {
            new DateTime(year, 1, 1),
            new DateTime(year, 1, 29),
            new DateTime(year, 1, 30),
            new DateTime(year, 2, 1),
            new DateTime(year, 3, 20),
            new DateTime(year, 3, 21),
            new DateTime(year, 5, 1),
            new DateTime(year, 5, 27),
            new DateTime(year, 5, 31),
            new DateTime(year, 6, 1),
            new DateTime(year, 8, 31),
            new DateTime(year, 9, 16),
            new DateTime(year, 11, 8),
            new DateTime(year, 12, 25)
        };

        var finalHolidays = new HashSet<DateTime>();
        foreach (var holiday in baseHolidays)
        {
            finalHolidays.Add(holiday);
            if (holiday.DayOfWeek == DayOfWeek.Sunday)
            {
                finalHolidays.Add(holiday.AddDays(1));
            }
        }
        return finalHolidays;
    }

    public IActionResult AdminLogin() => View();

    [HttpPost]
    public IActionResult VerifyAdmin(string email, string password)
    {
        if (email == "admin@gmail.com" && password == "admin123")
        {
            HttpContext.Session.SetString("IsAdminAuthenticated", "true");
            return RedirectToAction("Index");
        }
        ViewBag.Error = "Invalid Admin Credentials";
        return View("AdminLogin");
    }

    // --- FIXED: Merged Index Method to avoid AmbiguousMatchException ---
    public async Task<IActionResult> Index(string? month)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        // Default to current month if null
        if (string.IsNullOrEmpty(month))
            month = DateTime.Now.ToString("MMMM yyyy");

        // Fetch the summary analytics data
        var reportData = await GetReportData(month);

        var allEmployees = await _context.Employees
            .OrderBy(e => e.First_Name)
            .Select(e => new { e.Employee_ID, FullName = e.First_Name + " " + e.Last_Name })
            .ToListAsync();

        ViewBag.EmployeeList = allEmployees;
        ViewBag.SelectedMonth = month;

        // Generate months for the current year
        ViewBag.MonthsList = Enumerable.Range(1, 12)
            .Select(i => new DateTime(DateTime.Now.Year, i, 1).ToString("MMMM yyyy"))
            .ToList();

        return View(reportData);
    }

    public async Task<IActionResult> YearlyReport(int? year)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true") return RedirectToAction("AdminLogin");

        int targetYear = year ?? DateTime.Now.Year;

        ViewBag.YearList = new List<int> { targetYear - 2, targetYear - 1, targetYear, targetYear + 1 };
        ViewBag.SelectedYear = targetYear;

        var employees = await _context.Employees.ToListAsync();
        var yearlyData = new List<StaffSummaryViewModel>();
        var malaysiaHolidays = GetMalaysiaHolidays(targetYear);

        foreach (var emp in employees)
        {
            var records = await _context.Attendances
                .Where(a => a.Employee_ID == emp.Employee_ID && a.Date.Year == targetYear)
                .ToListAsync();

            var approvedLeaves = await _context.LeaveRequests
                .Where(l => l.Employee_ID == emp.Employee_ID && l.Status == "Approve" && (l.Start_Date.Year == targetYear || l.End_Date.Year == targetYear))
                .ToListAsync();

            int totalYearlyLeaveDays = 0;
            foreach (var leave in approvedLeaves)
            {
                for (var date = leave.Start_Date.Date; date <= leave.End_Date.Date; date = date.AddDays(1))
                {
                    if (date.Year == targetYear && date.DayOfWeek != DayOfWeek.Sunday && !malaysiaHolidays.Contains(date.Date))
                    {
                        if (!records.Any(r => r.Date.Date == date.Date && r.ClockInTime.HasValue))
                            totalYearlyLeaveDays++;
                    }
                }
            }

            yearlyData.Add(new StaffSummaryViewModel
            {
                Employee_ID = emp.Employee_ID,
                Name = emp.First_Name + " " + emp.Last_Name,
                AttendanceCount = records.Count(r => r.ClockInTime.HasValue && r.Date.DayOfWeek != DayOfWeek.Sunday),
                LateCount = records.Count(r => r.Status != null && r.Status.ToLower() == "late"),
                LeaveCount = totalYearlyLeaveDays,
                AbsentCount = 0
            });
        }

        return View(yearlyData);
    }

    private async Task<List<StaffSummaryViewModel>> GetReportData(string monthName)
    {
        DateTime parsedDate;
        if (!DateTime.TryParseExact(monthName, "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            parsedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        }

        var targetMonth = parsedDate.Month;
        var targetYear = parsedDate.Year;
        DateTime today = DateTime.Now.Date;

        var monthStartDate = new DateTime(targetYear, targetMonth, 1);
        var monthEndDate = monthStartDate.AddMonths(1).AddDays(-1);
        var malaysiaHolidays = GetMalaysiaHolidays(targetYear);

        int totalSundaysInMonth = 0;
        int totalHolidaysInMonth = 0;
        int daysInFullMonth = DateTime.DaysInMonth(targetYear, targetMonth);

        for (int d = 1; d <= daysInFullMonth; d++)
        {
            DateTime current = new DateTime(targetYear, targetMonth, d);
            if (current.DayOfWeek == DayOfWeek.Sunday) totalSundaysInMonth++;
            else if (malaysiaHolidays.Contains(current.Date)) totalHolidaysInMonth++;
        }

        int lastDayToProcess = (parsedDate.Year < today.Year || (parsedDate.Year == today.Year && parsedDate.Month < today.Month))
            ? daysInFullMonth
            : (parsedDate.Year == today.Year && parsedDate.Month == today.Month) ? today.Day : 0;

        int workDaysSoFar = 0;
        for (int d = 1; d <= lastDayToProcess; d++)
        {
            DateTime current = new DateTime(targetYear, targetMonth, d);
            if (current.DayOfWeek != DayOfWeek.Sunday && !malaysiaHolidays.Contains(current.Date))
                workDaysSoFar++;
        }

        var employees = await _context.Employees.ToListAsync();
        var reportData = new List<StaffSummaryViewModel>();

        foreach (var emp in employees)
        {
            var records = await _context.Attendances
                    .Where(a => a.Employee_ID == emp.Employee_ID && a.Date.Month == targetMonth && a.Date.Year == targetYear)
                    .ToListAsync();

            int attCount = records.Count(a => a.Date.Date <= today && a.ClockInTime.HasValue && a.Date.DayOfWeek != DayOfWeek.Sunday);
            int lateCount = records.Count(a => a.Date.Date <= today && (a.Status != null && a.Status.ToLower() == "late") && a.ClockInTime.HasValue);

            var approvedLeaves = await _context.LeaveRequests
                .Where(l => l.Employee_ID == emp.Employee_ID && l.Status == "Approve" && l.Start_Date <= monthEndDate && l.End_Date >= monthStartDate)
                .ToListAsync();

            int leaveDaysThisMonth = 0;
            foreach (var leave in approvedLeaves)
            {
                for (var date = leave.Start_Date.Date; date <= leave.End_Date.Date; date = date.AddDays(1))
                {
                    if (date >= monthStartDate && date <= monthEndDate && date.DayOfWeek != DayOfWeek.Sunday && !malaysiaHolidays.Contains(date.Date))
                    {
                        if (!records.Any(r => r.Date.Date == date.Date && r.ClockInTime.HasValue && r.Date.Date <= today))
                            leaveDaysThisMonth++;
                    }
                }
            }

            reportData.Add(new StaffSummaryViewModel
            {
                Employee_ID = emp.Employee_ID,
                Name = emp.First_Name + " " + emp.Last_Name,
                AttendanceCount = attCount,
                LateCount = lateCount,
                LeaveCount = leaveDaysThisMonth,
                AbsentCount = Math.Max(0, workDaysSoFar - (attCount + leaveDaysThisMonth)),
                HolidayCount = totalHolidaysInMonth,
                SundayCount = totalSundaysInMonth
            });
        }
        return reportData;
    }

    public async Task<IActionResult> ExportToExcel(string month)
    {
        if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("MMMM yyyy");
        var data = await GetReportData(month);

        DateTime parsedDate = DateTime.ParseExact(month, "MMMM yyyy", CultureInfo.InvariantCulture);
        int totalDays = DateTime.DaysInMonth(parsedDate.Year, parsedDate.Month);
        var firstRecord = data.FirstOrDefault();
        int expectedWorkDays = firstRecord != null ? (totalDays - firstRecord.SundayCount - firstRecord.HolidayCount) : 0;

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Attendance Report");

            worksheet.Cell(1, 1).Value = "Report Month:";
            worksheet.Cell(1, 2).Value = month;
            worksheet.Cell(2, 1).Value = "Expected Working Days:";
            worksheet.Cell(2, 2).Value = expectedWorkDays;
            worksheet.Range("A1:A2").Style.Font.Bold = true;

            var headers = new[] { "Employee Name", "Attendance", "Late", "Leave", "Absent", "Public Holiday", "Sunday" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4A77A5");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int currentRow = 4;
            foreach (var item in data)
            {
                currentRow++;
                worksheet.Cell(currentRow, 1).Value = item.Name;
                worksheet.Cell(currentRow, 2).Value = item.AttendanceCount;
                worksheet.Cell(currentRow, 3).Value = item.LateCount;
                worksheet.Cell(currentRow, 4).Value = item.LeaveCount;

                var absentCell = worksheet.Cell(currentRow, 5);
                absentCell.Value = item.AbsentCount;
                if (item.AbsentCount > 0) absentCell.Style.Font.FontColor = XLColor.Red;

                worksheet.Cell(currentRow, 6).Value = item.HolidayCount;
                worksheet.Cell(currentRow, 7).Value = item.SundayCount;
            }
            worksheet.Columns().AdjustToContents();
            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Attendance_Report_{month.Replace(" ", "")}.xlsx");
            }
        }
    }

    public async Task<IActionResult> ExportToPdf(string month)
    {
        if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("MMMM yyyy");
        var data = await GetReportData(month);

        DateTime parsedDate = DateTime.ParseExact(month, "MMMM yyyy", CultureInfo.InvariantCulture);
        int totalDaysInMonth = DateTime.DaysInMonth(parsedDate.Year, parsedDate.Month);
        var firstRecord = data.FirstOrDefault();
        int workingDays = firstRecord != null ? (totalDaysInMonth - firstRecord.SundayCount - firstRecord.HolidayCount) : 0;

        string logoBase64 = "";
        string logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "Alpine Logo.png");

        if (System.IO.File.Exists(logoPath))
        {
            try
            {
                byte[] imageBytes = System.IO.File.ReadAllBytes(logoPath);
                logoBase64 = "data:image/png;base64," + Convert.ToBase64String(imageBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logo conversion failed: {ex.Message}");
            }
        }

        string htmlContent = $@"
        <html>
        <head>
            <style>
                body {{ font-family: 'Arial', sans-serif; padding: 20px; color: #333; }}
                .header-container {{ width: 100%; border-bottom: 3px solid #4A77A5; padding-bottom: 10px; margin-bottom: 20px; }}
                .logo-box {{ float: left; width: 250px; height: 80px; }}
                .logo-img {{ height: 70px; width: auto; }}
                .address-box {{ float: right; text-align: right; font-size: 10px; color: #444; width: 350px; line-height: 1.4; }}
                .clearfix {{ clear: both; }}
                .report-title {{ text-align: center; margin: 20px 0; }}
                .report-title h2 {{ color: #4A77A5; font-size: 24px; margin: 0; text-transform: uppercase; letter-spacing: 1px; }}
                .stats-grid {{ background-color: #f9f9f9; padding: 15px; border: 1px solid #ddd; border-radius: 4px; text-align: center; font-size: 11px; margin-bottom: 25px; }}
                .data-table {{ width: 100%; border-collapse: collapse; }}
                .data-table th {{ background-color: #4A77A5; color: white; padding: 12px 5px; font-size: 11px; border: 1px solid #4A77A5; }}
                .data-table td {{ border: 1px solid #ddd; padding: 8px 5px; text-align: center; font-size: 10px; }}
                .name-column {{ text-align: left; padding-left: 10px; font-weight: bold; }}
                .total-row {{ background-color: #f2f2f2; font-weight: bold; font-size: 11px; }}
                .footer {{ margin-top: 60px; }}
                .signature-box {{ float: right; width: 220px; border-top: 1px solid #000; text-align: center; padding-top: 5px; font-size: 11px; }}
                .timestamp {{ float: left; font-size: 9px; color: #888; margin-top: 15px; }}
            </style>
        </head>
        <body>
            <div class='header-container'>
                <div class='logo-box'>
                    {(string.IsNullOrEmpty(logoBase64)
                        ? "<h1 style='color:#4A77A5; margin:0;'>ALPINE</h1>"
                        : $"<img src='{logoBase64}' class='logo-img' />")}
                </div>
                <div class='address-box'>
                    <strong>Alpine Software Solutions</strong><br/>
                    126-3, 12, Jalan Genting Kelang, Taman Danau Kota,<br/>
                    53300 Kuala Lumpur, Wilayah Persekutuan Kuala Lumpur<br/>
                    Phone: 011-3933 2219
                </div>
                <div class='clearfix'></div>
            </div>

            <div class='report-title'>
                <h2>ATTENDANCE ANALYTICS REPORT</h2>
                <p>For the Month of <strong>{month}</strong></p>
            </div>

            <div class='stats-grid'>
                Total Days: <strong>{totalDaysInMonth}</strong> | 
                Sundays: <strong>{firstRecord?.SundayCount}</strong> | 
                Holidays: <strong>{firstRecord?.HolidayCount}</strong> | 
                <span style='color:#4A77A5; font-weight:bold;'>Expected Working Days: {workingDays}</span>
            </div>

            <table class='data-table'>
                <thead>
                    <tr>
                        <th class='name-column'>Employee Name</th>
                        <th>Attendance</th>
                        <th>Late</th>
                        <th>Leave</th>
                        <th>Absent</th>
                        <th>Holiday</th>
                        <th>Sunday</th>
                    </tr>
                </thead>
                <tbody>";

        foreach (var item in data)
        {
            htmlContent += $@"
                <tr>
                    <td class='name-column'>{item.Name}</td>
                    <td>{item.AttendanceCount}</td>
                    <td>{item.LateCount}</td>
                    <td>{item.LeaveCount}</td>
                    <td {(item.AbsentCount > 0 ? "style='color:red; font-weight:bold;'" : "")}>{item.AbsentCount}</td>
                    <td>{item.HolidayCount}</td>
                    <td>{item.SundayCount}</td>
                </tr>";
        }

        htmlContent += $@"
                <tr class='total-row'>
                    <td class='name-column'>COMPANY TOTAL</td>
                    <td>{data.Sum(x => x.AttendanceCount)}</td>
                    <td>{data.Sum(x => x.LateCount)}</td>
                    <td>{data.Sum(x => x.LeaveCount)}</td>
                    <td>{data.Sum(x => x.AbsentCount)}</td>
                    <td>-</td>
                    <td>-</td>
                </tr>
                </tbody>
            </table>

            <div class='footer'>
                <div class='timestamp'>Generated on: {DateTime.Now:dd MMM yyyy HH:mm}</div>
                <div class='signature-box'>Manager Signature</div>
                <div class='clearfix'></div>
            </div>
        </body>
        </html>";

        HtmlToPdf converter = new HtmlToPdf();
        converter.Options.PdfPageSize = PdfPageSize.A4;
        converter.Options.WebPageWidth = 1024;

        PdfDocument doc = converter.ConvertHtmlString(htmlContent);
        byte[] pdfFile = doc.Save();
        doc.Close();

        return File(pdfFile, "application/pdf", $"Attendance_Report_{month.Replace(" ", "")}.pdf");
    }

    public async Task<IActionResult> EmployeeDetails(int id, string month)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("MMMM yyyy");

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();

        DateTime parsedDate;
        if (!DateTime.TryParseExact(month, "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            parsedDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        }

        var logs = await _context.Attendances
            .Where(a => a.Employee_ID == id &&
                        a.Date.Month == parsedDate.Month &&
                        a.Date.Year == parsedDate.Year)
            .OrderBy(a => a.Date)
            .ToListAsync();

        ViewBag.EmployeeName = employee.First_Name + " " + employee.Last_Name;
        ViewBag.EmployeeId = id;
        ViewBag.SelectedMonth = month;

        return View(logs);
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Remove("IsAdminAuthenticated");
        return RedirectToAction("AdminLogin");
    }
}