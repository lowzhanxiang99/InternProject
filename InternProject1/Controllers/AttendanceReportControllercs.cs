using ClosedXML.Excel;
using InternProject1.Data;
using InternProject1.Migrations;
using InternProject1.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SelectPdf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InternProject1.Controllers;

public class AttendanceReportController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private byte[] imageArray;

    public AttendanceReportController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
    {
        _context = context;
        _webHostEnvironment = webHostEnvironment;
    }

    private HashSet<DateTime> GetMalaysiaHolidays(int year)
    {
        var baseHolidays = new List<DateTime>
    {
        new DateTime(year, 1, 1),   // New Year's Day
        new DateTime(year, 2, 1),   // Federal Territory Day (Kuala Lumpur)
        new DateTime(year, 2, 17),  // Chinese New Year (Day 1) - NEW 2026 DATE
        new DateTime(year, 2, 18),  // Chinese New Year (Day 2) - NEW 2026 DATE
        new DateTime(year, 3, 20),  // Hari Raya Aidilfitri (Day 1)
        new DateTime(year, 3, 21),  // Hari Raya Aidilfitri (Day 2)
        new DateTime(year, 5, 1),   // Labour Day
        new DateTime(year, 5, 27),  // Hari Raya Haji
        new DateTime(year, 5, 31),  // Wesak Day
        new DateTime(year, 6, 1),   // Agong's Birthday
        new DateTime(year, 8, 31),  // Merdeka Day
        new DateTime(year, 9, 16),  // Malaysia Day
        new DateTime(year, 11, 8),  // Deepavali
        new DateTime(year, 12, 25)  // Christmas Day
        };

        var finalHolidays = new HashSet<DateTime>();
        foreach (var holiday in baseHolidays)
        {
            finalHolidays.Add(holiday.Date); // Ensure .Date is used
            if (holiday.DayOfWeek == DayOfWeek.Sunday)
            {
                finalHolidays.Add(holiday.AddDays(1).Date);
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

    public async Task<IActionResult> Index(string? month)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        if (string.IsNullOrEmpty(month))
            month = DateTime.Now.ToString("MMMM yyyy");

        var reportData = await GetReportData(month);

        var allEmployees = await _context.Employees
          .OrderBy(e => e.First_Name)
          .Select(e => new { e.Employee_ID, FullName = e.First_Name + " " + e.Last_Name })
          .ToListAsync();

        ViewBag.EmployeeList = allEmployees;
        ViewBag.SelectedMonth = month;

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
            var leaveDatesList = new List<string>();

            foreach (var leave in approvedLeaves)
            {
                for (var date = leave.Start_Date.Date; date <= leave.End_Date.Date; date = date.AddDays(1))
                {
                    if (date >= monthStartDate && date <= monthEndDate && date.DayOfWeek != DayOfWeek.Sunday && !malaysiaHolidays.Contains(date.Date))
                    {
                        if (!records.Any(r => r.Date.Date == date.Date && r.ClockInTime.HasValue && r.Date.Date <= today))
                        {
                            leaveDaysThisMonth++;
                            leaveDatesList.Add(date.ToString("dd MMM"));
                        }
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
                LeaveDatesList = leaveDatesList,
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
        // Use Path.GetFullPath to ensure we are looking at the absolute system path
        string rootPath = _webHostEnvironment.WebRootPath;
        string path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "Alpine_Logo.png");
       
        if (System.IO.File.Exists(path))
        {
            try
            {
                byte[] imageBytes = System.IO.File.ReadAllBytes(path);
                string extension = Path.GetExtension(path).Replace(".", "").ToLower();
                // Ensure the mime-type matches (png vs jpg)
                logoBase64 = Convert.ToBase64String(imageArray);
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
            @page {{ margin: 0; }}
            body {{ font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 0; color: #333; background-color: #fff; }}
            .container {{ padding: 40px; }}
            
            /* Modern Header */
            .header {{ display: flex; border-bottom: 2px solid #2c3e50; padding-bottom: 20px; margin-bottom: 30px; }}
            .logo-section {{ float: left; width: 50%; }}
            .info-section {{ float: right; width: 50%; text-align: right; font-size: 12px; color: #7f8c8d; line-height: 1.5; }}
            .company-name {{ color: #2c3e50; font-size: 28px; font-weight: bold; margin: 0; letter-spacing: -1px; }}
            
            .clearfix {{ clear: both; }}

            /* Title Section */
            .report-header {{ text-align: center; margin-bottom: 35px; }}
            .report-header h1 {{ font-size: 22px; color: #2c3e50; text-transform: uppercase; margin: 0; letter-spacing: 2px; }}
            .report-header p {{ color: #3498db; font-size: 14px; font-weight: bold; margin-top: 5px; }}

            /* Dashboard Stats */
            .stats-container {{ 
    width: 100%; 
    margin-bottom: 30px; 
    display: block;
}}
.stat-box {{ 
    float: left; 
    width: 23%; /* Adjusted to fit 4 boxes perfectly */
    background: #f8f9fa; 
    border: 1px solid #e9ecef; 
    padding: 15px 0; 
    margin-right: 2%; 
    text-align: center; 
    border-radius: 8px; 
}}
.stat-box.last {{
    margin-right: 0;
}}
.stat-box.highlight {{ 
    background: #2c3e50; 
    color: white; 
    border: 1px solid #2c3e50; 
}}
.stat-label {{ 
    display: block; 
    font-size: 10px; 
    text-transform: uppercase; 
    color: #95a5a6; 
    margin-bottom: 5px; 
}}
.stat-box.highlight .stat-label {{ 
    color: #bdc3c7; 
}}
.stat-value {{ 
    display: block; 
    font-size: 18px; 
    font-weight: bold; 
}}

            /* Professional Table */
            .data-table {{ width: 100%; border-collapse: collapse; margin-top: 10px; box-shadow: 0 2px 5px rgba(0,0,0,0.05); }}
            .data-table th {{ background-color: #2c3e50; color: #ffffff; padding: 12px 10px; text-transform: uppercase; font-size: 11px; text-align: center; border: none; }}
            .data-table th:first-child {{ border-radius: 8px 0 0 0; text-align: left; }}
            .data-table th:last-child {{ border-radius: 0 8px 0 0; }}
            .data-table td {{ padding: 10px; border-bottom: 1px solid #eee; font-size: 11px; text-align: center; color: #2c3e50; }}
            .data-table tr:nth-child(even) {{ background-color: #fcfcfc; }}
            .data-table .emp-name {{ text-align: left; font-weight: bold; color: #34495e; }}
            .absent-alert {{ color: #e74c3c; font-weight: bold; background: #fdedec; border-radius: 4px; padding: 2px 5px; }}

            /* Footer */
            .footer {{ margin-top: 50px; border-top: 1px solid #eee; padding-top: 20px; }}
            .meta-info {{ float: left; font-size: 10px; color: #95a5a6; }}
            .signature {{ float: right; width: 200px; text-align: center; }}
            .sig-line {{ border-top: 1px solid #2c3e50; margin-top: 40px; padding-top: 5px; font-size: 11px; font-weight: bold; color: #2c3e50; }}
        </style>
    </head>
    <body>
        <div class='container'>
           <div class='header'>
    <div class='logo-section' style='float: left; width: 40%;'>
        {(string.IsNullOrEmpty(logoBase64)
            ? "<h1 class='company-name' style='margin:0;'>ALPINE</h1>"
            : $"<img src='data:image/png;base64,{logoBase64}' style='height:70px; width:auto; display:block;' />")}
    </div>
    <div class='info-section' style='float: right; width: 50%; text-align: right;'>
        <strong style='color:#2c3e50;'>Alpine Software Solutions</strong><br/>
        <span style='font-size:10px;'>126-3, 12, Jalan Genting Kelang, Taman Danau Kota,<br/>
        53300 Kuala Lumpur, Malaysia<br/>
        Contact: 011-3933 2219</span>
    </div>
    <div class='clearfix' style='clear:both;'></div>
</div>

            <div class='report-header'>
                <h1>Monthly Attendance Summary</h1>
                <p>Period: {month}</p>
            </div>

           <div class='stats-container'>
    <div class='stat-box'>
        <span class='stat-label'>Total Days</span>
        <span class='stat-value'>{totalDaysInMonth}</span>
    </div>
    <div class='stat-box'>
        <span class='stat-label'>Holidays</span>
        <span class='stat-value'>{firstRecord?.HolidayCount}</span>
    </div>
    <div class='stat-box'>
        <span class='stat-label'>Sundays</span>
        <span class='stat-value'>{firstRecord?.SundayCount}</span>
    </div>
    <div class='stat-box highlight last'>
        <span class='stat-label'>Required Work Days</span>
        <span class='stat-value'>{workingDays}</span>
    </div>
    <div class='clearfix'></div>
</div>

            <table class='data-table'>
                <thead>
                    <tr>
                        <th style='width: 30%;'>Employee Name</th>
                        <th>Present</th>
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
                    <td class='emp-name'>{item.Name}</td>
                    <td>{item.AttendanceCount}</td>
                    <td>{item.LateCount}</td>
                    <td>{item.LeaveCount}</td>
                    <td><span class='{(item.AbsentCount > 0 ? "absent-alert" : "")}'>{item.AbsentCount}</span></td>
                    <td>{item.HolidayCount}</td>
                    <td>{item.SundayCount}</td>
                </tr>";
        }

        htmlContent += $@"
                <tr style='background: #f8f9fa; font-weight: bold; border-top: 2px solid #2c3e50;'>
                    <td class='emp-name'>ORGANIZATION TOTAL</td>
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
                <div class='meta-info'>
                    Generated securely by Alpine System<br/>
                    Date: {DateTime.Now:dd MMM yyyy} | Time: {DateTime.Now:HH:mm}
                </div>
                <div class='signature'>
                    <div class='sig-line'>Authorized Signature</div>
                </div>
                <div class='clearfix'></div>
            </div>
        </div>
    </body>
    </html>";

        HtmlToPdf converter = new HtmlToPdf();
        converter.Options.PdfPageSize = PdfPageSize.A4;
        converter.Options.WebPageWidth = 1024;
        converter.Options.MarginTop = 0;
        converter.Options.MarginBottom = 0;

        PdfDocument doc = converter.ConvertHtmlString(htmlContent);
        byte[] pdfFile = doc.Save();
        doc.Close();

        return File(pdfFile, "application/pdf", $"Attendance_Report_{month.Replace(" ", "_")}.pdf");
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

    [HttpPost]
    public async Task<IActionResult> AddManualAttendance(int EmployeeId, DateTime Date, TimeSpan ClockInTime, TimeSpan ClockOutTime, string Status)
    {
        var newAttendance = new Attendance
        {
            Employee_ID = EmployeeId,
            Date = Date,
            ClockInTime = ClockInTime,
            ClockOutTime = ClockOutTime,
            Status = Status,
            IPAddress = "Admin Entry" // To distinguish from auto-logs
        };

        _context.Attendances.Add(newAttendance);
        await _context.SaveChangesAsync();

        return RedirectToAction("EmployeeDetails", new { id = EmployeeId });
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Remove("IsAdminAuthenticated");
        return RedirectToAction("AdminLogin");
    }
}