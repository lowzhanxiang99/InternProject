using Microsoft.AspNetCore.Mvc;
using InternProject1.Models;

namespace InternProject1.Controllers;

public class HomeController : Controller
{
    // This is the main Dashboard page
    public IActionResult Index()
    {
        // Later, we will add code here to check if the user is logged in.
        // For now, it just shows the Welcome page.
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }
}