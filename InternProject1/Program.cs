using Microsoft.EntityFrameworkCore;
using InternProject1.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container
builder.Services.AddControllersWithViews();

// Database Configuration
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- ADDED FOR SESSION SUPPORT ---
// This allows the app to store UserID and UserName after login
builder.Services.AddDistributedMemoryCache(); // Required for Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session lasts for 30 minutes
    options.Cookie.HttpOnly = true; // Security: prevents client-side script access
    options.Cookie.IsEssential = true; // Essential for the app to function
});
// ----------------------------------

var app = builder.Build();

// 2. Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// --- ADDED FOR SESSION SUPPORT ---
// This MUST be placed after UseRouting and BEFORE UseAuthorization
app.UseSession();
// ----------------------------------

app.UseAuthorization();



// 3. Set the default route (Login page)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();