using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore; 
using Microsoft.AspNetCore.Mvc;

public class LeaveController : Controller
{
    private readonly ApplicationDbContext _context;

    public LeaveController(ApplicationDbContext context) => _context = context;

    // Main Selection Page
    public IActionResult Index() => View();

    // GET: Leave Application Form
    public IActionResult Apply() => View();

    // POST: Submit Leave
    [HttpPost]
    public async Task<IActionResult> Apply(LeaveRequest leave)
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        try
        {
            leave.Employee_ID = userId.Value;
            _context.Add(leave);
            await _context.SaveChangesAsync();
            return View("Success"); // Shows green success box
        }
        catch
        {
            return View("Error"); // Shows red failure box
        }
    }

    // GET: Leave Approval List (Admin Only)
    public async Task<IActionResult> Approval()
    {
        var requests = await _context.LeaveRequests.Include(l => l.Employee).ToListAsync();
        return View(requests);
    }
}