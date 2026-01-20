namespace InternProject1.Models
{
    public class StaffSummaryViewModel
    {
        public string? Name { get; set; }
        public int AttendanceCount { get; set; }
        public int LateCount { get; set; }
        public int LeaveCount { get; set; }
        public int AbsentCount { get; set; }
        public int OvertimeCount { get; set; }
    }
}