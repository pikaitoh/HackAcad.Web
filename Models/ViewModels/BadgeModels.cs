namespace HackAcad.Web.Models.ViewModels;

public class BadgeItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
<<<<<<< Updated upstream
    public string Category { get; set; } = string.Empty; // e.g., "Enrollment", "CTF", "Simulation", "Mastery"
    public string Rarity { get; set; } = "Common";
=======
    public string Category { get; set; } = string.Empty; // e.g., "Enrollment", "CTF", "Terminal", "Mastery"
>>>>>>> Stashed changes
    public string IconSvg { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#33796e";
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedDate { get; set; }
    public int CurrentProgress { get; set; }
    public int TargetProgress { get; set; }
}

<<<<<<< Updated upstream
public class BadgesPageViewModel
{
    public List<BadgeItem> Badges { get; set; } = [];
    public string StatusFilter { get; set; } = "all";
    public string CategoryFilter { get; set; } = "";
    public string RarityFilter { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public List<string> Rarities { get; set; } = [];
    public int UnlockedCount { get; set; }
    public int TotalCount { get; set; }
}

=======
>>>>>>> Stashed changes
public class UserBadgesViewModel
{
    public List<BadgeItem> Badges { get; set; } = [];
    public int UnlockedCount => Badges.Count(b => b.IsUnlocked);
    public int TotalCount => Badges.Count;
}
