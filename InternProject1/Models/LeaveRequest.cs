using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class LeaveRequest
{
    [Key]
    public int Leave_ID { get; set; }
    public int Employee_ID { get; set; }
    public string? Leave_Type { get; set; }
    public DateTime Start_Date { get; set; }
    public DateTime End_Date { get; set; }
    public string? Reasons { get; set; }
    public string Status { get; set; } = "Pending"; // Default status

    [ForeignKey("Employee_ID")]
    public virtual Employee? Employee { get; set; }
}