using System;
using System.Collections.Generic;
using System.Linq;

namespace InternProject1.Models
{
    public class EmployeeMonitoringViewModel
    {
        public List<Employee> Employees { get; set; } = new List<Employee>();
        public List<Attendance> TodayAttendance { get; set; } = new List<Attendance>();
        public DateTime SelectedDate { get; set; }

        // Helper method to get attendance for specific employee
        public Attendance GetEmployeeAttendance(int employeeId)
        {
            return TodayAttendance?.FirstOrDefault(a => a.Employee_ID == employeeId);
        }

        // Helper method to get status class
        public string GetStatusClass(Attendance attendance)
        {
            // If no record exists or ClockIn is effectively empty
            if (attendance == null || attendance.ClockInTime == TimeSpan.Zero)
                return "absent";

            // If they have clocked in but NOT yet clocked out
            if (attendance.ClockOutTime == null || attendance.ClockOutTime == TimeSpan.Zero)
            {
                return "in-progress";
            }

            // If they have both ClockIn and ClockOut, check if they were late
            // (Comparing against 9:00 AM)
            if (attendance.ClockInTime.Hours < 9 || (attendance.ClockInTime.Hours == 9 && attendance.ClockInTime.Minutes == 0))
            {
                return "on-time";
            }

            return "late";
        }

        // Helper method to get status text
        public string GetStatusText(Attendance attendance)
        {
            if (attendance == null || attendance.ClockInTime == TimeSpan.Zero)
                return "Absent";

            if (attendance.ClockOutTime == null || attendance.ClockOutTime == TimeSpan.Zero)
            {
                return "In Progress";
            }

            if (attendance.ClockInTime.Hours < 9 || (attendance.ClockInTime.Hours == 9 && attendance.ClockInTime.Minutes == 0))
            {
                return "On Time";
            }

            return "Late";
        }
    }
}