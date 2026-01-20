using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;

namespace InternProject1.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        // Profile Data
        ViewBag.UserName = HttpContext.Session.GetString("UserName");

        // Leave Balance - Pulling from DB (Example placeholders)
        ViewBag.AnnualLeave = "05/07";
        ViewBag.MedicalLeave = "05/05";
        ViewBag.CompassionateLeave = "03/03";
        ViewBag.UnpaidLeave = "00/00";
        ViewBag.HalfDayLeave = "00/00";

        // Attendance Insights
        ViewBag.OnTimePercentage = 65;
        ViewBag.LatePercentage = 35;

        // Calculated Time Records
        ViewBag.TotalBreakHours = "00 Hours 40 Minutes 55 Seconds";
        ViewBag.TotalWorkingHours = "08 Hours 15 Minutes 10 Seconds";

        return View();
    }
}