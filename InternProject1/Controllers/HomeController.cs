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

        var employee = await _context.Employees.FindAsync(userId);
        if (employee == null) return RedirectToAction("Login", "Account");

        var userLeaves = await _context.LeaveRequests
            .Where(l => l.Employee_ID == userId && l.Status == "Approve")
            .ToListAsync();

        // 1. Get Entitlements from DB
        int totalAnnual = employee.AnnualLeaveDays;
        int totalMC = employee.MCDays;
        int totalCompassionate = employee.EmergencyLeaveDays;
        int totalMaternity = employee.MaternityLeaveDays;
        int totalOther = employee.OtherLeaveDays;

        // 2. Calculate raw USED days from the database
        int rawAnnualUsed = userLeaves.Where(l => l.LeaveType == "Annual" || l.LeaveType == "Annual Leave").Sum(l => (l.End_Date - l.Start_Date).Days + 1);
        int mcUsed = userLeaves.Where(l => l.LeaveType == "MC" || l.LeaveType == "Medical Leave").Sum(l => (l.End_Date - l.Start_Date).Days + 1);
        int compassionateUsed = userLeaves.Where(l => l.LeaveType == "Compassionate" || l.LeaveType == "Emergency").Sum(l => (l.End_Date - l.Start_Date).Days + 1);
        int maternityUsed = userLeaves.Where(l => l.LeaveType == "Maternity" || l.LeaveType == "Maternity Leave").Sum(l => (l.End_Date - l.Start_Date).Days + 1);
        int rawUnpaidUsed = userLeaves.Where(l => l.LeaveType == "Unpaid" || l.LeaveType == "Unpaid Leave").Sum(l => (l.End_Date - l.Start_Date).Days + 1);
        int otherUsed = userLeaves.Where(l => l.LeaveType == "Other").Sum(l => (l.End_Date - l.Start_Date).Days + 1);

        // 3. Logic Fix: Handle Overflow (prevent negative available balance)
        // If Annual Used (7) > Total Annual (5), cap used at 5 and move 2 to unpaid.
        int displayAnnualUsed = rawAnnualUsed > totalAnnual ? totalAnnual : rawAnnualUsed;
        int annualOverflow = rawAnnualUsed > totalAnnual ? rawAnnualUsed - totalAnnual : 0;

        // 4. Set ViewBag for Leave Cards
        // Annual Leave Card
        ViewBag.AnnualLeave = $"{displayAnnualUsed:D2}/{totalAnnual:D2}";
        ViewBag.AnnualAvailable = totalAnnual - displayAnnualUsed; // Result: 00 instead of -02
        ViewBag.AnnualUsed = rawAnnualUsed; // Keep actual total for reference if needed

        // MC Leave Card
        ViewBag.MCLeave = $"{mcUsed:D2}/{totalMC:D2}";
        ViewBag.MCAvailable = Math.Max(0, totalMC - mcUsed);
        ViewBag.MCUsed = mcUsed;

        // Compassionate Leave Card
        ViewBag.CompassionateLeave = $"{compassionateUsed:D2}/{totalCompassionate:D2}";
        ViewBag.CompassionateAvailable = Math.Max(0, totalCompassionate - compassionateUsed);
        ViewBag.CompassionateUsed = compassionateUsed;

        // Maternity Leave Card
        ViewBag.MaternityLeave = $"{maternityUsed:D2}/{totalMaternity:D2}";
        ViewBag.MaternityAvailable = Math.Max(0, totalMaternity - maternityUsed);
        ViewBag.MaternityUsed = maternityUsed;

        // Other Leave Card
        ViewBag.OtherLeave = $"{otherUsed:D2}/{totalOther:D2}";
        ViewBag.OtherAvailable = Math.Max(0, totalOther - otherUsed);
        ViewBag.OtherUsed = otherUsed;

        // Unpaid Leave (Includes raw unpaid requests + overflow from annual leave)
        ViewBag.UnpaidUsed = rawUnpaidUsed + annualOverflow;

        // Attendance Insights (Hardcoded for now)
        ViewBag.OnTimePercentage = 65;
        ViewBag.LatePercentage = 35;
        ViewBag.TotalBreakHours = "00 Hours 40 Minutes 55 Seconds";
        ViewBag.TotalWorkingHours = "08 Hours 15 Minutes 10 Seconds";

        return View();
    }
}