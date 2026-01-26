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
            ViewBag.CompassionateBalance = employee.EmergencyLeaveDays;
            ViewBag.OtherBalance = employee.OtherLeaveDays;

            // FIXED: Now pulls from the new database column you created
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

        if (leave.Start_Date.Date < DateTime.Today || leave.End_Date.Date < DateTime.Today)
        {
            return View("Error");
        }

        var employee = await _context.Employees.FindAsync(userId);
        if (employee == null) return View("Error");

        int requestedDays = (leave.End_Date - leave.Start_Date).Days + 1;
        bool hasInsufficientBalance = false;

        if (leave.LeaveType == "Unpaid")
        {
            hasInsufficientBalance = true;
        }
        else
        {
            int currentBalance = leave.LeaveType switch
            {
                "Annual" => employee.AnnualLeaveDays,
                "MC" => employee.MCDays,
                "Compassionate" => employee.EmergencyLeaveDays,
                "Other" => employee.OtherLeaveDays,
                // FIXED: Use the actual DB value for validation
                "Maternity Leave" => employee.MaternityLeaveDays,
                _ => 100
            };

            if (currentBalance <= 0 || requestedDays > currentBalance)
            {
                hasInsufficientBalance = true;
            }
        }

        if (hasInsufficientBalance)
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
    public async Task<IActionResult> SetEntitlements(int employeeId, string leaveCategory, int newValue)
    {
        if (HttpContext.Session.GetString("IsManager") != "true")
            return RedirectToAction("ApprovalLogin");

        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee != null)
        {
            switch (leaveCategory)
            {
                case "Annual": employee.AnnualLeaveDays = newValue; break;
                case "MC": employee.MCDays = newValue; break;
                case "Compassionate": employee.EmergencyLeaveDays = newValue; break;
                case "Other": employee.OtherLeaveDays = newValue; break;
                // FIXED: Added Maternity update logic for Admin
                case "Maternity": employee.MaternityLeaveDays = newValue; break;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Updated {leaveCategory} for {employee.First_Name}.";
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

            if (request.LeaveType == "Annual") request.Employee.AnnualLeaveDays -= totalDays;
            else if (request.LeaveType == "MC") request.Employee.MCDays -= totalDays;
            else if (request.LeaveType == "Compassionate") request.Employee.EmergencyLeaveDays -= totalDays;
            else if (request.LeaveType == "Other") request.Employee.OtherLeaveDays -= totalDays;
            // FIXED: Automatically deduct from Maternity balance column upon approval
            else if (request.LeaveType == "Maternity Leave") request.Employee.MaternityLeaveDays -= totalDays;

            // Generate Attendance
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
                {
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

    public async Task<IActionResult> MyStatus()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");
        return View(await _context.LeaveRequests.Where(l => l.Employee_ID == userId).OrderByDescending(l => l.Leave_ID).ToListAsync());
    }

    private IQueryable<LeaveRequest> ApplyFilters(IQueryable<LeaveRequest> query, string searchString, DateTime? fromDate, DateTime? toDate)
    {
        if (!String.IsNullOrEmpty(searchString))
        {
            query = query.Where(s => s.Employee.First_Name.Contains(searchString) || s.Employee.Last_Name.Contains(searchString) || s.Leave_ID.ToString() == searchString);
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
            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "Status";
            worksheet.Cell(1, 3).Value = "Type";
            worksheet.Cell(1, 4).Value = "Start";
            worksheet.Cell(1, 5).Value = "End";
            worksheet.Cell(1, 6).Value = "Employee";

            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = "REQ-" + item.Leave_ID;
                worksheet.Cell(row, 2).Value = item.Status;
                worksheet.Cell(row, 3).Value = item.LeaveType;
                worksheet.Cell(row, 4).Value = item.Start_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 5).Value = item.End_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 6).Value = $"{item.Employee?.First_Name} {item.Employee?.Last_Name}";
                row++;
            }
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
        return request == null ? NotFound() : View(request);
    }
}