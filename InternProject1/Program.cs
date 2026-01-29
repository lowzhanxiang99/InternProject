using Microsoft.EntityFrameworkCore;
using InternProject1.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container
builder.Services.AddControllersWithViews();

// Database Configuration - Connects to SmartAttendance_DB
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- SESSION SUPPORT ---
// Configures 30-minute idle timeout as per your project requirements
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// 2. Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}

app.UseHttpsRedirection();

// This line is CRITICAL: It allows the app to serve your new "Alpine Logo.png" 
// from the wwwroot folder to the browser.
app.UseStaticFiles();

app.UseRouting();

// UseSession MUST be between UseRouting and UseAuthorization
app.UseSession();

app.UseAuthorization();

// 3. Set the default route
// The app starts at the Login page by default
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();