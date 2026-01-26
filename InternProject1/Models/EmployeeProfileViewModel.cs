using System.ComponentModel.DataAnnotations;

namespace InternProject1.Models
{
    public class EmployeeProfileViewModel
    {
        public int Employee_ID { get; set; }

        [Required]
        [Display(Name = "First Name")]
        public string First_Name { get; set; }

        [Required]
        [Display(Name = "Last Name")]
        public string Last_Name { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string Employee_Email { get; set; }

        [Phone]
        [Display(Name = "Phone Number")]
        public string Employee_Phone { get; set; }

        [Required]
        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime Date_of_Birth { get; set; }

        [Required]
        [Display(Name = "Gender")]
        public string Gender { get; set; }

        public string Role { get; set; }

        public string Branch { get; set; }

        public string? ProfilePicturePath { get; set; }

        public int? Department_ID { get; set; }

        public int? Shift_ID { get; set; }

        public string FullName => $"{First_Name} {Last_Name}";
    }
}
