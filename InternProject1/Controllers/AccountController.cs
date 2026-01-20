using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }

    // --- REGISTRATION & LOGIN (KEEPING YOUR EXISTING LOGIC) ---
    public IActionResult Register() => View();

    [HttpPost]
    public async Task<IActionResult> Register(Employee employee)
    {
        if (ModelState.IsValid)
        {
            _context.Add(employee);
            await _context.SaveChangesAsync();
            return RedirectToAction("Login");
        }
        return View(employee);
    }

    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email && u.Password == password);

        if (user != null)
        {
            HttpContext.Session.SetInt32("UserID", user.Employee_ID);
            HttpContext.Session.SetString("UserName", $"{user.First_Name} {user.Last_Name}");
            return RedirectToAction("Index", "Home");
        }

        ViewBag.ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
        return View();
    }

    // --- FORGOT PASSWORD (MODIFIED FOR EMAIL CONFIRMATION) ---

    public IActionResult ForgotPassword() => View();

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        var user = await _context.Employees.FirstOrDefaultAsync(e => e.Employee_Email == email);

        if (user != null)
        {
            // 1. Generate a unique token for the simulation
            string simulatedToken = Guid.NewGuid().ToString();

            // 2. Create the link that would normally be sent to an email
            string confirmationLink = Url.Action("ResetPassword", "Account",
                new { email = email, token = simulatedToken }, Request.Scheme);

            // 3. Store info in ViewBag to show on the 'EmailSent' simulation page
            ViewBag.UserEmail = email;
            ViewBag.SimulatedLink = confirmationLink;

            return View("EmailSent");
        }

        ViewBag.Error = "Email address not found.";
        return View();
    }

    // This view will show up after clicking 'Send Reset Link'
    public IActionResult EmailSent() => View();

    // 3. Display the Reset Password page - only accessible via the "Email Link"
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToAction("Login");
        }

        ViewBag.Email = email;
        return View();
    }

    // 4. Update the password in the database
    [HttpPost]
    public async Task<IActionResult> UpdatePassword(string email, string newPassword)
    {
        var user = await _context.Employees.FirstOrDefaultAsync(e => e.Employee_Email == email);

        if (user != null)
        {
            user.Password = newPassword;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password updated successfully. Please login.";
            return RedirectToAction("Login");
        }

        return View("Error");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}