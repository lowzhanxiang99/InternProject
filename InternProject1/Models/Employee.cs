using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Employee
{
    [Key]
    public int Employee_ID { get; set; }

    public string First_Name { get; set; } = string.Empty;
    public string Last_Name { get; set; } = string.Empty;
    public DateTime Date_of_Birth { get; set; }
    public string Gender { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Employee_Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string Employee_Phone { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff"; // Admin or Staff
    public string Branch { get; set; } = string.Empty;
    public string? QR_Code_Data { get; set; }
    public string? ProfilePicturePath { get; set; }

    // Relationships
    public int? Department_ID { get; set; }
    [ForeignKey("Department_ID")]
    public virtual Department? Department { get; set; }

    public int? Shift_ID { get; set; }
    [ForeignKey("Shift_ID")]
    public virtual Shift? Shift { get; set; }
    public string? Employee_Name { get; internal set; }
    [NotMapped]
    public string FullName => $"{First_Name} {Last_Name}";

    // New Database
    public int AnnualLeaveDays { get; set; } = 0;
    public int MCDays { get; set; } = 0;
    public int EmergencyLeaveDays { get; set; } = 0;
    public int OtherLeaveDays { get; set; } = 0;

    public int MaternityLeaveDays { get; set; } = 90;
    public DateTime Joining_Date { get; set; }
}