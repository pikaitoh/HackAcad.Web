namespace HackAcad.Web.Services;

public static class CourseFiles
{
    public const long MaximumLength = 10 * 1024 * 1024;
    private const string PrivatePrefix = "/course-files/ctf/";
    private const string LegacyPrefix = "/uploads/ctf/";

    public static string? Resolve(IWebHostEnvironment environment, string? storedPath)
    {
        if (storedPath is null) return null;
        string root, name;
        if (storedPath.StartsWith(PrivatePrefix, StringComparison.Ordinal))
        {
            root = Path.Combine(environment.ContentRootPath, "App_Data", "ctf");
            name = storedPath[PrivatePrefix.Length..];
        }
        else if (storedPath.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            root = Path.Combine(environment.WebRootPath, "uploads", "ctf");
            name = storedPath[LegacyPrefix.Length..];
        }
        else return null;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 250
            || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':') || Path.GetFileName(name) != name)
            return null;
        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, comparison) ? fullPath : null;
    }

    public static async Task<string> SaveAsync(IWebHostEnvironment environment, IFormFile file)
    {
        if (file.Length <= 0 || file.Length > MaximumLength) throw new ArgumentException("Choose a file up to 10 MB.");
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (name.Length is 0 or > 150 || name.Any(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)))
            throw new ArgumentException("Choose a file with a valid name up to 150 characters.");
        var storedPath = PrivatePrefix + Guid.NewGuid().ToString("N") + "_" + name;
        var destination = Resolve(environment, storedPath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var stream = new FileStream(destination, FileMode.CreateNew);
        await file.CopyToAsync(stream);
        return storedPath;
    }
}
