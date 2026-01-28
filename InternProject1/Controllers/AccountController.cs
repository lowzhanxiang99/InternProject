using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace InternProject1.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AccountController(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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

    // --- LOGIN ---
    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        var captchaResponse = Request.Form["g-recaptcha-response"];

        if (string.IsNullOrEmpty(captchaResponse) || !(await IsHuman(captchaResponse)))
        {
            ViewBag.ErrorMessage = "Please verify that you are not a robot.";
            return View();
        }

        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email);

        if (user != null && user.Password == password)
        {
            HttpContext.Session.SetInt32("UserID", user.Employee_ID);
            HttpContext.Session.SetString("UserName", $"{user.First_Name} {user.Last_Name}");
            return RedirectToAction("Index", "Home");
        }

        ViewBag.ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
        return View();
    }

    // --- QR SCANNER QR VIEW ---
    public IActionResult MyCode()
    {
        return View();
    }

    // --- QR SCANNER VIEW ---
    // This action simply returns the Scan.cshtml view you created
    public IActionResult Scan()
    {
        return View();
    }

    // --- QR CODE AUTO-LOGIN & CLOCK-IN ---
    public async Task<IActionResult> AutoLogin(int empId, string secret)
    {
        // Security Check: Pulls secret from appsettings.json
        string validSecret = _configuration["QRCodeSettings:SecretPassphrase"] ?? "TimeVIA123";
        if (secret != validSecret) return Unauthorized();

        var user = await _context.Employees.FindAsync(empId);
        if (user == null) return NotFound();

        var today = DateTime.Now.Date;
        var existingAttendance = await _context.Attendances
            .FirstOrDefaultAsync(a => a.Employee_ID == empId && a.Date == today);

        if (existingAttendance == null)
        {
            var attendance = new Attendance
            {
                Employee_ID = empId,
                Date = today,
                ClockInTime = DateTime.Now.TimeOfDay,
                Status = (DateTime.Now.Hour < 9) ? "Present" : "Late"
            };

            _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Clock-in successful!";
        }
        else
        {
            TempData["SuccessMessage"] = "You have already clocked in for today.";
        }

        HttpContext.Session.SetInt32("UserID", user.Employee_ID);
        HttpContext.Session.SetString("UserName", $"{user.First_Name} {user.Last_Name}");

        return RedirectToAction("Index", "Home");
    }

    // --- RECAPTCHA VERIFICATION HELPER ---
    private async Task<bool> IsHuman(string token)
    {
        // Pulls Secret Key from appsettings.json
        string secretKey = _configuration["ReCaptcha:SecretKey"];

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
        if (string.IsNullOrEmpty(token)) return RedirectToAction("Login");
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