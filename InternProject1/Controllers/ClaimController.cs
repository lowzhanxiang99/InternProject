using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
using InternProject1.Models;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Controllers;

public class ClaimController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public ClaimController(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    // List of claims for the logged-in user
    public async Task<IActionResult> Index()
    {
        int? userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        var claims = await _context.Claims
            .Where(c => c.Employee_ID == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return View(claims);
    }

    // Show the Application Form
    public IActionResult Create() => View();

    // Process the Application Form
    [HttpPost]
    public async Task<IActionResult> Create(Claim claim, IFormFile? receiptFile)
    {
        int? userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        if (ModelState.IsValid)
        {
            if (receiptFile != null && receiptFile.Length > 0)
            {
                string folder = Path.Combine(_environment.WebRootPath, "uploads/receipts");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string fileName = Guid.NewGuid().ToString() + "_" + receiptFile.FileName;
                string filePath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await receiptFile.CopyToAsync(stream);
                }
                claim.ReceiptPath = "/uploads/receipts/" + fileName;
            }

            claim.Employee_ID = userId.Value;
            claim.Status = "Pending";
            claim.CreatedAt = DateTime.Now;

            _context.Claims.Add(claim);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Claim submitted successfully!";
            return RedirectToAction("Index");
        }
        return View(claim);
    }

    // --- ADMIN SECTION ---

    public IActionResult AdminLogin() => View();

    [HttpPost]
    public IActionResult AdminLogin(string email, string password)
    {
        if (email == "admin@gmail.com" && password == "admin123")
        {
            HttpContext.Session.SetString("IsClaimAdmin", "true");
            return RedirectToAction("AdminApproval");
        }

        ViewBag.Error = "Invalid Admin Credentials";
        return View();
    }

    // Log out specifically from Admin mode
    public IActionResult AdminLogout()
    {
        HttpContext.Session.Remove("IsClaimAdmin");
        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> AdminApproval()
    {
        if (HttpContext.Session.GetString("IsClaimAdmin") != "true")
        {
            return RedirectToAction("AdminLogin");
        }

        var allPendingClaims = await _context.Claims
            .Include(c => c.Employee) // Ensure 'public Employee Employee { get; set; }' exists in Claim model
            .Where(c => c.Status == "Pending")
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return View(allPendingClaims);
    }

    [HttpPost]
    public async Task<IActionResult> ApproveOrReject(int claimId, string status)
    {
        if (HttpContext.Session.GetString("IsClaimAdmin") != "true") return Unauthorized();

        var claim = await _context.Claims.FindAsync(claimId);
        if (claim != null)
        {
            claim.Status = status;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Claim has been {status}.";
        }
        return RedirectToAction("AdminApproval");
    }
}