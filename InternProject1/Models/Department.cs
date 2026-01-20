using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Department
{
    [Key]
    public int Department_ID { get; set; }
    public string? Department_Name { get; set; }

    public int Manager_ID { get; set; }
    [ForeignKey("Manager_ID")]
    public Manager? Manager { get; set; }

    public ICollection<Employee>? Employees { get; set; }
}