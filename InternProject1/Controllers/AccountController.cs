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
        // Search for the user in the Employees table
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email && u.Password == password);

        if (user != null)
        {
            // Login successful: Save details to Session
            HttpContext.Session.SetInt32("UserID", user.Employee_ID);
            HttpContext.Session.SetString("UserName", $"{user.First_Name} {user.Last_Name}");
            return RedirectToAction("Index", "Home");
        }

        // Login failed: Provide specific feedback
        ViewBag.ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
        return View();
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}