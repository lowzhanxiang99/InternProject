using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

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

    // 2. Validate the specific admin@gmail.com credentials
    [HttpPost]
    public IActionResult VerifyAdmin(string email, string password)
    {
        if (email == "admin@gmail.com" && password == "admin123")
        {
            // Set a specific session key for the report access
            HttpContext.Session.SetString("IsAdminAuthenticated", "true");
            return RedirectToAction("Index");
        }

        ViewBag.Error = "Invalid Admin Credentials";
        return View("AdminLogin");
    }

    // 3. GET: AttendanceReport/Index (The actual Report)
    public async Task<IActionResult> Index()
    {
        // Guard: Only allow access if the admin-specific session is active
        if (HttpContext.Session.GetString("IsAdminAuthenticated") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        // Fetching combined data for all staff
        var reportData = await _context.Employees
            .Select(e => new StaffSummaryViewModel
            {
                Name = e.Employee_Name,
                AttendanceCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Present"),
                LateCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Late"),
                LeaveCount = _context.LeaveRequests.Count(l => l.Employee_ID == e.Employee_ID && l.Status == "Approve"),
                AbsentCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Absent"),
                OvertimeCount = _context.Attendances.Count(a => a.Employee_ID == e.Employee_ID && a.Status == "Overtime")
            }).ToListAsync();

        return View(reportData);
    }
}