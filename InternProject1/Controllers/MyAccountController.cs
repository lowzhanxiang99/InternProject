using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InternProject1.Models;
using InternProject1.Data;

namespace InternProject1.Controllers
{
    public class MyAccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public MyAccountController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index()
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var employee = await _context.Employees
                .Include(e => e.Shift)
                .Include(e => e.Department)
                .Where(e => e.Employee_ID == employeeId.Value)
                .Select(e => new EmployeeProfileViewModel
                {
                    Employee_ID = e.Employee_ID,
                    First_Name = e.First_Name,
                    Last_Name = e.Last_Name,
                    Employee_Email = e.Employee_Email,
                    Employee_Phone = e.Employee_Phone,
                    Date_of_Birth = e.Date_of_Birth,
                    Gender = e.Gender,
                    Role = e.Role,
                    Branch = e.Branch,
                    ProfilePicturePath = e.ProfilePicturePath,
                    Department_ID = e.Department_ID,
                    Shift_ID = e.Shift_ID,
                    // Use the actual department name from database
                    DepartmentName = e.Department != null ? e.Department.Department_Name : "",
                    ShiftTime = e.Shift != null ? $"{e.Shift.Start_Time:hh\\:mm} - {e.Shift.End_Time:hh\\:mm}" : "N/A"
                })
                .FirstOrDefaultAsync();

            if (employee == null)
            {
                return RedirectToAction("Login", "Account");
            }

