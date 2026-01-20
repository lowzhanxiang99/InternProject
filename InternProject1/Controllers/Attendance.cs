using Microsoft.AspNetCore.Mvc;
using InternProject1.Models;
using System.Collections.Generic;
using System.Linq;

namespace InternProject1.Controllers
{
    public class AttendanceController : Controller
    {
        // Temporary in-memory storage for testing
        private static List<Attendance> _attendanceList = new List<Attendance>();
        private static int _nextId = 1;

        public IActionResult Index()
        {
            // For testing, use Employee_ID = 1
            int employeeId = 1;

            var attendanceRecords = _attendanceList
                .Where(a => a.Employee_ID == employeeId)
                .OrderByDescending(a => a.Date)
                .Take(10)
                .ToList();

            return View(attendanceRecords);
        }

        [HttpPost]
        public IActionResult ClockIn(double latitude, double longitude)
        {
            int employeeId = 1;

            var todayAttendance = _attendanceList
                .FirstOrDefault(a => a.Employee_ID == employeeId && a.Date.Date == System.DateTime.Today);

            if (todayAttendance != null)
            {
                TempData["Error"] = "You have already clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            var attendance = new Attendance
            {
                Attendance_ID = _nextId++,
                Employee_ID = employeeId,
                Date = System.DateTime.Today,
                ClockInTime = System.DateTime.Now.TimeOfDay,
                Location_Lat_Long = $"{latitude},{longitude}",
                Status = System.DateTime.Now.TimeOfDay.Hours >= 9 ? "Late" : "Present"
            };

            _attendanceList.Add(attendance);
            TempData["Success"] = "Successfully clocked in!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult ClockOut(double latitude, double longitude)
        {
            int employeeId = 1;

            var todayAttendance = _attendanceList
                .FirstOrDefault(a => a.Employee_ID == employeeId && a.Date.Date == System.DateTime.Today);

            if (todayAttendance == null)
            {
                TempData["Error"] = "You haven't clocked in today!";
                return RedirectToAction(nameof(Index));
            }

            if (todayAttendance.ClockOutTime != null)
            {
                TempData["Error"] = "You have already clocked out today!";
                return RedirectToAction(nameof(Index));
            }

            todayAttendance.ClockOutTime = System.DateTime.Now.TimeOfDay;
            todayAttendance.Location_Lat_Long += $";{latitude},{longitude}";

            TempData["Success"] = "Successfully clocked out!";

            return RedirectToAction(nameof(Index));
        }

        // If you need other actions
        public IActionResult Report()
        {
            return View();
        }

        public IActionResult Monitor()
        {
            return View();
        }
    }
}