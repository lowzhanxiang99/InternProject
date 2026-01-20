using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Employee
{
    [Key]
    public int Employee_ID { get; set; }

    public string Employee_Name { get; set; }
    public string Employee_Email { get; set; }
    public string Employee_Phone { get; set; }
    public string Role { get; set; } // Admin/Staff
    public string QR_Code_Data { get; set; }

    public int Department_ID { get; set; }
    [ForeignKey("Department_ID")]
    public Department Department { get; set; }

    public int Shift_ID { get; set; }
    [ForeignKey("Shift_ID")]
    public Shift Shift { get; set; }
}