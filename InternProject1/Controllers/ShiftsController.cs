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

        // GET: Shifts/Index (Dashboard)
        public async Task<IActionResult> Index()
        {
            ViewBag.TotalShifts = await _context.Shifts.CountAsync();
            ViewBag.AssignedEmployees = await _context.Employees.CountAsync(e => e.Shift_ID != null);
            ViewBag.UnassignedCount = await _context.Employees.CountAsync(e => e.Shift_ID == null);
            ViewBag.UsingDefaultCount = await _context.Employees.CountAsync(e => e.UsingDefaultShift);

            return View();
        }

        // GET: Shifts/Manage (CRUD for shifts)
        public async Task<IActionResult> Manage()
        {
            var shifts = await _context.Shifts.ToListAsync();
            return View(shifts);
        }

        // GET: Shifts/Assign (Assign shifts to employees)
        public async Task<IActionResult> Assign()
        {
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
        public async Task<IActionResult> AssignShiftToEmployee(int employeeId, int shiftId)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null)
                return Json(new { success = false, message = "Employee not found" });

            var shift = await _context.Shifts.FindAsync(shiftId);
            if (shift == null && shiftId != 0)
                return Json(new { success = false, message = "Shift not found" });

            // Update with tracking
            employee.Shift_ID = shiftId == 0 ? null : shiftId;
            employee.UsingDefaultShift = false;
            employee.ShiftAssignedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            string employeeName = $"{employee.First_Name} {employee.Last_Name}";
            string shiftName = shiftId == 0 ? "No Shift" : shift.Shift_Name;

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
            return View();
        }

        // POST: Shifts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Shift shift)
        {
            if (ModelState.IsValid)
            {
                _context.Add(shift);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Manage));  // Fixed
            }
            return View(shift);
        }

        // GET: Shifts/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
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
            if (id != shift.Shift_ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
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
                return RedirectToAction(nameof(Manage));  // Fixed
            }
            return View(shift);
        }

        // GET: Shifts/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
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
    }
}