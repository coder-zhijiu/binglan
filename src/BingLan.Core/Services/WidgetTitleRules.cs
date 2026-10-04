namespace BingLan.Core.Services;

public static class WidgetTitleRules
{
    /// <summary>
    /// A title for a new card that differs from the existing ones, so cards of the same
    /// kind can be told apart in lists: the default title first, then "标题 2", "标题 3", …
    /// </summary>
    public static string NextTitle(string baseTitle, IEnumerable<string> existingTitles)
    {
        var taken = existingTitles.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(baseTitle))
        {
            return baseTitle;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{baseTitle} {number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
