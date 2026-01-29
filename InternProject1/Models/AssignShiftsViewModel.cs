namespace InternProject1.Models
{
    public class AssignShiftsViewModel
    {
        public List<Employee> UnassignedEmployees { get; set; } = new();
        public List<Employee> AllEmployees { get; set; } = new();
        public List<Shift> Shifts { get; set; } = new();
    }
}