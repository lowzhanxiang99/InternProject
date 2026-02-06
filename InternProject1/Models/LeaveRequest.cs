using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models
{
    public class LeaveRequest
    {
        [Key]
        public int Leave_ID { get; set; } // Primary Key from ERD

        [Required]
        public int Employee_ID { get; set; } // Foreign Key from ERD

        public string? Leave_Type { get; set; } // Nullable to avoid compiler errors

        [DataType(DataType.Date)]
        public DateTime Start_Date { get; set; } // From ERD

        [DataType(DataType.Date)]
        public DateTime End_Date { get; set; } // From ERD

        // Inside Models/LeaveRequest.cs
        public DateTime? Request_Date { get; set; }

        public string? Reasons { get; set; } // Nullable string for "Enter Reasons" field

        public string? Status { get; set; } = "Pending"; // Matches status dots in your design

        public int? Leave_Balance { get; set; } // Included from ERD to prevent submission fails

        public string? LeaveType { get; set; } // "Annual", "MC", "Emergency", "Other"

        // Navigation property to link to the Employee table
        [ForeignKey("Employee_ID")]
        public virtual Employee? Employee { get; set; }
        public string? Reason { get; set; }
        public string? Email { get; set; }
        public string? AttachmentPath { get; set; }
    }
}