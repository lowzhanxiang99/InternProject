using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models;

public class Shift
{
    [Key]
    public int Shift_ID { get; set; }

    // Change 'object' to 'string' here!
    [Required]
    public string Shift_Name { get; set; }

    public TimeSpan Start_Time { get; set; }
    public TimeSpan End_Time { get; set; }

    // Changed to 'string?' so it can be optional in the database
    public string? Description { get; set; }

    // Changed 'internal set' to 'set' so EF can update it from the DB
    public bool Is_Default { get; set; }

    public ICollection<Employee>? Employees { get; set; }
}