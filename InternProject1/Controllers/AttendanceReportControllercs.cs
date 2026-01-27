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
using System.Globalization;

namespace InternProject1.Controllers;

public class AttendanceReportController : Controller
{
    private readonly ApplicationDbContext _context;

    public AttendanceReportController(ApplicationDbContext context)
    {
        _context = context;
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

    public async Task<IActionResult> Index(string month)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("MMMM yyyy");

        var reportData = await GetReportData(month);

        var allEmployees = await _context.Employees
            .OrderBy(e => e.First_Name)
            .Select(e => new { e.Employee_ID, FullName = e.First_Name + " " + e.Last_Name })
            .ToListAsync();

        ViewBag.EmployeeList = allEmployees;
        ViewBag.SelectedMonth = month;

        var monthsList = Enumerable.Range(1, 12).Select(i => new DateTime(2026, i, 1).ToString("MMMM yyyy")).ToList();
        ViewBag.MonthsList = monthsList;

        return View(reportData);
    }

    public async Task<IActionResult> EmployeeDetails(int id, string month)
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();

        if (string.IsNullOrEmpty(month)) month = "January 2026";

        DateTime parsedDate;
        if (!DateTime.TryParseExact(month, "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            parsedDate = new DateTime(2026, 1, 1);
        }

        var targetMonth = parsedDate.Month;
        var targetYear = parsedDate.Year;
        DateTime today = DateTime.Now.Date;

        var approvedLeaveDates = await _context.LeaveRequests
            .Where(l => l.Employee_ID == id && l.Status == "Approve")
            .ToListAsync();

        var validDatesList = new HashSet<DateTime>();
        foreach (var leave in approvedLeaveDates)
        {
            for (var dt = leave.Start_Date.Date; dt <= leave.End_Date.Date; dt = dt.AddDays(1))
            {
                if (dt.Month == targetMonth && dt.Year == targetYear)
                {
                    validDatesList.Add(dt);
                }
            }
        }

        // MODIFY: Pull all records, then filter in memory to keep future Leaves but hide future Presence
        var allRecords = await _context.Attendances
            .Where(a => a.Employee_ID == id && a.Date.Month == targetMonth && a.Date.Year == targetYear)
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        var filteredAttendance = allRecords.Where(a =>
            a.Date.Date <= today || (a.Date.Date > today && a.Status == "Leave")
        ).ToList();

        foreach (var log in filteredAttendance)
        {
            if (log.Status == "Leave" && !validDatesList.Contains(log.Date.Date))
            {
                log.Status = "Absent";
            }
        }

        ViewBag.EmployeeName = employee.First_Name + " " + employee.Last_Name;
        ViewBag.SelectedMonth = month;
        ViewBag.EmployeeId = id;

        return View(filteredAttendance);
    }

    private async Task<List<StaffSummaryViewModel>> GetReportData(string monthName)
    {
        DateTime parsedDate;
        if (!DateTime.TryParseExact(monthName, "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            parsedDate = new DateTime(2026, 1, 1);
        }

        var targetMonth = parsedDate.Month;
        var targetYear = parsedDate.Year;
        DateTime today = DateTime.Now.Date;

        var monthStartDate = new DateTime(targetYear, targetMonth, 1);
        var monthEndDate = monthStartDate.AddMonths(1).AddDays(-1);

        int workDaysToCount = 0;
        int lastDayToProcess = 0;

        if (parsedDate.Year < today.Year || (parsedDate.Year == today.Year && parsedDate.Month < today.Month))
            lastDayToProcess = DateTime.DaysInMonth(targetYear, targetMonth);
        else if (parsedDate.Year == today.Year && parsedDate.Month == today.Month)
            lastDayToProcess = today.Day;
        else
            lastDayToProcess = 0;

        for (int d = 1; d <= lastDayToProcess; d++)
        {
            DateTime current = new DateTime(targetYear, targetMonth, d);
            if (current.DayOfWeek != DayOfWeek.Sunday) workDaysToCount++;
        }

        var employees = await _context.Employees.ToListAsync();
        var reportData = new List<StaffSummaryViewModel>();

        foreach (var emp in employees)
        {
            var records = await _context.Attendances
                    .Where(a => a.Employee_ID == emp.Employee_ID && a.Date.Month == targetMonth && a.Date.Year == targetYear)
                    .ToListAsync();

            // Only count Attendance/Late if Date <= Today
            int attCount = records.Count(a => a.Date.Date <= today && a.ClockInTime.HasValue && a.Date.DayOfWeek != DayOfWeek.Sunday);
            int lateCount = records.Count(a => a.Date.Date <= today && (a.Status != null && a.Status.ToLower() == "late") && a.ClockInTime.HasValue);

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
                    if (date >= monthStartDate && date <= monthEndDate && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        // Future leaves are allowed to be counted in the total
                        bool workedOnThisDay = records.Any(r => r.Date.Date == date.Date && r.ClockInTime.HasValue && r.Date.Date <= today);
                        if (!workedOnThisDay)
                        {
                            leaveDaysThisMonth++;
                        }
                    }
                }
            }

            int absentCount = Math.Max(0, workDaysToCount - (attCount + records.Count(r => r.Date.Date <= today && r.Status == "Leave")));

            reportData.Add(new StaffSummaryViewModel
            {
                Employee_ID = emp.Employee_ID,
                Name = emp.First_Name + " " + emp.Last_Name,
                AttendanceCount = attCount,
                LateCount = lateCount,
                LeaveCount = leaveDaysThisMonth,
                AbsentCount = absentCount
            });
        }

