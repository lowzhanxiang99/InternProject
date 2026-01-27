using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Net.Http;

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

    // --- LOGIN (Modified with reCAPTCHA & Case-Sensitivity) ---
    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        // 1. Capture the reCAPTCHA response from the form
        var captchaResponse = Request.Form["g-recaptcha-response"];

        // 2. Perform Human Verification check
        if (string.IsNullOrEmpty(captchaResponse) || !(await IsHuman(captchaResponse)))
        {
            ViewBag.ErrorMessage = "Please verify that you are not a robot.";
            return View();
        }

        // 3. Find the user by email
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email);

        // 4. Perform Case-Sensitive password check
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

    // --- RECAPTCHA VERIFICATION HELPER ---
    private async Task<bool> IsHuman(string token)
    {
        // Replace with your actual Secret Key from Google Admin Console
        // Remember to use 'localhost' as the domain in Google settings for local testing
        string secretKey = "6Lc_qFYsAAAAADkTqIKciPW0MMAdZmBF1sOHjd1k";

        using var client = new HttpClient();
        var response = await client.PostAsync($"https://www.google.com/recaptcha/api/siteverify?secret={secretKey}&response={token}", null);

        if (response.IsSuccessStatusCode)
        {
            var jsonString = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonString);
            return result.success;
        }
        return false;
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

    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToAction("Login");
        }

        ViewBag.Email = email;
        return View();
    }

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