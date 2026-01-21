using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;



namespace InternProject1.Models;



public class Attendance

{

    [Key]

    public int Attendance_ID { get; set; }



    public DateTime Date { get; set; }

    public TimeSpan ClockInTime { get; set; }

    public TimeSpan? ClockOutTime { get; set; }

    public string? Location_Lat_Long { get; set; }

    public string? Status { get; set; }



    public int Employee_ID { get; set; }

    [ForeignKey("Employee_ID")]

    public Employee? Employee { get; set; }

}