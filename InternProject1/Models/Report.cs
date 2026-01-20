using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models;

public class Report
{
    [Key]
    public int Report_ID { get; set; }

    public DateTime Generated_Date { get; set; }
    public string? Month { get; set; }
    public int TotalPresent { get; set; }
    public int TotalAbsend { get; set; } // Spelling from your ERD

    public int Employee_ID { get; set; }
    [ForeignKey("Employee_ID")]
    public Employee? Employee { get; set; }
}