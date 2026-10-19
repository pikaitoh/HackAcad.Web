using HackAcad.Web.Models.Entities;
namespace HackAcad.Web.Models.ViewModels;
public record BannerChoice(string Key, string Title, string Theme, bool Unlocked, string Requirement);
public class NotificationListModel
{
    public List<UserNotification> Items { get; set; } = [];
    public int Unread { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
}
public class HeaderToolsModel
{
    public string Name { get; set; } = "";
    public string Appearance { get; set; } = "system";
    public int Unread { get; set; }
    public List<UserNotification> Items { get; set; } = [];
}
