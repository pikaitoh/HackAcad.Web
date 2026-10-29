namespace HackAcad.Web.Models.ViewModels;

// ADMIN - Shared count/pluralization text keeps filtered Admin lists consistent.

public static class ListPresentation
{
    public static string Count(int total, string noun) => $"{total} {noun}{(total == 1 ? "" : "s")}";
    public static string Summary(int total, string noun, bool filtered) => total == 0
        ? $"No {noun}s{(filtered ? " match your filters." : " yet.")}"
        : Count(total, noun) + (filtered ? (total == 1 ? " matches your filters." : " match your filters.") : "");
}
