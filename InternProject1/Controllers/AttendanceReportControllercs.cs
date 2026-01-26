using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using System.IO;
using SelectPdf;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

public class AttendanceReportController : Controller
{
    private readonly ApplicationDbContext _context;

    public AttendanceReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    public IActionResult AdminLogin()
    {
        return View();
    }

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

    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        var reportData = await GetReportData();

        var allEmployees = await _context.Employees
            .OrderBy(e => e.First_Name)
            .Select(e => new { e.Employee_ID, FullName = e.First_Name + " " + e.Last_Name })
            .ToListAsync();

        ViewBag.EmployeeList = allEmployees;

        return View(reportData);
    }

    public async Task<IActionResult> EmployeeDetails(int id)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();

        var attendance = await _context.Attendances
            .Where(a => a.Employee_ID == id)
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        ViewBag.EmployeeName = employee.First_Name + " " + employee.Last_Name;
        return View(attendance);
    }

    // UPDATED: Logic to exclude Sundays and filter Leave by specific month
    private async Task<List<StaffSummaryViewModel>> GetReportData()
    {
        // Define the target period
        var targetMonth = 1;
        var targetYear = 2026;
        var monthStartDate = new DateTime(targetYear, targetMonth, 1);
        var monthEndDate = monthStartDate.AddMonths(1).AddDays(-1);

        // 1. Calculate how many work days to count (Excluding Sundays)
        int workDaysToCount = 0;
        int lastDayToProcess = (DateTime.Now.Year == targetYear && DateTime.Now.Month == targetMonth)
            ? DateTime.Now.Day
            : DateTime.DaysInMonth(targetYear, targetMonth);

        for (int d = 1; d <= lastDayToProcess; d++)
        {
            DateTime current = new DateTime(targetYear, targetMonth, d);
            if (current.DayOfWeek != DayOfWeek.Sunday)
            {
                workDaysToCount++;
            }
        }

        var employees = await _context.Employees.ToListAsync();
        var reportData = new List<StaffSummaryViewModel>();

        foreach (var emp in employees)
        {
            // Fetch attendance records strictly for this month
            var records = await _context.Attendances
                .Where(a => a.Employee_ID == emp.Employee_ID && a.Date.Month == targetMonth && a.Date.Year == targetYear)
                .ToListAsync();

            // 2. Attendance & Late Counts (Monday-Saturday only)
            int attCount = records.Count(a => (a.Status == "On Time" || a.Status == "Late") && a.Date.DayOfWeek != DayOfWeek.Sunday);
            int lateCount = records.Count(a => a.Status == "Late" && a.Date.DayOfWeek != DayOfWeek.Sunday);

            // 3. Leave Count: Fetch leaves that overlap with THIS month ONLY
            var approvedLeaves = await _context.LeaveRequests
                .Where(l => l.Employee_ID == emp.Employee_ID &&
                            l.Status == "Approve" &&
                            l.Start_Date <= monthEndDate &&
                            l.End_Date >= monthStartDate)
                .ToListAsync();

            int leaveDaysThisMonth = 0;
            foreach (var leave in approvedLeaves)
            {
                for (var date = leave.Start_Date.Date; date <= leave.End_Date.Date; date = date.AddDays(1))
                {
                    // Only count if the day falls inside January 2026 AND is not a Sunday
                    if (date >= monthStartDate && date <= monthEndDate && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        // Additionally, if checking for "today's month", don't count future leave days yet
                        if (date.Date <= DateTime.Now.Date || (targetYear != DateTime.Now.Year || targetMonth != DateTime.Now.Month))
                        {
                            leaveDaysThisMonth++;
                        }
                    }
                }
            }

            // 4. Absent Count Logic: workDaysToCount - (Attended + Leave)
            int absentCount = workDaysToCount - (attCount + leaveDaysThisMonth);
            if (absentCount < 0) absentCount = 0;

            reportData.Add(new StaffSummaryViewModel
            {
                Employee_ID = emp.Employee_ID,
                Name = emp.First_Name + " " + emp.Last_Name,
                AttendanceCount = attCount,
                LateCount = lateCount,
                LeaveCount = leaveDaysThisMonth,
                AbsentCount = absentCount,
                OvertimeCount = records.Count(a => a.Status == "Overtime")
            });
        }

        return reportData;
    }

    public async Task<IActionResult> ExportToExcel()
    {
        var data = await GetReportData();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Attendance Report");
            var currentRow = 1;
            worksheet.Cell(currentRow, 1).Value = "Employee Name";
            worksheet.Cell(currentRow, 2).Value = "Attendance";
            worksheet.Cell(currentRow, 3).Value = "Late";
            worksheet.Cell(currentRow, 4).Value = "Leave";
            worksheet.Cell(currentRow, 5).Value = "Absent";
            worksheet.Cell(currentRow, 6).Value = "Overtime";

            foreach (var item in data)
            {
                currentRow++;
                worksheet.Cell(currentRow, 1).Value = item.Name;
                worksheet.Cell(currentRow, 2).Value = item.AttendanceCount;
                worksheet.Cell(currentRow, 3).Value = item.LateCount;
                worksheet.Cell(currentRow, 4).Value = item.LeaveCount;
                worksheet.Cell(currentRow, 5).Value = item.AbsentCount;
                worksheet.Cell(currentRow, 6).Value = item.OvertimeCount;
            }
            worksheet.Columns().AdjustToContents();
            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Attendance_Report_Jan2026.xlsx");
            }
        }
    }

    public async Task<IActionResult> ExportToPdf()
    {
        var data = await GetReportData();

        int totalAtt = data.Sum(x => x.AttendanceCount);
        int totalLate = data.Sum(x => x.LateCount);
        int totalLeave = data.Sum(x => x.LeaveCount);
        int totalAbs = data.Sum(x => x.AbsentCount);
        int totalOt = data.Sum(x => x.OvertimeCount);

        string htmlContent = $@"
        <html>
        <head>
            <style>
                body {{ font-family: 'Segoe UI', Arial, sans-serif; padding: 20px; color: #333; }}
                .header {{ text-align: center; margin-bottom: 30px; border-bottom: 2px solid #4A77A5; padding-bottom: 10px; }}
                h2 {{ color: #4A77A5; margin: 0; text-transform: uppercase; letter-spacing: 1px; }}
                table {{ width: 100%; border-collapse: collapse; margin-top: 20px; table-layout: fixed; }}
                th {{ background-color: #4A77A5; color: white; padding: 12px 5px; font-size: 13px; text-align: center; }}
                td {{ border: 1px solid #ddd; padding: 10px 5px; text-align: center; font-size: 12px; }}
                .col-name {{ width: 25%; text-align: left; padding-left: 10px; }}
                .col-data {{ width: 15%; }} 
                tr:nth-child(even) {{ background-color: #f9f9f9; }}
                .total-row {{ background-color: #eee !important; font-weight: bold; border-top: 2px solid #4A77A5; }}
                .footer {{ margin-top: 30px; font-size: 10px; text-align: right; color: #777; font-style: italic; }}
            </style>
        </head>
        <body>
            <div class='header'>
                <h2>Attendance Analytics Report</h2>
                <p>Generated for the month of January 2026</p>
                <small style='color: #777;'>* Sundays and non-month days are excluded from calculations</small>
            </div>
            <table>
                <thead>
                    <tr>
                        <th class='col-name'>Employee Name</th>
                        <th class='col-data'>Attendance</th>
                        <th class='col-data'>Late</th>
                        <th class='col-data'>Leave</th>
                        <th class='col-data'>Absent</th>
                        <th class='col-data'>Overtime</th>
                    </tr>
                </thead>
                <tbody>";

        foreach (var item in data)
        {
            htmlContent += $@"
                    <tr>
                        <td style='text-align: left; padding-left: 10px;'>{item.Name}</td>
                        <td>{item.AttendanceCount}</td>
                        <td>{item.LateCount}</td>
                        <td>{item.LeaveCount}</td>
                        <td>{item.AbsentCount}</td>
                        <td>{item.OvertimeCount}</td>
                    </tr>";
        }

        htmlContent += $@"
                    <tr class='total-row'>
                        <td style='text-align: left; padding-left: 10px;'>COMPANY TOTAL</td>
                        <td>{totalAtt}</td>
                        <td>{totalLate}</td>
                        <td>{totalLeave}</td>
                        <td>{totalAbs}</td>
                        <td>{totalOt}</td>
                    </tr>
                </tbody>
            </table>
            <div class='footer'>
                Record Summary | Generated on: {DateTime.Now:dd-MM-yyyy HH:mm}
            </div>
        </body>
        </html>";

        HtmlToPdf converter = new HtmlToPdf();
        converter.Options.PdfPageSize = PdfPageSize.A4;
        converter.Options.PdfPageOrientation = PdfPageOrientation.Portrait;
        converter.Options.MarginTop = 30;
        converter.Options.MarginBottom = 30;
        converter.Options.MarginLeft = 20;
        converter.Options.MarginRight = 20;

        PdfDocument doc = converter.ConvertHtmlString(htmlContent);
        byte[] pdfFile = doc.Save();
        doc.Close();

        return File(pdfFile, "application/pdf", "Attendance_Report_Jan2026.pdf");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Remove("IsAdminAuthenticated");
        return RedirectToAction("AdminLogin");
    }
}