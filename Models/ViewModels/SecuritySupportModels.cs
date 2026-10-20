using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;
namespace HackAcad.Web.Models.ViewModels;

public class AdminVerifyInput
{
    public string Ticket { get; set; } = "";
    [StringLength(100), DataType(DataType.Password), Display(Name="Current password")] public string? CurrentPassword { get; set; }
    [StringLength(32), Display(Name="Verification or recovery code")] public string? Code { get; set; }
}
public class SecurityOverviewModel
{
    public string? Search { get; set; }
    public int FailedLogins { get; set; }
    public int LockedAccounts { get; set; }
    public int OpenAlerts { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; }
    public int Total { get; set; }
    public int PageCount => Math.Max(1,(int)Math.Ceiling(Total/20d));
    public List<SecurityAlert> Alerts { get; set; } = [];
}
public class AlertUpdateInput
{
    public string? ReturnUrl { get; set; }
    [Required] public string Status { get; set; } = "";
    private string notes = "";
    [Required, StringLength(1000, MinimumLength=10)] public string Notes { get => notes; set => notes = value?.Trim() ?? ""; }
    public string Version { get; set; } = "";
}
public class AdminSessionsModel
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<AccountSession> Sessions { get; set; } = [];
}
public class SupportStartInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
}
public class SupportMessageInput
{
    [StringLength(50)] public string Category { get; set; } = SupportCategory.General;
    private string? subject;
    private string body = "";
    [StringLength(120, MinimumLength=5)] public string? Subject { get => subject; set => subject = value?.Trim(); }
    [Required, StringLength(3000, MinimumLength=10)] public string Body { get => body; set => body = value?.Trim() ?? ""; }
    public bool Internal { get; set; }
    public string? Status { get; set; }
    public string Version { get; set; } = "";
}
public class SupportListModel
{
    public List<SupportTicket> Tickets { get; set; } = [];
    public Dictionary<string,string> Users { get; set; } = [];
    public HashSet<string> DisabledUsers { get; set; } = [];
    public string? Category { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; }
    public int Total { get; set; }
    public int PageCount => Math.Max(1,(int)Math.Ceiling(Total/20d));
}
public class SupportThreadModel
{
    public bool CanReply => Ticket.CanUserReply(Messages.LastOrDefault(m => !m.Internal)?.ByAdmin == true);
    public SupportTicket Ticket { get; set; } = null!;
    public List<SupportMessage> Messages { get; set; } = [];
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool Disabled { get; set; }
    public List<AdminActivity> AccessChanges { get; set; } = [];
    public SupportMessageInput Input { get; set; } = new();
}
public class AdminAccessInput
{
    public bool ReadOnly { get; set; }
    private string reason = "";
    [Required, StringLength(500,MinimumLength=10)] public string Reason { get => reason; set => reason = value?.Trim() ?? ""; }
}

public class AdminResumeModel
{
    public string Path { get; set; } = "";
    public Dictionary<string,string[]> Fields { get; set; } = [];
}
