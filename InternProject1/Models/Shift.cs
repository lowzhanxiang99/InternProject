using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models;

public class Shift
{
    [Key]
    public int Shift_ID { get; set; }

    [Required]
    public string Shift_Name { get; set; }

    public TimeSpan Start_Time { get; set; }
    public TimeSpan End_Time { get; set; }

    // Changed to 'string?' so it can be optional in the database
    public string? Description { get; set; }

    // Changed 'internal set' to 'set' so EF can update it from the DB
    public bool Is_Default { get; set; }

    public ICollection<Employee>? Employees { get; set; }
    public virtual ICollection<ShiftSchedule> Schedules { get; set; } = new List<ShiftSchedule>();

    // Helper method to get time for a specific date
    public (TimeSpan start, TimeSpan end) GetTimeForDate(DateTime date)
    {
        // Check for specific date schedule
        var specificSchedule = Schedules?
            .FirstOrDefault(s =>
                s.Is_Active &&
                s.SpecificDate.HasValue &&
                s.SpecificDate.Value.Date == date.Date);

        if (specificSchedule != null)
        {
            return (specificSchedule.Start_Time, specificSchedule.End_Time);
        }

        // Check for day-of-week schedule
        var daySchedule = Schedules?
            .FirstOrDefault(s =>
                s.Is_Active &&
                s.DayOfWeek.HasValue &&
                s.DayOfWeek.Value == date.DayOfWeek &&
                (!s.StartDate.HasValue || date >= s.StartDate.Value) &&
                (!s.EndDate.HasValue || date <= s.EndDate.Value));

        if (daySchedule != null)
        {
            return (daySchedule.Start_Time, daySchedule.End_Time);
        }

        // Default to regular shift time
        return (Start_Time, End_Time);
    }
}