using Microsoft.AspNetCore.Mvc;
using InternProject1.Data;
// Using an alias to fix the 'Ambiguous Reference' error permanently
using MyClaim = InternProject1.Models.Claim;
using Microsoft.EntityFrameworkCore;
using InternProject1.Services;

namespace InternProject1.Controllers;

public class ClaimController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IEmailService _emailService;

    public ClaimController(ApplicationDbContext context, IWebHostEnvironment environment, IEmailService emailService)
    {
        _context = context;
        _environment = environment;
        _emailService = emailService;
    }

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

    public IActionResult Create() => View();

    [HttpPost]
    public async Task<IActionResult> Create(MyClaim claim, IFormFile? receiptFile)
    {
        int? userId = HttpContext.Session.GetInt32("UserID");
        if (userId == null) return RedirectToAction("Login", "Account");

        ModelState.Remove("Status");
        ModelState.Remove("CreatedAt");
        ModelState.Remove("Employee"); // Prevent validation errors on navigation property

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
            claim.Claim_Date = DateTime.Now; // Matches your model property

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
            .Include(c => c.Employee)
            .Where(c => c.Status == "Pending")
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return View(allPendingClaims);
    }

    [HttpPost]
    public async Task<IActionResult> ApproveOrReject(int claimId, string status)
    {
        if (HttpContext.Session.GetString("IsClaimAdmin") != "true") return Unauthorized();

        // Fetches claim and joins with Employee table
        var claim = await _context.Claims
            .Include(c => c.Employee)
            .FirstOrDefaultAsync(c => c.Claim_ID == claimId);

        if (claim != null)
        {
            claim.Status = status;
            await _context.SaveChangesAsync();

            // Email Logic using Employee_Email
            if (claim.Employee != null && !string.IsNullOrEmpty(claim.Employee.Employee_Email))
            {
                try
                {
                    string subject = $"Claim Request Update: {status}";
                    string body = $@"<h3>Claim Status Notification</h3>
                                     <p>Dear {claim.Employee.First_Name},</p>
                                     <p>Your claim for <b>{claim.Claim_Type}</b> in the amount of <b>{claim.Amount:C}</b> has been <b>{status}</b>.</p>
                                     <p>Status Date: {DateTime.Now:dd-MM-yyyy}</p>";

                    await _emailService.SendEmailAsync(claim.Employee.Employee_Email, subject, body);
                    TempData["SuccessMessage"] = $"Claim {status} and notification sent to {claim.Employee.Employee_Email}.";
                }
                catch (Exception)
                {
                    TempData["SuccessMessage"] = $"Claim {status}, but the email system encountered an error.";
                }
            }
            else
            {
                TempData["SuccessMessage"] = $"Claim {status} (No email sent: Recipient address missing).";
            }
        }
        return RedirectToAction("AdminApproval");
    }
}