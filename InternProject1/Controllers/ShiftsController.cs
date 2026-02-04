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

            // Update employee shift
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

        // GET: Shifts/Edit/5
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

        // POST: Shifts/Edit/5
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

        // GET: Shifts/Delete/5
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

        // POST: Shifts/Delete/5
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