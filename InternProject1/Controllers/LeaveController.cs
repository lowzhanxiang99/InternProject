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
        if (userId == null)
            return RedirectToAction("Login", "Account");

        var employee = await _context.Employees.FindAsync(userId);
        if (employee != null)
        {
            ViewBag.AnnualBalance = employee.AnnualLeaveDays;
            ViewBag.MCBalance = employee.MCDays;
            ViewBag.EmergencyBalance = employee.EmergencyLeaveDays;
            ViewBag.OtherBalance = employee.OtherLeaveDays;
        }

        return View();
    }

    // POST: Leave/Apply
    [HttpPost]
    public async Task<IActionResult> Apply(LeaveRequest leave)
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        if (leave.Start_Date.Date < DateTime.Today || leave.End_Date.Date < DateTime.Today)
        {
            return View("Error");
        }

        var employee = await _context.Employees.FindAsync(userId);
        if (employee == null) return View("Error");

        int requestedDays = (leave.End_Date - leave.Start_Date).Days + 1;

        int currentBalance = leave.LeaveType switch
        {
            "Annual" => employee.AnnualLeaveDays,
            "MC" => employee.MCDays,
            "Emergency" => employee.EmergencyLeaveDays,
            _ => employee.OtherLeaveDays
        };

        if (currentBalance <= 0 || requestedDays > currentBalance)
        {
            leave.Reasons = "[SALARY DEDUCTION ADVISORY] " + leave.Reasons;
        }

        try
        {
            leave.Employee_ID = userId.Value;
            leave.Status = "Pending";
            leave.Request_Date = DateTime.Now;

            _context.Add(leave);
            await _context.SaveChangesAsync();
            return View("Success");
        }
        catch
        {
            return View("Error");
        }
    }

    // --- ADMIN/MANAGER SECTION ---

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

    public async Task<IActionResult> Approval(string searchString, DateTime? fromDate, DateTime? toDate, string sortOrder)
    {
        if (HttpContext.Session.GetString("IsManager") != "true")
            return RedirectToAction("ApprovalLogin");

        ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
        ViewData["IdSortParm"] = sortOrder == "id_asc" ? "id_desc" : "id_asc";
        ViewData["StatusSortParm"] = sortOrder == "Status" ? "status_desc" : "Status";

        ViewBag.EmployeeList = await _context.Employees.ToListAsync();

        var requests = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        requests = ApplyFilters(requests, searchString, fromDate, toDate);

        requests = sortOrder switch
        {
            "name_desc" => requests.OrderByDescending(s => s.Employee.Last_Name),
            "id_asc" => requests.OrderBy(s => s.Leave_ID),
            "id_desc" => requests.OrderByDescending(s => s.Leave_ID),
            "Status" => requests.OrderBy(s => s.Status),
            "status_desc" => requests.OrderByDescending(s => s.Status),
            _ => requests.OrderByDescending(s => s.Leave_ID),
        };

        return View(await requests.ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> SetEntitlements(int employeeId, int annual, int mc, int emergency, int other)
    {
        if (HttpContext.Session.GetString("IsManager") != "true")
            return RedirectToAction("ApprovalLogin");

        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee != null)
        {
            employee.AnnualLeaveDays = annual;
            employee.MCDays = mc;
            employee.EmergencyLeaveDays = emergency;
            employee.OtherLeaveDays = other;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Entitlements updated for {employee.First_Name}.";
        }
        return RedirectToAction(nameof(Approval));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var request = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Leave_ID == id);

        if (request != null && status == "Approve" && request.Status != "Approve")
        {
            int totalDays = (request.End_Date - request.Start_Date).Days + 1;

            // 1. Subtract from balance 
            if (request.LeaveType == "Annual") request.Employee.AnnualLeaveDays -= totalDays;
            else if (request.LeaveType == "MC") request.Employee.MCDays -= totalDays;
            else if (request.LeaveType == "Emergency") request.Employee.EmergencyLeaveDays -= totalDays;
            else if (request.LeaveType == "Other") request.Employee.OtherLeaveDays -= totalDays;

            // 2. NEW LOGIC: Generate Attendance records for the approved dates
            // This makes the leave "visible" in the Attendance Report/Details
            for (DateTime date = request.Start_Date.Date; date <= request.End_Date.Date; date = date.AddDays(1))
            {
                // Check if a record already exists for this day to avoid duplicates
                var existingRecord = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.Employee_ID == request.Employee_ID && a.Date.Date == date);

                if (existingRecord == null)
                {
                    var leaveAttendance = new Attendance
                    {
                        Employee_ID = request.Employee_ID,
                        Date = date,
                        Status = "Leave", // Matches your logic in the View
                        ClockInTime = TimeSpan.Zero,
                        ClockOutTime = null
                    };
                    _context.Attendances.Add(leaveAttendance);
                }
                else
                {
                    // If a record (like 'Absent') existed, change it to 'Leave'
                    existingRecord.Status = "Leave";
                }
            }
        }

        if (request != null)
        {
            request.Status = status;
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Approval));
    }

    [HttpGet]
    public async Task<IActionResult> ExportToExcel(string searchString, DateTime? fromDate, DateTime? toDate)
    {
        var query = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        query = ApplyFilters(query, searchString, fromDate, toDate);
        var data = await query.ToListAsync();
        return GenerateExcelFile(data, "All_Leave_Requests.xlsx");
    }

    // --- PERSONAL USER SECTION ---

    public async Task<IActionResult> MyStatus()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var myRequests = await _context.LeaveRequests
                                       .Where(l => l.Employee_ID == userId)
                                       .OrderByDescending(l => l.Leave_ID)
                                       .ToListAsync();

        return View(myRequests);
    }

    [HttpGet]
    public async Task<IActionResult> ExportMyStatusExcel()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var data = await _context.LeaveRequests
                                 .Include(l => l.Employee)
                                 .Where(l => l.Employee_ID == userId)
                                 .OrderByDescending(l => l.Leave_ID)
                                 .ToListAsync();

        return GenerateExcelFile(data, "My_Leave_History.xlsx");
    }

    // --- SHARED UTILITIES ---

    private IQueryable<LeaveRequest> ApplyFilters(IQueryable<LeaveRequest> query, string searchString, DateTime? fromDate, DateTime? toDate)
    {
        if (!String.IsNullOrEmpty(searchString))
        {
            query = query.Where(s => s.Employee.First_Name.Contains(searchString) ||
                                     s.Employee.Last_Name.Contains(searchString) ||
                                     s.Leave_ID.ToString() == searchString);
        }

        if (fromDate.HasValue) query = query.Where(l => l.Start_Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(l => l.End_Date <= toDate.Value);

        return query;
    }

    private FileContentResult GenerateExcelFile(List<LeaveRequest> data, string fileName)
    {
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Leave Records");

            worksheet.Cell(1, 1).Value = "Request ID";
            worksheet.Cell(1, 2).Value = "Status";
            worksheet.Cell(1, 3).Value = "Leave Type";
            worksheet.Cell(1, 4).Value = "Start Date";
            worksheet.Cell(1, 5).Value = "End Date";
            worksheet.Cell(1, 6).Value = "Employee Name";
            worksheet.Cell(1, 7).Value = "Reason";
            worksheet.Cell(1, 8).Value = "Submitted On";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = "REQ-" + item.Leave_ID;
                worksheet.Cell(row, 2).Value = item.Status;
                worksheet.Cell(row, 3).Value = item.LeaveType;
                worksheet.Cell(row, 4).Value = item.Start_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 5).Value = item.End_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 6).Value = $"{item.Employee?.First_Name} {item.Employee?.Last_Name}";
                worksheet.Cell(row, 7).Value = item.Reasons;
                worksheet.Cell(row, 8).Value = item.Request_Date?.ToString("dd-MM-yyyy HH:mm");

                if (item.Reasons != null && item.Reasons.Contains("[SALARY DEDUCTION ADVISORY]"))
                {
                    worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8D7DA");
                    worksheet.Cell(row, 7).Style.Font.FontColor = XLColor.DarkRed;
                }
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
        }
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var request = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(m => m.Leave_ID == id);
        if (request == null) return NotFound();
        return View(request);
    }
}