using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models;

public class Shift
{
    [Key]
    public int Shift_ID { get; set; }
    public TimeSpan Start_Time { get; set; }
    public TimeSpan End_Time { get; set; }
    public string Shift_Name { get; set; } = string.Empty;
    public bool Is_Default { get; set; } = false;      
    public string? Description { get; set; }
    public ICollection<Employee>? Employees { get; set; }
}