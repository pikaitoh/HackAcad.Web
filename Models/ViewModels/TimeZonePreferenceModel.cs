using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.ViewModels;

public class TimeZonePreferenceModel
{
    [Required(ErrorMessage = "Choose a time zone."), StringLength(100), Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "";
}
