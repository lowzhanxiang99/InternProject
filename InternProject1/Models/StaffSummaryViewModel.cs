namespace InternProject1.Models
{
    public class StaffSummaryViewModel
    {
        public int Employee_ID { get; set; } // Changed to public set for easier use
        public string? Name { get; set; }
        public int AttendanceCount { get; set; }
        public int LateCount { get; set; }
        public int LeaveCount { get; set; }
        public int AbsentCount { get; set; }
        public int OvertimeCount { get; set; }

        // --- ADD NEW TWO LINES ---
        public int HolidayCount { get; set; }
        public int SundayCount { get; set; }
        public string? LeaveDates { get; set; } // Stores dates as a string like "05 Feb, 06 Feb"
        public List<string> LeaveDatesList { get; set; } = new List<string>();
    }
}