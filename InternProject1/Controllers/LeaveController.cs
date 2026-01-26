using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using System.IO;

public class LeaveController : Controller
{
    private readonly ApplicationDbContext _context;

    public LeaveController(ApplicationDbContext context) => _context = context;

    public IActionResult Index() => View();

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

        if (leave.Start_Date.Date < DateTime.Today || leave.End_Date.Date < DateTime.Today) return View("Error");

        var employee = await _context.Employees.FindAsync(userId);
        if (employee == null) return View("Error");

        int requestedDays = (leave.End_Date - leave.Start_Date).Days + 1;
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
            .OrderByDescending(l => l.Leave_ID)
            .ToListAsync();

        return View(history);
    }

    // MISSING METHOD ADDED HERE: Handles the Export button in Mystatus.cshtml
    [HttpGet]
    public async Task<IActionResult> ExportMyStatusExcel()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var data = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId)
            .OrderByDescending(l => l.Leave_ID)
            .ToListAsync();

        return GenerateExcelFile(data, "My_Leave_History.xlsx");
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

    public async Task<IActionResult> Approval(string searchString, DateTime? fromDate, DateTime? toDate)
    {
        if (HttpContext.Session.GetString("IsManager") != "true") return RedirectToAction("ApprovalLogin");

        ViewBag.EmployeeList = await _context.Employees.OrderBy(e => e.First_Name).ToListAsync();

        var requests = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        requests = ApplyFilters(requests, searchString, fromDate, toDate);

        return View(await requests.OrderByDescending(s => s.Leave_ID).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var request = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Leave_ID == id);

        if (request != null && status == "Approve" && request.Status != "Approve")
        {
            int totalDays = (request.End_Date - request.Start_Date).Days + 1;

            // Deduct from Balance
            if (request.LeaveType == "Annual") request.Employee.AnnualLeaveDays -= totalDays;
            else if (request.LeaveType == "MC") request.Employee.MCDays -= totalDays;
            else if (request.LeaveType == "Compassionate") request.Employee.EmergencyLeaveDays -= totalDays;
            else if (request.LeaveType == "Other") request.Employee.OtherLeaveDays -= totalDays;
            else if (request.LeaveType == "Maternity Leave") request.Employee.MaternityLeaveDays -= totalDays;

            // Mark Attendance as 'Leave'
            for (DateTime date = request.Start_Date.Date; date <= request.End_Date.Date; date = date.AddDays(1))
            {
                var existing = await _context.Attendances.FirstOrDefaultAsync(a => a.Employee_ID == request.Employee_ID && a.Date.Date == date);
                if (existing == null)
                    _context.Attendances.Add(new Attendance { Employee_ID = request.Employee_ID, Date = date, Status = "Leave", ClockInTime = TimeSpan.Zero });
                else
                    existing.Status = "Leave";
            }
        }

        if (request != null)
        {
            request.Status = status;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Approval));
    }

    // --- ENTITLEMENT MANAGEMENT ---

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