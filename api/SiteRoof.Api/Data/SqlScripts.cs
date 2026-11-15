namespace SiteRoof.Api.Data;

/// <summary>Reads T-SQL scripts embedded from Data/Sql.</summary>
public static class SqlScripts
{
    public static string Read(string name)
    {
        var assembly = typeof(SqlScripts).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
