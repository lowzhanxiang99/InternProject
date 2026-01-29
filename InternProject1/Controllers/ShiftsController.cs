// In Controllers/ShiftsController.cs
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

        // GET: Shifts
        public async Task<IActionResult> Index()
        {
            ViewBag.TotalShifts = await _context.Shifts.CountAsync();
            ViewBag.AssignedEmployees = await _context.Employees.CountAsync(e => e.Shift_ID != null);
            ViewBag.UnassignedCount = await _context.Employees.CountAsync(e => e.Shift_ID == null);

            return View();
        }

        // Manage)
        public async Task<IActionResult> Manage()
        {
            var shifts = await _context.Shifts.ToListAsync();
            return View(shifts);
        }

        // Assign shifts to employees
        public async Task<IActionResult> Assign()
        {
            // Get employees without shifts
            var unassignedEmployees = await _context.Employees
                .Where(e => e.Shift_ID == null)
                .Include(e => e.Department)
                .ToListAsync();

            // Get all employees for bulk assignment
            var allEmployees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Shift)
                .OrderBy(e => e.Department != null ? e.Department.Department_Name : "ZZZ")
                .ThenBy(e => e.First_Name)
                .ToListAsync();

            // Get all shifts
            var shifts = await _context.Shifts.ToListAsync();

            var viewModel = new AssignShiftsViewModel
            {
                UnassignedEmployees = unassignedEmployees,
                AllEmployees = allEmployees,
                Shifts = shifts
            };

            return View(viewModel);
        }

        // API to assign shift
        [HttpPost]
        public async Task<IActionResult> AssignShiftToEmployee(int employeeId, int shiftId)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null)
                return Json(new { success = false, message = "Employee not found" });

            var shift = await _context.Shifts.FindAsync(shiftId);
            if (shift == null)
                return Json(new { success = false, message = "Shift not found" });

            employee.Shift_ID = shiftId;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Shift assigned: {employee.FullName} → {shift.Shift_Name}"
            });
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
                return RedirectToAction(nameof(Index));
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
                return RedirectToAction(nameof(Index));
            }
            return View(shift);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shift = await _context.Shifts
                .Include(s => s.Employees)  // Check if employees use this shift
                .FirstOrDefaultAsync(m => m.Shift_ID == id);

            if (shift == null)
            {
                return NotFound();
            }

            // Check if any employees are assigned to this shift
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

            if (shift == null)
            {
                return NotFound();
            }

            // Prevent deletion if employees are assigned
            if (shift.Employees != null && shift.Employees.Any())
            {
                TempData["Error"] = $"Cannot delete '{shift.Shift_Name}' because {shift.Employees.Count} employee(s) are assigned to it.";
                return RedirectToAction(nameof(Index));
            }

            // Prevent deletion if this is the default shift
            if (shift.Is_Default)
            {
                TempData["Error"] = $"Cannot delete '{shift.Shift_Name}' because it is set as the default shift.";
                return RedirectToAction(nameof(Index));
            }

            _context.Shifts.Remove(shift);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Shift '{shift.Shift_Name}' deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        private bool ShiftExists(int id)
        {
            return _context.Shifts.Any(e => e.Shift_ID == id);
        }
    }
}