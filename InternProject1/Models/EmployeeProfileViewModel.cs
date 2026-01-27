using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models
{
    public class EmployeeProfileViewModel
    {
        public int Employee_ID { get; set; }

        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        [RegularExpression(@"^[a-zA-Z\s]+$", ErrorMessage = "First name can only contain letters")]
        public string First_Name { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        [RegularExpression(@"^[a-zA-Z\s]+$", ErrorMessage = "Last name can only contain letters")]
        public string Last_Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public string Employee_Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [RegularExpression(@"^(\+?6?01)[0-46-9]-*[0-9]{7,8}$", ErrorMessage = "Invalid Malaysian phone number format (e.g., 012-3456789 or +6012-3456789)")]
        public string Employee_Phone { get; set; }

        [Required(ErrorMessage = "Date of birth is required")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
        public DateTime Date_of_Birth { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        public string Gender { get; set; }

        public string Role { get; set; }

        public string Branch { get; set; }

        public string? ProfilePicturePath { get; set; }

        public int? Department_ID { get; set; }

        public int? Shift_ID { get; set; }

        public string FullName => $"{First_Name} {Last_Name}";
        public bool IsValidAge()
        {
            var age = DateTime.Today.Year - Date_of_Birth.Year;
            if (Date_of_Birth.Date > DateTime.Today.AddYears(-age)) age--;
            return age >= 18 && age <= 100;
        }
    }
}
