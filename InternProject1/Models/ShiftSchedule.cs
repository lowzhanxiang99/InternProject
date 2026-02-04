using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InternProject1.Models
{
    public class ShiftSchedule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Schedule_ID { get; set; }

        [Required]
        public int Shift_ID { get; set; }  // Foreign Key to Shift

        // For weekly patterns (e.g., every Saturday)
        public DayOfWeek? DayOfWeek { get; set; }

        // For specific dates (e.g., Dec 25, 2024)
        public DateTime? SpecificDate { get; set; }

        // For date ranges (e.g., Summer schedule)
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // Override times
        [Required]
        [DataType(DataType.Time)]
        public TimeSpan Start_Time { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan End_Time { get; set; }

        // Schedule type
        public string ScheduleType { get; set; } = "OneTime";

        // Properties
        [StringLength(200)]
        public string Description { get; set; }

        public bool Is_Active { get; set; } = true;
        public bool Is_HalfDay { get; set; }

        // Navigation property - ADD [ForeignKey] ATTRIBUTE
        [ForeignKey("Shift_ID")]
        public Shift Shift { get; set; }
    }
}