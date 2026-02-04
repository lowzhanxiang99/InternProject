using InternProject1.Models;
using Microsoft.EntityFrameworkCore;

namespace InternProject1.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Each DbSet represents one table in your database. 
    public DbSet<Manager> Managers { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<Shift> Shifts { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Attendance> Attendances { get; set; }
    public DbSet<LeaveRequest> LeaveRequests { get; set; }
    public DbSet<Report> Reports { get; set; }
    public DbSet<Claim> Claims { get; set; }
    public DbSet<ShiftSchedule> ShiftSchedules { get; set; }
}