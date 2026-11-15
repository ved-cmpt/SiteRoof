using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SiteRoof.Api.Auth;

namespace SiteRoof.Api.Data;

/// <summary>Used only by <c>dotnet ef</c> to build migrations; no database connection is opened.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SiteRoofDbContext>
{
    public SiteRoofDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__SiteRoof")
                 ?? "Server=127.0.0.1,1434;Database=SiteRoof;Integrated Security=false;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<SiteRoofDbContext>().UseSqlServer(cs).Options;
        return new SiteRoofDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
