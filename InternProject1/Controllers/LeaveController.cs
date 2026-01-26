using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class LeaveController : Controller
{
    private readonly ApplicationDbContext _context;

    public LeaveController(ApplicationDbContext context) => _context = context;

    public IActionResult Index() => View();

    // NEW: AJAX helper to check for date overlaps before submission
    [HttpGet]
    public async Task<IActionResult> CheckDuplicateLeave(int employeeId, DateTime start, DateTime end)
    {
        // Check if any existing leave request for this employee overlaps with the selected range
        bool isDuplicate = await _context.LeaveRequests.AnyAsync(l =>
            l.Employee_ID == employeeId &&
            l.Status != "Rejected" && // Only check against Pending or Approved leaves
            start.Date <= l.End_Date.Date && end.Date >= l.Start_Date.Date);

        return Json(new { isDuplicate = isDuplicate });
    }

    // GET: Leave/Apply
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

    // POST: Leave/Apply
    [HttpPost]
    public async Task<IActionResult> Apply(LeaveRequest leave)
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        // 1. Validation: Dates must not be in the past
        if (leave.Start_Date.Date < DateTime.Today || leave.End_Date.Date < DateTime.Today) return View("Error");

        // 2. Server-side Duplicate Check (Security Layer)
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

        leave.Employee_ID = userId.Value;
        leave.Status = "Pending";
        leave.Request_Date = DateTime.Now;

        _context.Add(leave);
        await _context.SaveChangesAsync();
        return View("Success");
    }

    // --- PERSONAL HISTORY SECTION ---

    public async Task<IActionResult> MyStatus()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var history = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId)
            .OrderBy(l => l.Leave_ID)
            .ToListAsync();

        return View(history);
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
            status = request.Status
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var request = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Leave_ID == id);

        if (request != null && status == "Approve" && request.Status != "Approve")
        {
            int totalDays = (request.End_Date.Date - request.Start_Date.Date).Days + 1;

            if (request.LeaveType == "Annual") request.Employee.AnnualLeaveDays -= totalDays;
            else if (request.LeaveType == "MC") request.Employee.MCDays -= totalDays;
            else if (request.LeaveType == "Compassionate") request.Employee.EmergencyLeaveDays -= totalDays;
            else if (request.LeaveType == "Other") request.Employee.OtherLeaveDays -= totalDays;
            else if (request.LeaveType == "Maternity Leave") request.Employee.MaternityLeaveDays -= totalDays;

            for (DateTime date = request.Start_Date.Date; date <= request.End_Date.Date; date = date.AddDays(1))
            {
                var existingRecord = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.Employee_ID == request.Employee_ID && a.Date.Date == date);

                if (existingRecord == null)
                {
                    _context.Attendances.Add(new Attendance
                    {
                        Employee_ID = request.Employee_ID,
                        Date = date,
                        Status = "Leave",
                        ClockInTime = null
                    });
                }
                else
                    existingRecord.Status = "Leave";
            }
        }

        if (request != null)
        {
            request.Status = status;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Request REQ-{id} updated to {status}.";
        }
        return RedirectToAction(nameof(Approval));
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

    // --- HELPERS ---

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
        worksheet.Cell(1, 1).Value = "ID"; worksheet.Cell(1, 2).Value = "Status";
        worksheet.Cell(1, 3).Value = "Type"; worksheet.Cell(1, 4).Value = "Start Date";
        worksheet.Cell(1, 5).Value = "End Date"; worksheet.Cell(1, 6).Value = "Reason";

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