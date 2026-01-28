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

        // Attendance Insights
        CalculateAttendanceInsights(userId.Value);


        return View();
    }

    private void CalculateAttendanceInsights(int userId)
    {
        // Get current month and year for comparison
        var now = DateTime.Now;
        var currentMonth = now.Month;
        var currentYear = now.Year;
        var previousMonth = currentMonth == 1 ? 12 : currentMonth - 1;
        var previousYear = currentMonth == 1 ? currentYear - 1 : currentYear;

        // Get attendance records for current month
        var currentMonthAttendances = _context.Attendances
            .Where(a => a.Employee_ID == userId
                && a.Date.Month == currentMonth
                && a.Date.Year == currentYear)
            .ToList();

        // Get attendance records for previous month (for comparison)
        var previousMonthAttendances = _context.Attendances
            .Where(a => a.Employee_ID == userId
                && a.Date.Month == previousMonth
                && a.Date.Year == previousYear)
            .ToList();

        // 1. On-Time Percentage Calculation
        TimeSpan onTimeThreshold = TimeSpan.FromHours(9); // 9:00 AM

        int currentOnTimeCount = currentMonthAttendances
            .Where(a => a.ClockInTime.HasValue)
            .Count(a => a.ClockInTime.Value <= onTimeThreshold);

        int previousOnTimeCount = previousMonthAttendances
            .Where(a => a.ClockInTime.HasValue)
            .Count(a => a.ClockInTime.Value <= onTimeThreshold);

        int currentTotalWithClockIn = currentMonthAttendances.Count(a => a.ClockInTime.HasValue);
        int previousTotalWithClockIn = previousMonthAttendances.Count(a => a.ClockInTime.HasValue);

        double currentOnTimePercentage = currentTotalWithClockIn > 0
            ? Math.Round((double)currentOnTimeCount / currentTotalWithClockIn * 100)
            : 0;

        double previousOnTimePercentage = previousTotalWithClockIn > 0
            ? Math.Round((double)previousOnTimeCount / previousTotalWithClockIn * 100)
            : 0;

        double onTimeChange = previousOnTimePercentage == 0 ? 0
            : Math.Round(((currentOnTimePercentage - previousOnTimePercentage) / previousOnTimePercentage * 100));

        ViewBag.OnTimePercentage = (int)currentOnTimePercentage;
        ViewBag.OnTimeChange = onTimeChange;
        ViewBag.OnTimeChangeDirection = onTimeChange >= 0 ? "up" : "down";

        // 2. Late Percentage Calculation
        int currentLateCount = currentMonthAttendances
            .Where(a => a.ClockInTime.HasValue)
            .Count(a => a.ClockInTime.Value > onTimeThreshold);

        int previousLateCount = previousMonthAttendances
            .Where(a => a.ClockInTime.HasValue)
            .Count(a => a.ClockInTime.Value > onTimeThreshold);

        double currentLatePercentage = currentTotalWithClockIn > 0
            ? Math.Round((double)currentLateCount / currentTotalWithClockIn * 100)
            : 0;

        double previousLatePercentage = previousTotalWithClockIn > 0
            ? Math.Round((double)previousLateCount / previousTotalWithClockIn * 100)
            : 0;

        double lateChange = previousLatePercentage == 0 ? 0
            : Math.Round(((currentLatePercentage - previousLatePercentage) / previousLatePercentage * 100));

        ViewBag.LatePercentage = (int)currentLatePercentage;
        ViewBag.LateChange = lateChange;
        ViewBag.LateChangeDirection = lateChange >= 0 ? "up" : "down";

        // 3. Total Break Hours Calculation
        // Use TotalBreakTime from your model
        TimeSpan currentMonthBreakTotal = currentMonthAttendances
            .Where(a => a.TotalBreakTime.HasValue)
            .Select(a => a.TotalBreakTime.Value)
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        TimeSpan previousMonthBreakTotal = previousMonthAttendances
            .Where(a => a.TotalBreakTime.HasValue)
            .Select(a => a.TotalBreakTime.Value)
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        double breakChangePercentage = previousMonthBreakTotal.TotalHours == 0 ? 0
            : Math.Round(((currentMonthBreakTotal.TotalHours - previousMonthBreakTotal.TotalHours) / previousMonthBreakTotal.TotalHours * 100));

        ViewBag.TotalBreakHours = FormatTimeSpan(currentMonthBreakTotal);
        ViewBag.BreakChange = (int)breakChangePercentage;
        ViewBag.BreakChangeDirection = breakChangePercentage >= 0 ? "up" : "down";

        // 4. Total Working Hours Calculation
        // Calculate working hours from ClockInTime to ClockOutTime
        TimeSpan currentMonthWorkTotal = currentMonthAttendances
            .Where(a => a.ClockInTime.HasValue && a.ClockOutTime.HasValue)
            .Select(a => {
                // Calculate work duration (excluding breaks)
                TimeSpan workDuration = a.ClockOutTime.Value - a.ClockInTime.Value;

                // Subtract break time if available
                if (a.TotalBreakTime.HasValue)
                {
                    workDuration = workDuration.Subtract(a.TotalBreakTime.Value);
                }

                // Ensure non-negative
                return workDuration.TotalSeconds > 0 ? workDuration : TimeSpan.Zero;
            })
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        TimeSpan previousMonthWorkTotal = previousMonthAttendances
            .Where(a => a.ClockInTime.HasValue && a.ClockOutTime.HasValue)
            .Select(a => {
                TimeSpan workDuration = a.ClockOutTime.Value - a.ClockInTime.Value;
                if (a.TotalBreakTime.HasValue)
                {
                    workDuration = workDuration.Subtract(a.TotalBreakTime.Value);
                }
                return workDuration.TotalSeconds > 0 ? workDuration : TimeSpan.Zero;
            })
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        double workChangePercentage = previousMonthWorkTotal.TotalHours == 0 ? 0
            : Math.Round(((currentMonthWorkTotal.TotalHours - previousMonthWorkTotal.TotalHours) / previousMonthWorkTotal.TotalHours * 100));

        ViewBag.TotalWorkingHours = FormatTimeSpan(currentMonthWorkTotal);
        ViewBag.WorkChange = (int)workChangePercentage;
        ViewBag.WorkChangeDirection = workChangePercentage >= 0 ? "up" : "down";

        // Additional useful stats you might want to display
        ViewBag.TotalWorkingDays = currentMonthAttendances.Count(a => a.ClockInTime.HasValue);
        ViewBag.AverageDailyHours = currentMonthAttendances.Count(a => a.ClockInTime.HasValue) > 0
            ? FormatTimeSpan(TimeSpan.FromHours(currentMonthWorkTotal.TotalHours / currentMonthAttendances.Count(a => a.ClockInTime.HasValue)))
            : "00 Hours 00 Minutes 00 Seconds";
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        int hours = (int)timeSpan.TotalHours;
        int minutes = timeSpan.Minutes;
        int seconds = timeSpan.Seconds;

        return $"{hours:D2} Hours {minutes:D2} Minutes {seconds:D2} Seconds";
    }
}