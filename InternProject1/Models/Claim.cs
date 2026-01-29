using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Claim
{
    [Key]
    public int Claim_ID { get; set; }

    [Required]
    public int Employee_ID { get; set; }

    [Required]
    public string Claim_Type { get; set; } = string.Empty; // Travel, Meal, Medical, etc.

    [Required]
    public DateTime Claim_Date { get; set; }

    [Required]
    [Column(TypeName = "decimal(18, 2)")]
    public decimal Amount { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;

    public string? ReceiptPath { get; set; } // Path to the uploaded image/PDF

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey("Employee_ID")]
    public virtual Employee? Employee { get; set; }
}