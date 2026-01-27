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
                    Shift_ID = e.Shift_ID
                })
                .FirstOrDefaultAsync();

            if (employee == null)
            {
                return RedirectToAction("Login", "Account");
            }

            return View(employee);
        }

        // NEW: Separate endpoint for handling cropped image upload
        [HttpPost]
        public async Task<IActionResult> UploadCroppedImage([FromForm] IFormFile croppedImage)
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return Json(new { success = false, message = "Session expired" });
            }

            if (croppedImage != null && croppedImage.Length > 0)
            {
                try
                {
                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profiles");
                    Directory.CreateDirectory(uploadsFolder);

                    // Delete old profile picture if exists
                    var employee = await _context.Employees.FindAsync(employeeId.Value);
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

                        // Update session
                        HttpContext.Session.SetString("ProfilePicture", profilePath);
                    }

                    return Json(new { success = true, path = profilePath });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

            return Json(new { success = false, message = "No image provided" });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(EmployeeProfileViewModel model)
        {
            var employeeId = HttpContext.Session.GetInt32("UserID");

            if (employeeId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Employee_ID == employeeId.Value);

            if (employee == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Update employee properties (profile picture is handled separately now)
            employee.First_Name = model.First_Name;
            employee.Last_Name = model.Last_Name;
            employee.Employee_Email = model.Employee_Email;
            employee.Employee_Phone = model.Employee_Phone;
            employee.Date_of_Birth = model.Date_of_Birth;
            employee.Gender = model.Gender;

            try
            {
                await _context.SaveChangesAsync();

                HttpContext.Session.SetString("UserName", $"{employee.First_Name} {employee.Last_Name}");

                TempData["SuccessMessage"] = "Profile updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to update profile. Please try again.";
            }

            return RedirectToAction("Index");
        }
    }
}