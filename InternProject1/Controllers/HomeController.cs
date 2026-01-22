using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
using Microsoft.EntityFrameworkCore;
using InternProject1.Models;

namespace InternProject1.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        ViewBag.UserName = HttpContext.Session.GetString("UserName");

        // Fetch all Approved leave requests for this specific employee
        var userLeaves = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId && l.Status == "Approve")
            .ToListAsync();

        // 1. Set Total Entitlements (Based on your Admin Set screen)
        int totalAnnual = 7;
        int totalMC = 5;
        int totalCompassionate = 3;
        int totalMaternity = 60; // Standard example

        // 2. Calculate USED count for each of your 6 types
        // This checks both the short name (MC) and long name (Medical Leave)
        int annualUsed = userLeaves.Count(l => l.LeaveType == "Annual" || l.LeaveType == "Annual Leave");
        int mcUsed = userLeaves.Count(l => l.LeaveType == "MC" || l.LeaveType == "Medical Leave");
        int compassionateUsed = userLeaves.Count(l => l.LeaveType == "Compassionate" || l.LeaveType == "Emergency");
        int maternityUsed = userLeaves.Count(l => l.LeaveType == "Maternity Leave");
        int unpaidUsed = userLeaves.Count(l => l.LeaveType == "Unpaid" || l.LeaveType == "Unpaid Leave");
        int otherUsed = userLeaves.Count(l => l.LeaveType == "Other");

        // 3. Pass Data to View
        ViewBag.AnnualLeave = $"{(totalAnnual - annualUsed):D2}/{totalAnnual:D2}";
        ViewBag.AnnualAvailable = totalAnnual - annualUsed;
        ViewBag.AnnualUsed = annualUsed;

        ViewBag.MCLeave = $"{(totalMC - mcUsed):D2}/{totalMC:D2}";
        ViewBag.MCAvailable = totalMC - mcUsed;
        ViewBag.MCUsed = mcUsed;

        ViewBag.CompassionateLeave = $"{(totalCompassionate - compassionateUsed):D2}/{totalCompassionate:D2}";
        ViewBag.CompassionateAvailable = totalCompassionate - compassionateUsed;
        ViewBag.CompassionateUsed = compassionateUsed;

        ViewBag.MaternityLeave = $"{(totalMaternity - maternityUsed):D2}/{totalMaternity:D2}";
        ViewBag.MaternityAvailable = totalMaternity - maternityUsed;
        ViewBag.MaternityUsed = maternityUsed;

        ViewBag.UnpaidUsed = unpaidUsed;
        ViewBag.OtherUsed = otherUsed;

        // Attendance Insights (Kept Hardcoded as requested)
        ViewBag.OnTimePercentage = 65;
        ViewBag.LatePercentage = 35;
        ViewBag.TotalBreakHours = "00 Hours 40 Minutes 55 Seconds";
        ViewBag.TotalWorkingHours = "08 Hours 15 Minutes 10 Seconds";

        return View();
    }
}