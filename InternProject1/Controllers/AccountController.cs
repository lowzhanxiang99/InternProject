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

    // --- REGISTRATION ---
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

    // --- LOGIN (Fixed for Case-Sensitivity) ---
    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        // 1. Find the user by email only first. 
        // SQL Server is usually case-insensitive for emails, which is standard.
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email);

        // 2. Perform a Case-Sensitive password check in C#
        // C#'s '==' operator will correctly distinguish 'Z' from 'z'
        if (user != null && user.Password == password)
        {
            // Login successful: Save details to Session
            HttpContext.Session.SetInt32("UserID", user.Employee_ID);
            HttpContext.Session.SetString("UserName", $"{user.First_Name} {user.Last_Name}");
            return RedirectToAction("Index", "Home");
        }

        // Login failed
        ViewBag.ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
        return View();
    }

    // --- FORGOT PASSWORD ---
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        var user = await _context.Employees.FirstOrDefaultAsync(e => e.Employee_Email == email);

        if (user != null)
        {
            string simulatedToken = Guid.NewGuid().ToString();
            string confirmationLink = Url.Action("ResetPassword", "Account",
                new { email = email, token = simulatedToken }, Request.Scheme);

            ViewBag.UserEmail = email;
            ViewBag.SimulatedLink = confirmationLink;

            return View("EmailSent");
        }

        ViewBag.Error = "Email address not found.";
        return View();
    }

    public IActionResult EmailSent() => View();

    // Reset Password GET: accessible via the simulated email link
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToAction("Login");
        }

        ViewBag.Email = email;
        return View();
    }

    // Update Password POST: strictly updates the record for the specific email
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

    // --- LOGOUT ---
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}