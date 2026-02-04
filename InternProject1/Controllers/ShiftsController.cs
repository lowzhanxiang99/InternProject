using InternProject1.Data;
using InternProject1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Controllers
{
    public class ShiftsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShiftsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Shifts/AdminLogin
        public IActionResult AdminLogin()
        {
            var isAdmin = HttpContext.Session.GetString("IsShiftsAdmin");

            if (isAdmin == "true")
                return RedirectToAction("Index");

            return View();
        }

        // POST: Shifts/AdminLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdminLogin(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Email and password are required.");
                return View();
            }

            if (email == "admin@gmail.com" && password == "admin123")
            {
                HttpContext.Session.SetString("IsShiftsAdmin", "true");
                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Invalid admin credentials or insufficient permissions.");
            return View();
        }

        // GET: Shifts/Index (Dashboard)
        public async Task<IActionResult> Index()
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            ViewBag.TotalShifts = await _context.Shifts.CountAsync();
            ViewBag.AssignedEmployees = await _context.Employees.CountAsync(e => e.Shift_ID != null);
            ViewBag.UnassignedCount = await _context.Employees.CountAsync(e => e.Shift_ID == null);
            ViewBag.UsingDefaultCount = await _context.Employees.CountAsync(e => e.UsingDefaultShift);

            return View();
        }

        // GET: Shifts/Manage (CRUD for shifts)
        public async Task<IActionResult> Manage()
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            var shifts = await _context.Shifts.ToListAsync();
            var scheduleCounts = await _context.ShiftSchedules
                .Where(s => s.Is_Active)
                .GroupBy(s => s.Shift_ID)
                .Select(g => new { ShiftId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ShiftId, x => x.Count);

            ViewBag.ScheduleCounts = scheduleCounts;
            return View(shifts);
        }

        // GET: Shifts/Assign (Assign shifts to employees)
        public async Task<IActionResult> Assign()
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            var unassignedEmployees = await _context.Employees
                .Where(e => e.Shift_ID == null)
                .Include(e => e.Department)
                .ToListAsync();

            var allEmployees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Shift)
                .OrderBy(e => e.Department != null ? e.Department.Department_Name : "ZZZ")
                .ThenBy(e => e.First_Name)
                .ToListAsync();

            var shifts = await _context.Shifts.ToListAsync();

            var viewModel = new AssignShiftsViewModel
            {
                UnassignedEmployees = unassignedEmployees,
                AllEmployees = allEmployees,
                Shifts = shifts
            };

            return View(viewModel);
        }

        // POST: Shifts/AssignShiftToEmployee
        [HttpPost]
        public async Task<IActionResult> AssignShiftToEmployee([FromBody] AssignShiftRequest request)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            var employee = await _context.Employees.FindAsync(request.EmployeeId);
            if (employee == null)
                return Json(new { success = false, message = "Employee not found" });

            // Handle "No Shift" (clear shift)
            if (request.ShiftId == 0)
            {
                employee.Shift_ID = null;
                employee.UsingDefaultShift = false;
                employee.ShiftAssignedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Shift removed: {employee.First_Name} {employee.Last_Name}"
                });
            }

            var shift = await _context.Shifts.FindAsync(request.ShiftId);
            if (shift == null)
                return Json(new { success = false, message = "Shift not found" });

            employee.Shift_ID = request.ShiftId;
            employee.UsingDefaultShift = false;
            employee.ShiftAssignedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            string employeeName = $"{employee.First_Name} {employee.Last_Name}";
            string shiftName = shift.Shift_Name;

            return Json(new
            {
                success = true,
                message = $"Shift updated: {employeeName} → {shiftName}"
            });
        }

        // POST: Shifts/SetAsDefault
        [HttpPost]
        public async Task<IActionResult> SetAsDefault(int shiftId)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            var shift = await _context.Shifts.FindAsync(shiftId);
            if (shift == null)
            {
                TempData["Error"] = "Shift not found";
                return RedirectToAction(nameof(Manage));  // Fixed
            }

            // Remove default from others
            var currentDefault = await _context.Shifts
                .Where(s => s.Is_Default && s.Shift_ID != shiftId)
                .ToListAsync();

            foreach (var s in currentDefault)
            {
                s.Is_Default = false;
                _context.Update(s);
            }

            // Set new default
            shift.Is_Default = true;
            _context.Update(shift);

            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{shift.Shift_Name}' is now the default shift";
            return RedirectToAction(nameof(Manage));  // Fixed
        }

        // GET: Shifts/Create
        public IActionResult Create()
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            return View();
        }

        // POST: Shifts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Shift shift)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            if (ModelState.IsValid)
            {
                // If setting as default, remove default from others
                if (shift.Is_Default)
                {
                    var currentDefault = await _context.Shifts
                        .FirstOrDefaultAsync(s => s.Is_Default);

                    if (currentDefault != null)
                    {
                        currentDefault.Is_Default = false;
                        _context.Update(currentDefault);
                    }
                }

                _context.Add(shift);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Manage));
            }
            return View(shift);
        }

        // GET: Shifts/Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            if (id == null) return NotFound();

            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null) return NotFound();

            return View(shift);
        }

        // POST: Shifts/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Shift shift)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            if (id != shift.Shift_ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // If setting as default, remove default from others
                    if (shift.Is_Default)
                    {
                        var currentDefault = await _context.Shifts
                            .Where(s => s.Shift_ID != id)  // Exclude current shift
                            .FirstOrDefaultAsync(s => s.Is_Default);

                        if (currentDefault != null)
                        {
                            currentDefault.Is_Default = false;
                            _context.Update(currentDefault);
                        }
                    }

                    _context.Update(shift);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ShiftExists(shift.Shift_ID))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Manage));
            }
            return View(shift);
        }

        // GET: Shifts/Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            if (id == null) return NotFound();

            var shift = await _context.Shifts
                .Include(s => s.Employees)
                .FirstOrDefaultAsync(m => m.Shift_ID == id);

            if (shift == null) return NotFound();

            if (shift.Employees != null && shift.Employees.Any())
            {
                ViewBag.ErrorMessage = $"Cannot delete this shift because {shift.Employees.Count} employee(s) are assigned to it.";
                ViewBag.Employees = shift.Employees.ToList();
            }

            return View(shift);
        }

        // POST: Shifts/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
            {
                return RedirectToAction("AdminLogin");
            }

            var shift = await _context.Shifts
                .Include(s => s.Employees)
                .FirstOrDefaultAsync(s => s.Shift_ID == id);

            if (shift == null) return NotFound();

            // Prevent deletion if employees are assigned
            if (shift.Employees != null && shift.Employees.Any())
            {
                TempData["Error"] = $"Cannot delete '{shift.Shift_Name}' because {shift.Employees.Count} employee(s) are assigned to it.";
                return RedirectToAction(nameof(Manage));  // Fixed
            }

            // Prevent deletion if this is the default shift
            if (shift.Is_Default)
            {
                TempData["Error"] = $"Cannot delete '{shift.Shift_Name}' because it is set as the default shift.";
                return RedirectToAction(nameof(Manage));  // Fixed
            }

            _context.Shifts.Remove(shift);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Shift '{shift.Shift_Name}' deleted successfully.";
            return RedirectToAction(nameof(Manage));  // Fixed
        }

        // GET: Shifts/ManageSchedules
        public async Task<IActionResult> ManageSchedules(int shiftId)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
                return RedirectToAction("AdminLogin");

            var shift = await _context.Shifts
                .Include(s => s.Schedules)
                .FirstOrDefaultAsync(s => s.Shift_ID == shiftId);

            if (shift == null) return NotFound();

            ViewBag.ShiftId = shiftId;
            ViewBag.ShiftName = shift.Shift_Name;

            // Order schedules: specific dates first, then weekly
            var schedules = shift.Schedules?
                .OrderBy(s => s.SpecificDate.HasValue ? 0 : 1) // Specific dates first
                .ThenBy(s => s.DayOfWeek)                     // Then by day of week
                .ThenBy(s => s.SpecificDate)                  // Then by date
                .ToList() ?? new List<ShiftSchedule>();

            return View(schedules);
        }

        // POST: Shifts/ToggleScheduleStatus
        [HttpPost]
        public async Task<IActionResult> ToggleScheduleStatus(int scheduleId, int shiftId)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
                return RedirectToAction("AdminLogin");

            var schedule = await _context.ShiftSchedules.FindAsync(scheduleId);
            if (schedule == null)
            {
                TempData["Error"] = "Schedule not found";
                return RedirectToAction("ManageSchedules", new { shiftId });
            }

            schedule.Is_Active = !schedule.Is_Active;
            _context.Update(schedule);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Schedule {(schedule.Is_Active ? "activated" : "deactivated")}";
            return RedirectToAction("ManageSchedules", new { shiftId });
        }

        // GET: Shifts/CreateSchedule
        public async Task<IActionResult> CreateSchedule(int shiftId)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
                return RedirectToAction("AdminLogin");

            var shift = await _context.Shifts.FindAsync(shiftId);
            if (shift == null) return NotFound();

            ViewBag.ShiftId = shiftId;
            ViewBag.ShiftName = shift.Shift_Name;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSchedule(int shiftId, ShiftSchedule schedule, string scheduleMode)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
                return RedirectToAction("AdminLogin");

            if (scheduleMode == "specific" && !schedule.SpecificDate.HasValue)
            {
                ModelState.AddModelError("SpecificDate", "Date is required for specific date schedule");
            }
            else if (scheduleMode == "specific" && schedule.SpecificDate.HasValue)
            {
                if (schedule.SpecificDate.Value.Date < DateTime.Today)
                {
                    ModelState.AddModelError("SpecificDate", "Cannot set schedule for past dates");
                }
            }
            else if (scheduleMode == "weekly" && !schedule.DayOfWeek.HasValue)
            {
                ModelState.AddModelError("DayOfWeek", "Day of week is required for weekly schedule");
            }
            else if (string.IsNullOrEmpty(scheduleMode))
            {
                ModelState.AddModelError("", "Please select a schedule type");
            }

            if (schedule.StartDate.HasValue && schedule.EndDate.HasValue)
            {
                if (schedule.StartDate.Value.Date > schedule.EndDate.Value.Date)
                {
                    ModelState.AddModelError("EndDate", "End date must be after start date");
                }
                if (schedule.StartDate.Value.Date < DateTime.Today)
                {
                    ModelState.AddModelError("StartDate", "Cannot start schedule in the past");
                }
            }
            else if (schedule.StartDate.HasValue && !schedule.EndDate.HasValue)
            {
                if (schedule.StartDate.Value.Date < DateTime.Today)
                {
                    ModelState.AddModelError("StartDate", "Cannot start schedule in the past");
                }
            }
            else if (!schedule.StartDate.HasValue && schedule.EndDate.HasValue)
            {
                if (schedule.EndDate.Value.Date < DateTime.Today)
                {
                    ModelState.AddModelError("EndDate", "Cannot end schedule in the past");
                }
            }

            if (scheduleMode == "specific" && schedule.SpecificDate.HasValue && ModelState.IsValid)
            {
                // Check for duplicate specific date
                bool hasDuplicate = await _context.ShiftSchedules
                    .AnyAsync(s => s.Shift_ID == shiftId &&
                                  s.SpecificDate.HasValue &&
                                  s.SpecificDate.Value.Date == schedule.SpecificDate.Value.Date &&
                                  s.Is_Active);

                if (hasDuplicate)
                {
                    var existingSchedule = await _context.ShiftSchedules
                        .FirstOrDefaultAsync(s => s.Shift_ID == shiftId &&
                                                 s.SpecificDate.HasValue &&
                                                 s.SpecificDate.Value.Date == schedule.SpecificDate.Value.Date &&
                                                 s.Is_Active);
                    if (existingSchedule != null)
                    {
                        ModelState.AddModelError("SpecificDate",
                            $"'{schedule.SpecificDate.Value:ddd, MMM dd, yyyy}' already has an active schedule: " +
                            $"{existingSchedule.Start_Time:hh\\:mm} - {existingSchedule.End_Time:hh\\:mm}" +
                            $"{(string.IsNullOrEmpty(existingSchedule.Description) ? "" : $" ({existingSchedule.Description})")}");
                    }
                    else
                    {
                        ModelState.AddModelError("SpecificDate",
                            "This date already has an active schedule.");
                    }
                }
            }
            else if (scheduleMode == "weekly" && schedule.DayOfWeek.HasValue && ModelState.IsValid)
            {
                var existingSchedules = await _context.ShiftSchedules
                    .Where(s => s.Shift_ID == shiftId &&
                               s.DayOfWeek.HasValue &&
                               s.DayOfWeek.Value == schedule.DayOfWeek.Value &&
                               s.Is_Active)
                    .ToListAsync();
                bool hasOverlap = existingSchedules.Any(s =>
                    DateRangesOverlap(
                        s.StartDate,
                        s.EndDate,
                        schedule.StartDate,
                        schedule.EndDate
                    ));

                if (hasOverlap)
                {
                    var conflictingSchedule = existingSchedules.FirstOrDefault(s =>
                        DateRangesOverlap(
                            s.StartDate,
                            s.EndDate,
                            schedule.StartDate,
                            schedule.EndDate
                        ));
                    string dateRangeInfo = "";
                    if (conflictingSchedule != null)
                    {
                        if (conflictingSchedule.StartDate.HasValue && conflictingSchedule.EndDate.HasValue)
                        {
                            dateRangeInfo = $" (from {conflictingSchedule.StartDate.Value:MMM dd} to {conflictingSchedule.EndDate.Value:MMM dd})";
                        }
                        else if (conflictingSchedule.StartDate.HasValue)
                        {
                            dateRangeInfo = $" (starting {conflictingSchedule.StartDate.Value:MMM dd})";
                        }
                        else if (conflictingSchedule.EndDate.HasValue)
                        {
                            dateRangeInfo = $" (until {conflictingSchedule.EndDate.Value:MMM dd})";
                        }
                    }
                    ModelState.AddModelError("DayOfWeek",
                        $"There is already an active {schedule.DayOfWeek.Value.ToString()} schedule{dateRangeInfo}. " +
                        "Please edit or deactivate the existing one.");
                }
            }

            // Validate time
            if (schedule.Start_Time >= schedule.End_Time)
            {
                ModelState.AddModelError("End_Time", "End time must be after start time");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    schedule.Shift_ID = shiftId;
                    schedule.Is_Active = true;
                    schedule.Is_HalfDay = schedule.Is_HalfDay; // Keep user's choice

                    if (string.IsNullOrEmpty(schedule.Description))
                        schedule.Description = "No description";

                    if (string.IsNullOrEmpty(schedule.ScheduleType))
                        schedule.ScheduleType = "Custom";

                    if (scheduleMode == "specific")
                    {
                        schedule.DayOfWeek = null;
                        schedule.StartDate = null;
                        schedule.EndDate = null;
                    }
                    else if (scheduleMode == "weekly")
                    {
                        schedule.SpecificDate = null;
                    }

                    schedule.Shift = null;

                    _context.ShiftSchedules.Add(schedule);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Schedule added successfully";
                    return RedirectToAction("ManageSchedules", new { shiftId });
                }
                catch (Exception ex)
                {
                    string errorMessage = ex.Message;
                    Exception inner = ex.InnerException;

                    while (inner != null)
                    {
                        errorMessage += $" -> {inner.Message}";
                        inner = inner.InnerException;
                    }

                    ModelState.AddModelError("", $"Error: {errorMessage}");
                }
            }

            ViewBag.ShiftId = shiftId;
            ViewBag.ShiftName = (await _context.Shifts.FindAsync(shiftId))?.Shift_Name;
            return View(schedule);
        }

        private bool DateRangesOverlap(DateTime? start1, DateTime? end1, DateTime? start2, DateTime? end2)
        {
            var s1 = start1 ?? DateTime.MinValue;
            var e1 = end1 ?? DateTime.MaxValue;
            var s2 = start2 ?? DateTime.MinValue;
            var e2 = end2 ?? DateTime.MaxValue;

            return s1 <= e2 && e1 >= s2;
        }

        // POST: Shifts/DeleteSchedule
        [HttpPost]
        public async Task<IActionResult> DeleteSchedule(int scheduleId, int shiftId)
        {
            if (HttpContext.Session.GetString("IsShiftsAdmin") != "true")
                return RedirectToAction("AdminLogin");

            var schedule = await _context.ShiftSchedules.FindAsync(scheduleId);
            if (schedule == null)
            {
                TempData["Error"] = "Schedule not found";
                return RedirectToAction("ManageSchedules", new { shiftId });
            }

            _context.ShiftSchedules.Remove(schedule);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Schedule deleted successfully";
            return RedirectToAction("ManageSchedules", new { shiftId });
        }

        private bool ShiftExists(int id)
        {
            return _context.Shifts.Any(e => e.Shift_ID == id);
        }

        [HttpPost]
        public IActionResult ClearSession()
        {
            HttpContext.Session.Remove("IsShiftsAdmin");
            return Ok();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Remove("IsShiftsAdmin");
            return RedirectToAction("AdminLogin");
        }
    }

    public class AssignShiftRequest
    {
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
    }

    public class AssignShiftRequest
    {
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
    }
}