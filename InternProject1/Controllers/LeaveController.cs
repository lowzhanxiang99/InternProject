using ClosedXML.Excel;
using InternProject1.Data;
using InternProject1.Models;
using InternProject1.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class LeaveController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IWebHostEnvironment _environment;

    public LeaveController(ApplicationDbContext context, IEmailService emailService, IWebHostEnvironment environment)
    {
        _context = context;
        _emailService = emailService;
        _environment = environment;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> CheckDuplicateLeave(int employeeId, DateTime start, DateTime end)
    {
        bool isDuplicate = await _context.LeaveRequests.AnyAsync(l =>
            l.Employee_ID == employeeId &&
            l.Status != "Rejected" &&
            start.Date <= l.End_Date.Date && end.Date >= l.Start_Date.Date);

        return Json(new { isDuplicate = isDuplicate });
    }

    public async Task<IActionResult> Apply()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var employee = await _context.Employees.FindAsync(userId);
        if (employee != null)
        {
            ViewBag.AnnualBalance = employee.AnnualLeaveDays;
            ViewBag.MCBalance = employee.MCDays;
            ViewBag.CompassionateBalance = employee.EmergencyLeaveDays;
            ViewBag.OtherBalance = employee.OtherLeaveDays;
            ViewBag.MaternityBalance = employee.MaternityLeaveDays;
        }
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(LeaveRequest leave, IFormFile? MCAttachment)
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        if (leave.Start_Date.Date < DateTime.Today || leave.End_Date.Date < DateTime.Today)
        {
            TempData["ErrorMessage"] = "Leave dates cannot be in the past.";
            return RedirectToAction(nameof(Apply));
        }

        if ((leave.LeaveType == "MC" || leave.LeaveType == "Medical Leave") && MCAttachment == null)
        {
            TempData["ErrorMessage"] = "A Medical Certificate (MC) attachment is required for this leave type.";
            return RedirectToAction(nameof(Apply));
        }

        bool isDuplicate = await _context.LeaveRequests.AnyAsync(l =>
            l.Employee_ID == userId &&
            l.Status != "Rejected" &&
            leave.Start_Date.Date <= l.End_Date.Date && leave.End_Date.Date >= l.Start_Date.Date);

        if (isDuplicate)
        {
            TempData["ErrorMessage"] = "You already have a leave record for these dates.";
            return RedirectToAction(nameof(Apply));
        }

        var employee = await _context.Employees.FindAsync(userId);
        if (employee == null) return View("Error");

        leave.Email = employee.Employee_Email;
        leave.Employee_ID = userId.Value;
        leave.Status = "Pending";
        leave.Request_Date = DateTime.Now;

        if (MCAttachment != null && MCAttachment.Length > 0)
        {
            try
            {
                string uploadFolder = Path.Combine(_environment.WebRootPath, "uploads", "mc");
                if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

                string uniqueFileName = Guid.NewGuid().ToString() + "_" + MCAttachment.FileName;
                string filePath = Path.Combine(uploadFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await MCAttachment.CopyToAsync(fileStream);
                }

                leave.AttachmentPath = "/uploads/mc/" + uniqueFileName;
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "Failed to upload MC attachment. Please try again.";
                return RedirectToAction(nameof(Apply));
            }
        }

        int requestedDays = (leave.End_Date.Date - leave.Start_Date.Date).Days + 1;
        bool hasInsufficientBalance = false;

        if (leave.LeaveType != "Unpaid")
        {
            int currentBalance = leave.LeaveType switch
            {
                "Annual" => employee.AnnualLeaveDays,
                "MC" => employee.MCDays,
                "Compassionate" => employee.EmergencyLeaveDays,
                "Other" => employee.OtherLeaveDays,
                "Maternity Leave" => employee.MaternityLeaveDays,
                _ => 100
            };

            if (currentBalance <= 0 || requestedDays > currentBalance) hasInsufficientBalance = true;
        }

        if (hasInsufficientBalance)
            leave.Reasons = "[SALARY DEDUCTION ADVISORY] " + leave.Reasons;

        _context.Add(leave);
        await _context.SaveChangesAsync();
        return View("Success");
    }

    public async Task<IActionResult> MyStatus()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var history = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId)
            .OrderByDescending(l => l.Request_Date)
            .ToListAsync();

        return View(history);
    }

    [HttpGet]
    public async Task<IActionResult> ExportMyStatusExcel()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var history = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId)
            .OrderByDescending(l => l.Request_Date)
            .ToListAsync();

        return GenerateExcelFile(history, $"My_Leave_Status_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // --- ADMIN SECTION ---

    public IActionResult ApprovalLogin() => View();

    [HttpPost]
    public IActionResult ApprovalLogin(string username, string password)
    {
        if (username == "admin@gmail.com" && password == "admin123")
        {
            HttpContext.Session.SetString("IsManager", "true");
            return RedirectToAction("Approval");
        }
        ViewBag.Error = "Warning! Only Authorized Users Are Allowed To Log In.";
        return View();
    }

    public async Task<IActionResult> Approval(string searchString, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        if (HttpContext.Session.GetString("IsManager") != "true") return RedirectToAction("ApprovalLogin");

        ViewBag.EmployeeList = await _context.Employees.OrderBy(e => e.Employee_ID).ToListAsync();

        var requestsQuery = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        requestsQuery = ApplyFilters(requestsQuery, searchString, fromDate, toDate);

        int pageSize = 10;
        int totalItems = await requestsQuery.CountAsync();
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        page = page < 1 ? 1 : (totalPages > 0 && page > totalPages ? totalPages : page);

        var data = await requestsQuery
            .OrderByDescending(s => s.Leave_ID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.SearchString = searchString;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View(data);
    }

    [HttpGet]
    public async Task<IActionResult> GetDetails(int id)
    {
        var request = await _context.LeaveRequests
            .Include(l => l.Employee)
            .FirstOrDefaultAsync(l => l.Leave_ID == id);

        if (request == null) return NotFound();

        return Json(new
        {
            leave_ID = request.Leave_ID,
            firstName = request.Employee?.First_Name ?? "Unknown",
            lastName = request.Employee?.Last_Name ?? "",
            leaveType = request.LeaveType,
            startDate = request.Start_Date.ToString("dd-MM-yy"),
            endDate = request.End_Date.ToString("dd-MM-yy"),
            reasons = request.Reasons,
            status = request.Status,
            attachmentPath = request.AttachmentPath
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var request = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Leave_ID == id);
        if (request == null) return NotFound();

        string oldStatus = request.Status;
        int totalDays = (request.End_Date.Date - request.Start_Date.Date).Days + 1;

        // Logic 1: Moving TO Approve (Deduct balance and add attendance)
        if (status == "Approve" && oldStatus != "Approve")
        {
            AdjustLeaveBalance(request.Employee, request.LeaveType, -totalDays); // Minus
            await SyncAttendanceRecords(request, true); // Add logs
        }
        // Logic 2: Moving FROM Approve TO Reject (Refund balance and remove attendance)
        else if (status == "Reject" && oldStatus == "Approve")
        {
            AdjustLeaveBalance(request.Employee, request.LeaveType, totalDays); // Plus back
            await SyncAttendanceRecords(request, false); // Remove logs
        }

        request.Status = status;
        await _context.SaveChangesAsync();

        // Email Notification Logic
        if (request.Employee != null && !string.IsNullOrEmpty(request.Employee.Employee_Email))
        {
            try
            {
                string subject = $"Leave Request REQ-{id}: {status}";
                string body = $@"<h3>Leave Status Update</h3>
                                 <p>Dear {request.Employee.First_Name},</p>
                                 <p>Your leave request (REQ-{id}) for <b>{request.LeaveType}</b> has been <b>{status}</b>.</p>
                                 <p>Dates: {request.Start_Date:dd-MM-yyyy} to {request.End_Date:dd-MM-yyyy}</p>";

                await _emailService.SendEmailAsync(request.Employee.Employee_Email, subject, body);
                TempData["Success"] = $"Request REQ-{id} updated to {status} and email sent.";
            }
            catch (Exception)
            {
                TempData["Success"] = $"Status updated to {status}, but email failed to send.";
            }
        }
        return RedirectToAction(nameof(Approval));
    }

    // --- HELPER METHODS ---

    private void AdjustLeaveBalance(Employee emp, string leaveType, int days)
    {
        if (leaveType == "Annual") emp.AnnualLeaveDays += days;
        else if (leaveType == "MC") emp.MCDays += days;
        else if (leaveType == "Compassionate") emp.EmergencyLeaveDays += days;
        else if (leaveType == "Other") emp.OtherLeaveDays += days;
        else if (leaveType == "Maternity Leave") emp.MaternityLeaveDays += days;
    }

    private async Task SyncAttendanceRecords(LeaveRequest request, bool isAdding)
    {
        for (DateTime date = request.Start_Date.Date; date <= request.End_Date.Date; date = date.AddDays(1))
        {
            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.Employee_ID == request.Employee_ID && a.Date.Date == date);

            if (isAdding)
            {
                if (existingRecord == null)
                {
                    _context.Attendances.Add(new Attendance { Employee_ID = request.Employee_ID, Date = date, Status = "Leave" });
                }
                else { existingRecord.Status = "Leave"; }
            }
            else
            {
                // If rejecting previously approved leave, remove the "Leave" record or reset it
                if (existingRecord != null && existingRecord.Status == "Leave")
                {
                    _context.Attendances.Remove(existingRecord);
                }
            }
        }
    }

    public async Task<IActionResult> EditEntitlements(int id)
    {
        if (HttpContext.Session.GetString("IsManager") != "true") return RedirectToAction("ApprovalLogin");
        var employee = await _context.Employees.FindAsync(id);
        return employee == null ? NotFound() : View(employee);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEntitlements(Employee updatedEmp)
    {
        if (HttpContext.Session.GetString("IsManager") != "true") return Unauthorized();

        var dbEmp = await _context.Employees.FindAsync(updatedEmp.Employee_ID);
        if (dbEmp != null)
        {
            dbEmp.AnnualLeaveDays = updatedEmp.AnnualLeaveDays;
            dbEmp.MCDays = updatedEmp.MCDays;
            dbEmp.EmergencyLeaveDays = updatedEmp.EmergencyLeaveDays;
            dbEmp.MaternityLeaveDays = updatedEmp.MaternityLeaveDays;
            dbEmp.OtherLeaveDays = updatedEmp.OtherLeaveDays;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Updated balances for {dbEmp.First_Name}.";
        }
        return RedirectToAction(nameof(Approval));
    }

    [HttpGet]
    public async Task<IActionResult> ExportToExcel(string searchString, DateTime? fromDate, DateTime? toDate)
    {
        var query = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        query = ApplyFilters(query, searchString, fromDate, toDate);
        return GenerateExcelFile(await query.ToListAsync(), "Admin_Leave_Report.xlsx");
    }

    private IQueryable<LeaveRequest> ApplyFilters(IQueryable<LeaveRequest> query, string searchString, DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrEmpty(searchString))
            query = query.Where(s => s.Employee.First_Name.Contains(searchString) || s.Employee.Last_Name.Contains(searchString) || s.Leave_ID.ToString() == searchString);
        if (fromDate.HasValue) query = query.Where(l => l.Start_Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(l => l.End_Date <= toDate.Value);
        return query;
    }

    private FileContentResult GenerateExcelFile(List<LeaveRequest> data, string fileName)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Leave Records");

        worksheet.Cell(1, 1).Value = "ID";
        worksheet.Cell(1, 2).Value = "Status";
        worksheet.Cell(1, 3).Value = "Type";
        worksheet.Cell(1, 4).Value = "Start Date";
        worksheet.Cell(1, 5).Value = "End Date";
        worksheet.Cell(1, 6).Value = "Reason";

        var headerRange = worksheet.Range("A1:F1");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        for (int i = 0; i < data.Count; i++)
        {
            var item = data[i];
            worksheet.Cell(i + 2, 1).Value = "REQ-" + item.Leave_ID;
            worksheet.Cell(i + 2, 2).Value = item.Status;
            worksheet.Cell(i + 2, 3).Value = item.LeaveType;
            worksheet.Cell(i + 2, 4).Value = item.Start_Date.ToString("dd-MM-yyyy");
            worksheet.Cell(i + 2, 5).Value = item.End_Date.ToString("dd-MM-yyyy");
            worksheet.Cell(i + 2, 6).Value = item.Reasons;
        }

        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}