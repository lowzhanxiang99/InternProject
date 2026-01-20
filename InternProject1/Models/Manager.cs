using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models;

public class Manager
{
    [Key]
    public int Manager_ID { get; set; }
    public string? Manager_Name { get; set; }

    // Navigation property: One manager can manage departments
    public ICollection<Department>?  Departments { get; set; }
}
