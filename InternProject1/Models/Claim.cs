using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Claim
{
    [Key]
    public int Claim_ID { get; set; }

    // This is the line you are likely missing!
    public int Employee_ID { get; set; }

    public string Claim_Type { get; set; } // e.g., Medical, Travel
    [Column(TypeName = "decimal(18,2)")] // Fixes the truncation warning
    public decimal Amount { get; set; }
    public DateTime Claim_Date { get; set; } = DateTime.Now;
    public DateTime Date_Submitted { get; set; }

    // Add these missing properties:
    public string Description { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ReceiptPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation property to the Employee model
    [ForeignKey("Employee_ID")]
    public Employee? Employee { get; set; }
}