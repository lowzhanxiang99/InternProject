using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models
{
    public class Attendance
    {
        [Key]
        public int Attendance_ID { get; set; }

        [Required]
        public int Employee_ID { get; set; }

        [ForeignKey("Employee_ID")]
        public Employee? Employee { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? ClockInTime { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? ClockOutTime { get; set; }
        public TimeSpan? Expected_Start { get; set; }      // What time they were supposed to start
        public TimeSpan? Expected_End { get; set; }        // What time they were supposed to end
        public string? Shift_Used { get; set; }            // Which shift name they used
        public int? Shift_ID_Used { get; set; }            // Which shift ID they used
        public bool Used_Default_Shift { get; set; } = false; // Was it a default shift?
        public string? IPAddress { get; set; }
        public string? UserAgent { get; set; }

        [StringLength(100)]
        public string? Location_Lat_Long { get; set; }

        [StringLength(50)]
        public string? Status { get; set; }

        public bool IsOnBreak { get; set; } = false;

        [DataType(DataType.DateTime)]
        public DateTime? BreakStartTime { get; set; }

        public TimeSpan? TotalBreakTime { get; set; } = TimeSpan.Zero;

        public bool HasTakenBreak { get; set; } = false;
    }
}