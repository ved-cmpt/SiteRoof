namespace SiteRoof.Api.Common;

/// <summary>Small helpers for user-facing text.</summary>
public static class Text
{
    /// <summary>"1 day", "3 days"; pass the plural form when it is not just "+s".</summary>
    public static string Count(int n, string singular, string? plural = null) =>
        $"{n} {(n == 1 ? singular : plural ?? singular + "s")}";
}
