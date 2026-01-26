using InternProject1.Controllers;

namespace InternProject1.Models
{
    public class EmployeeMonitoringViewModel
    {
        public List<Employee> Employees { get; set; }
        public List<Attendance> TodayAttendance { get; set; }
        public DateTime SelectedDate { get; set; }
        public int TotalEmployees { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int OnTimeCount { get; set; }
        public int TimeOffCount { get; set; }
        public int EarlyDeparturesCount { get; set; }
        public string EmployeeTrend { get; set; }
        public string TrendDirection { get; set; }
        // New properties for charts
        public double TodayAttendanceRate { get; set; }
        public double LastWeekAttendanceRate { get; set; }
        public double LastMonthAttendanceRate { get; set; }
        public int LastWeekPresentCount { get; set; }
        public int LastMonthPresentCount { get; set; }
        public List<DailyAttendance> WeeklyData { get; set; }
        public List<DailyAttendance> LastWeekData { get; set; }

        // Helper method to get attendance for specific employee
        public Attendance GetEmployeeAttendance(int employeeId)
        {
            return TodayAttendance?.FirstOrDefault(a => a.Employee_ID == employeeId);
        }

        // Helper method to get status class
        public string GetStatusClass(Attendance attendance)
        {
            if (attendance == null) return "absent";

            if (attendance.ClockInTime != null && attendance.ClockOutTime != null)
            {
                // Check if on time (before 9:00 AM)
                if (attendance.ClockInTime.Value.Hours < 9 ||
                    (attendance.ClockInTime.Value.Hours == 9 && attendance.ClockInTime.Value.Minutes == 0))
                {
                    return "on-time";
                }
                return "late";
            }
            else if (attendance.ClockInTime != null)
            {
                return "in-progress";
            }

            return "absent";
        }

        // Helper method to get status text
        public string GetStatusText(Attendance attendance)
        {
            if (attendance == null) return "Absent";

            if (attendance.ClockInTime != null && attendance.ClockOutTime != null)
            {
                if (attendance.ClockInTime.Value.Hours < 9 ||
                    (attendance.ClockInTime.Value.Hours == 9 && attendance.ClockInTime.Value.Minutes == 0))
                {
                    return "On Time";
                }
                return "Late";
            }
            else if (attendance.ClockInTime != null)
            {
                return "In Progress";
            }

            return "Absent";
        }
    }
}