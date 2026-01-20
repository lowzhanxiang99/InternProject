using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using System.IO;
using SelectPdf; // Added SelectPdf for easier PDF generation

public class AttendanceReportController : Controller
{
    private readonly ApplicationDbContext _context;

    public AttendanceReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Show the Admin Login Page first
    public IActionResult AdminLogin()
    {
        return View();
    }

    // 2. Validate admin credentials
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

    // 3. GET: AttendanceReport/Index
    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        var reportData = await GetReportData();
        return View(reportData);
    }

    // Helper method to fetch and calculate report statistics for all registered employees
    private async Task<List<StaffSummaryViewModel>> GetReportData()
    {
        return await _context.Employees
            .Select(e => new StaffSummaryViewModel
            {
                Name = e.First_Name + " " + e.Last_Name,
                AttendanceCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && (a.Status == "Present" || a.Status == "Late")),
                LateCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Late"),
                LeaveCount = _context.LeaveRequests.Count(l => l.Employee_ID == e.Employee_ID && l.Status == "Approve"),
                AbsentCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Absent"),
                OvertimeCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Overtime")
            }).ToListAsync();
    }

    // 4. Export to Excel
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

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Attendance_Report_Jan2026.xlsx");
            }
        }
    }

    // 5. Export to PDF (Self-contained, no extra DLLs needed)
    public async Task<IActionResult> ExportToPdf()
    {
        var data = await GetReportData();

        // Build HTML string for the PDF
        var htmlContent = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; }}
                    .header {{ text-align: center; background-color: #BDD2E7; padding: 20px; border-bottom: 2px solid #a5bed9; }}
                    table {{ width: 100%; border-collapse: collapse; margin-top: 20px; }}
                    th {{ background-color: #4A77A5; color: white; padding: 12px; border: 1px solid #ddd; }}
                    td {{ border: 1px solid #ddd; padding: 10px; text-align: center; }}
                    .name-cell {{ text-align: left; font-weight: bold; color: #4A77A5; }}
                </style>
            </head>
            <body>
                <div class='header'>
                    <h2 style='margin:0;'>Attendance Report</h2>
                    <p style='margin:5px 0 0 0;'>2026 / Jan</p>
                </div>
                <table>
                    <thead>
                        <tr>
                            <th>Employee Name</th>
                            <th>Attendance</th>
                            <th>Late</th>
                            <th>Leave</th>
                            <th>Absent</th>
                            <th>Overtime</th>
                        </tr>
                    </thead>
                    <tbody>";

        foreach (var item in data)
        {
            htmlContent += $@"
                        <tr>
                            <td class='name-cell'>{item.Name}</td>
                            <td>{item.AttendanceCount}</td>
                            <td>{item.LateCount}</td>
                            <td>{item.LeaveCount}</td>
                            <td>{item.AbsentCount}</td>
                            <td>{item.OvertimeCount}</td>
                        </tr>";
        }

        htmlContent += "</tbody></table></body></html>";

        // Convert HTML string to PDF using SelectPdf
        HtmlToPdf converter = new HtmlToPdf();
        converter.Options.PdfPageSize = PdfPageSize.A4;
        converter.Options.PdfPageOrientation = PdfPageOrientation.Portrait;

        PdfDocument doc = converter.ConvertHtmlString(htmlContent);
        byte[] pdfFile = doc.Save();
        doc.Close();

        return File(pdfFile, "application/pdf", "Attendance_Report_Jan2026.pdf");
    }

    // 6. Admin Logout
    public IActionResult Logout()
    {
        HttpContext.Session.Remove("IsAdminAuthenticated");
        return RedirectToAction("AdminLogin");
    }
}