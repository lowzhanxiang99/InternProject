namespace InternProject1.Models
{
    public class EmployeeMonitoringViewModel
    {
        public List<Employee> Employees { get; set; }
        public List<Attendance> TodayAttendance { get; set; }
        public DateTime SelectedDate { get; set; }

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