        return reportData;
    }

    public async Task<IActionResult> ExportToExcel(string month)
    {
        if (string.IsNullOrEmpty(month)) month = DateTime.Now.ToString("MMMM yyyy");
        var data = await GetReportData(month);

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Attendance Report");
            var currentRow = 1;
            worksheet.Cell(currentRow, 1).Value = "Employee Name";
            worksheet.Cell(currentRow, 2).Value = "Attendance";
            worksheet.Cell(currentRow, 3).Value = "Late";
            worksheet.Cell(currentRow, 4).Value = "Leave";
            worksheet.Cell(currentRow, 5).Value = "Absent";

            foreach (var item in data)
            {
                currentRow++;
                worksheet.Cell(currentRow, 1).Value = item.Name;
                worksheet.Cell(currentRow, 2).Value = item.AttendanceCount;
                worksheet.Cell(currentRow, 3).Value = item.LateCount;
                worksheet.Cell(currentRow, 4).Value = item.LeaveCount;
                worksheet.Cell(currentRow, 5).Value = item.AbsentCount;
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

        int totalAtt = data.Sum(x => x.AttendanceCount);
        int totalLate = data.Sum(x => x.LateCount);
        int totalLeave = data.Sum(x => x.LeaveCount);
        int totalAbs = data.Sum(x => x.AbsentCount);

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
                tr:nth-child(even) {{ background-color: #f9f9f9; }}
                .total-row {{ background-color: #eee !important; font-weight: bold; border-top: 2px solid #4A77A5; }}
            </style>
        </head>
        <body>
            <div class='header'>
                <h2>Attendance Analytics Report</h2>
                <p>Generated for the month of {month}</p>
            </div>
            <table>
                <thead>
                    <tr>
                        <th style='width: 25%; text-align: left; padding-left: 10px;'>Employee Name</th>
                        <th>Attendance</th>
                        <th>Late</th>
                        <th>Leave</th>
                        <th>Absent</th>
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
                    </tr>";
        }

        htmlContent += $@"
                    <tr class='total-row'>
                        <td style='text-align: left; padding-left: 10px;'>COMPANY TOTAL</td>
                        <td>{totalAtt}</td>
                        <td>{totalLate}</td>
                        <td>{totalLeave}</td>
                        <td>{totalAbs}</td>
                    </tr>
                </tbody>
            </table>
        </body>
        </html>";

        HtmlToPdf converter = new HtmlToPdf();
        PdfDocument doc = converter.ConvertHtmlString(htmlContent);
        byte[] pdfFile = doc.Save();
        doc.Close();

        return File(pdfFile, "application/pdf", $"Attendance_Report_{month.Replace(" ", "")}.pdf");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Remove("IsAdminAuthenticated");
        return RedirectToAction("AdminLogin");
    }
}