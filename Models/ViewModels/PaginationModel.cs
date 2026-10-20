namespace HackAcad.Web.Models.ViewModels;

public class PaginationModel(int page, int pageCount, string? suffix = null)
{
    public int Page { get; } = Math.Clamp(page, 1, Math.Max(1, pageCount));
    public int PageCount { get; } = Math.Max(1, pageCount);
    public string? Suffix { get; } = suffix;

    public IEnumerable<int> Numbers
    {
        get
        {
            if (PageCount <= 7) return Enumerable.Range(1, PageCount);
            var start = Page <= 4 ? 2 : Page >= PageCount - 3 ? PageCount - 4 : Page - 1;
            var end = Page <= 4 ? 5 : Page >= PageCount - 3 ? PageCount - 1 : Page + 1;
            return new[] { 1 }.Concat(Enumerable.Range(start, end - start + 1)).Append(PageCount);
        }
    }
}
