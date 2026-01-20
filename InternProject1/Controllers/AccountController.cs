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

    // GET: Account/Register
    public IActionResult Register() => View();

    // POST: Account/Register
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

    // GET: Account/Login
    public IActionResult Login() => View();

    // POST: Account/Login
    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Employee_Email == email && u.Password == password);

        if (user != null)
        {
            // For now, just redirect to Home. In a real app, you'd set a Cookie here.
            return RedirectToAction("Index", "Home");
        }

        ViewBag.Error = "Invalid email or password";
        return View();
    }
}