            return View(employee);
        }

        [HttpPost]
        public async Task<IActionResult> UploadCroppedImage([FromForm] IFormFile croppedImage)
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return Json(new { success = false, message = "Session expired" });
            }

            if (croppedImage == null || croppedImage.Length == 0)
            {
                return Json(new { success = false, message = "No image provided" });
            }

            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var extension = Path.GetExtension(croppedImage.FileName).ToLowerInvariant();

            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                return Json(new { success = false, message = "Invalid file type. Only JPG, PNG, and GIF are allowed." });
            }

            // Validate file size (max 5MB)
            if (croppedImage.Length > 5 * 1024 * 1024)
            {
                return Json(new { success = false, message = "File size exceeds 5MB limit" });
            }

            try
            {
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profiles");
                Directory.CreateDirectory(uploadsFolder);

                var employee = await _context.Employees.FindAsync(employeeId.Value);

                // Delete old profile picture if exists
                if (employee != null && !string.IsNullOrEmpty(employee.ProfilePicturePath))
                {
                    var oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, employee.ProfilePicturePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                var uniqueFileName = $"{employeeId}_{DateTime.Now.Ticks}.jpg";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await croppedImage.CopyToAsync(fileStream);
                }

                var profilePath = $"/uploads/profiles/{uniqueFileName}";

                if (employee != null)
                {
                    employee.ProfilePicturePath = profilePath;
                    await _context.SaveChangesAsync();
                    HttpContext.Session.SetString("ProfilePicture", profilePath);
                }

                return Json(new { success = true, path = profilePath });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Upload failed: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(EmployeeProfileViewModel model)
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Remove validation for non-editable fields
            ModelState.Remove("Role");
            ModelState.Remove("ProfilePicturePath");
            ModelState.Remove("Shift_ID");
            ModelState.Remove("ShiftTime");
            ModelState.Remove("Department_ID");

            // Custom age validation
            if (!model.IsValidAge())
            {
                ModelState.AddModelError("Date_of_Birth", "Age must be between 18 and 100 years");
            }

            // Check if date of birth is not in the future
            if (model.Date_of_Birth.Date > DateTime.Today.Date)
            {
                ModelState.AddModelError("Date_of_Birth", "Date of birth cannot be in the future");
            }

            if (!ModelState.IsValid)
            {
                // Reload employee data for display
                var currentEmployee = await _context.Employees
                    .Include(e => e.Shift)
                    .Include(e => e.Department)
                    .Where(e => e.Employee_ID == employeeId.Value)
                    .FirstOrDefaultAsync();

                if (currentEmployee != null)
                {
                    model.Employee_ID = currentEmployee.Employee_ID;
                    model.Role = currentEmployee.Role;
                    model.ProfilePicturePath = currentEmployee.ProfilePicturePath;
                    model.Shift_ID = currentEmployee.Shift_ID;
                    model.Department_ID = currentEmployee.Department_ID;
                    model.ShiftTime = currentEmployee.Shift != null ?
                        $"{currentEmployee.Shift.Start_Time:hh\\:mm} - {currentEmployee.Shift.End_Time:hh\\:mm}" : "N/A";
                }

                TempData["ErrorMessage"] = "Please correct the errors and try again.";
                return View("Index", model);
            }

            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.Employee_ID == employeeId.Value);

            if (employee == null)
            {
                TempData["ErrorMessage"] = "Employee not found.";
                return RedirectToAction("Login", "Account");
            }

            // Check if email is already used by another employee
            var emailExists = await _context.Employees
                .AnyAsync(e => e.Employee_Email == model.Employee_Email && e.Employee_ID != employeeId.Value);

            if (emailExists)
            {
                ModelState.AddModelError("Employee_Email", "This email is already in use by another employee");
                model.Role = employee.Role;
                model.ProfilePicturePath = employee.ProfilePicturePath;
                TempData["ErrorMessage"] = "Email is already in use.";
                return View("Index", model);
            }

            // Update employee properties
            employee.First_Name = model.First_Name.Trim();
            employee.Last_Name = model.Last_Name.Trim();
            employee.Employee_Email = model.Employee_Email.Trim().ToLower();
            employee.Employee_Phone = model.Employee_Phone.Trim();
            employee.Date_of_Birth = model.Date_of_Birth;
            employee.Gender = model.Gender;
            employee.Branch = model.Branch.Trim();

            // Handle Department - check if it exists or create new
            // In your UpdateProfile method:
            var departmentName = model.DepartmentName.Trim();

            if (string.IsNullOrEmpty(departmentName))
            {
                employee.Department_ID = null;
            }
            else
            {
                // Check if department exists in database
                var existingDepartment = await _context.Departments
                    .FirstOrDefaultAsync(d => d.Department_Name.ToLower() == departmentName.ToLower());

                if (existingDepartment != null)
                {
                    // Use existing department
                    employee.Department_ID = existingDepartment.Department_ID;
                }
                else
                {
                    // FIRST: Find or create a manager
                    var manager = await _context.Managers.FirstOrDefaultAsync();

                    if (manager == null)
                    {
                        // Create a default manager
                        manager = new Manager
                        {
                            Manager_Name = "System Manager"
                        };
                        _context.Managers.Add(manager);
                        await _context.SaveChangesAsync();
                    }

                    // NOW create department with the manager ID
                    var newDepartment = new Department
                    {
                        Department_Name = departmentName,
                        Manager_ID = manager.Manager_ID  // <-- MUST set this value
                    };

                    _context.Departments.Add(newDepartment);
                    await _context.SaveChangesAsync();

                    employee.Department_ID = newDepartment.Department_ID;
                }
            }

            try
            {
                await _context.SaveChangesAsync();

                HttpContext.Session.SetString("UserName", $"{employee.First_Name} {employee.Last_Name}");

                TempData["SuccessMessage"] = "Profile updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to update profile. Error: {ex.Message}";
                model.Role = employee.Role;
                model.ProfilePicturePath = employee.ProfilePicturePath;
                return View("Index", model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return Json(new { success = false, message = "Session expired" });
            }

            var employee = await _context.Employees.FindAsync(employeeId.Value);

            if (employee == null)
            {
                return Json(new { success = false, message = "Employee not found" });
            }

            // Verify current password
            if (employee.Password != request.CurrentPassword)
            {
                return Json(new { success = false, message = "Current password is incorrect" });
            }

            // Validate new password
            if (request.NewPassword != request.ConfirmPassword)
            {
                return Json(new { success = false, message = "Passwords do not match" });
            }

            if (request.NewPassword.Length < 6)
            {
                return Json(new { success = false, message = "Password must be at least 6 characters" });
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return Json(new { success = false, message = "New password must be different from current password" });
            }

            // Update password
            employee.Password = request.NewPassword;

            try
            {
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Password changed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to change password" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return Json(new { success = false, message = "Session expired" });
            }

            try
            {
                var employee = await _context.Employees.FindAsync(employeeId.Value);

                if (employee == null)
                {
                    return Json(new { success = false, message = "Employee not found" });
                }

                // Delete the physical file if it exists
                if (!string.IsNullOrEmpty(employee.ProfilePicturePath))
                {
                    var filePath = Path.Combine(_webHostEnvironment.WebRootPath,
                        employee.ProfilePicturePath.TrimStart('/'));

                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // Clear the database field
                employee.ProfilePicturePath = null;
                await _context.SaveChangesAsync();

                // Clear session
                HttpContext.Session.Remove("ProfilePicture");

                return Json(new { success = true, message = "Profile picture removed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error removing picture: " + ex.Message });
            }
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
            public string ConfirmPassword { get; set; }
        }
    }
}