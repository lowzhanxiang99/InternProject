using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Controllers
{
    public class MonitoringController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MonitoringController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Monitoring/AdminLogin
        public IActionResult AdminLogin()
        {
            // Check session instead of User.Identity
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId != null && isAdmin == "true")
                return RedirectToAction("Index");

            return View();
        }

        // POST: Monitoring/AdminLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminLogin(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email and password are required.");
                return View();
            }

            // Find admin user
            var admin = await _context.Employees
                .FirstOrDefaultAsync(e => e.Employee_Email == email && e.Password == password && e.Role == "Admin");

            if (admin != null)
            {
                HttpContext.Session.SetInt32("AdminID", admin.Employee_ID);
                HttpContext.Session.SetString("AdminName", $"{admin.First_Name} {admin.Last_Name}");
                HttpContext.Session.SetString("AdminEmail", admin.Employee_Email);
                HttpContext.Session.SetString("AdminRole", admin.Role);
                HttpContext.Session.SetString("IsAdmin", "true");

                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Invalid admin credentials or insufficient permissions.");
            return View();
        }

        // GET: Monitoring/Index (Monitoring Dashboard) - Protected - Admin only
        public async Task<IActionResult> Index()
        {
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId == null || isAdmin != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            // Get all employees with their departments and shifts
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Shift)
                .Where(e => e.Role == "Staff")
                .OrderBy(e => e.Department != null ? e.Department.Department_Name : "ZZZ")
                .ThenBy(e => e.First_Name)
                .ToListAsync();

            // Get today's attendance for all employees
            var today = DateTime.Today;
            var todayAttendance = await _context.Attendances
                .Where(a => a.Date.Date == today)
                .ToListAsync();

            // Create view model
            var viewModel = new EmployeeMonitoringViewModel
            {
                Employees = employees,
                TodayAttendance = todayAttendance,
                SelectedDate = today
            };

            return View(viewModel);
        }

        // GET: Monitoring/Logout
        public IActionResult Logout()
        {
            // Clear session - NO SignOutAsync since we're not using cookie auth
            HttpContext.Session.Clear();
            return RedirectToAction("AdminLogin");
        }

        // AJAX: Get attendance details for specific date
        [HttpGet]
        public async Task<IActionResult> GetAttendanceForDate(DateTime date)
        {
            var adminId = HttpContext.Session.GetInt32("AdminID");
            var isAdmin = HttpContext.Session.GetString("IsAdmin");

            if (adminId == null || isAdmin != "true")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                // First get all attendance for the date
                var attendance = await _context.Attendances
                    .Where(a => a.Date.Date == date.Date)
                    .ToListAsync();

                // Get all employee IDs from attendance
                var employeeIds = attendance.Select(a => a.Employee_ID).Distinct().ToList();

                // Get employees with their departments
                var employees = await _context.Employees
                    .Include(e => e.Department)
                    .Include(e => e.Shift)
                    .Where(e => employeeIds.Contains(e.Employee_ID))
                    .ToListAsync();

                // Combine the data
                var result = attendance.Select(a => new
                {
                    attendance = a,
                    employee = employees.FirstOrDefault(e => e.Employee_ID == a.Employee_ID)
                }).ToList();

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}