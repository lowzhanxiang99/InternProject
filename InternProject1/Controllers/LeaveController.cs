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
    public IActionResult Apply()
    {
        if (HttpContext.Session.GetInt32("UserID") == null)
            return RedirectToAction("Login", "Account");
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
        ViewData["DateSortParm"] = sortOrder == "Date" ? "date_desc" : "Date";
        ViewData["StatusSortParm"] = sortOrder == "Status" ? "status_desc" : "Status";

        var requests = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        requests = ApplyFilters(requests, searchString, fromDate, toDate);

        requests = sortOrder switch
        {
            "name_desc" => requests.OrderByDescending(s => s.Employee.Last_Name),
            "Date" => requests.OrderBy(s => s.Start_Date),
            "date_desc" => requests.OrderByDescending(s => s.Start_Date),
            "Status" => requests.OrderBy(s => s.Status),
            "status_desc" => requests.OrderByDescending(s => s.Status),
            _ => requests.OrderBy(s => s.Employee.Last_Name),
        };

        return View(await requests.ToListAsync());
    }

    // Export ALL (For Admin)
    [HttpGet]
    public async Task<IActionResult> ExportToExcel(string searchString, DateTime? fromDate, DateTime? toDate)
    {
        var query = _context.LeaveRequests.Include(l => l.Employee).AsQueryable();
        query = ApplyFilters(query, searchString, fromDate, toDate);
        var data = await query.ToListAsync();
        return GenerateExcelFile(data, "All_Leave_Requests.xlsx");
    }

    // --- PERSONAL USER SECTION ---

    // GET: Leave/MyStatus
    public async Task<IActionResult> MyStatus()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var myRequests = await _context.LeaveRequests
                                       .Where(l => l.Employee_ID == userId)
                                       .OrderByDescending(l => l.Request_Date)
                                       .ToListAsync();

        return View(myRequests);
    }

    // Export Personal Records ONLY
    [HttpGet]
    public async Task<IActionResult> ExportMyStatusExcel()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var data = await _context.LeaveRequests
                                 .Include(l => l.Employee)
                                 .Where(l => l.Employee_ID == userId)
                                 .ToListAsync();

        return GenerateExcelFile(data, "My_Leave_History.xlsx");
    }

    // --- SHARED UTILITIES ---

    private IQueryable<LeaveRequest> ApplyFilters(IQueryable<LeaveRequest> query, string searchString, DateTime? fromDate, DateTime? toDate)
    {
        if (!String.IsNullOrEmpty(searchString))
            query = query.Where(s => s.Employee.First_Name.Contains(searchString) || s.Employee.Last_Name.Contains(searchString));

        if (fromDate.HasValue) query = query.Where(l => l.Start_Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(l => l.End_Date <= toDate.Value);

        return query;
    }

    private FileContentResult GenerateExcelFile(List<LeaveRequest> data, string fileName)
    {
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Leave Records");
            worksheet.Cell(1, 1).Value = "Request Date";
            worksheet.Cell(1, 2).Value = "Status";
            worksheet.Cell(1, 3).Value = "Start Date";
            worksheet.Cell(1, 4).Value = "End Date";
            worksheet.Cell(1, 5).Value = "Employee Name";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.Request_Date?.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 2).Value = item.Status;
                worksheet.Cell(row, 3).Value = item.Start_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 4).Value = item.End_Date.ToString("dd-MM-yyyy");
                worksheet.Cell(row, 5).Value = $"{item.Employee?.First_Name} {item.Employee?.Last_Name}";
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

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var request = await _context.LeaveRequests.FindAsync(id);
        if (request != null)
        {
            request.Status = status;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Approval));
    }
}