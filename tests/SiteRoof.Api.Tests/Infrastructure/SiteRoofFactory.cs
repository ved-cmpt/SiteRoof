using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Data;
using SiteRoof.Api.Features.Integrations;
using Testcontainers.MsSql;

namespace SiteRoof.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API (all middleware, auth, CSRF, EF Core) against a disposable SQL Server 2022 container.
/// Each test provisions its own demo tenant, so tests are isolated without resetting the database.
/// The outbox dispatcher is disabled; tests drive <see cref="OutboxProcessor"/> directly with a fake clock.
/// </summary>
public sealed class SiteRoofFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:SiteRoof", _sql.GetConnectionString() + ";Database=SiteRoofTests");
        builder.UseSetting("Outbox:DispatcherEnabled", "false");
        builder.UseSetting("Outbox:BaseDelaySeconds", "5");
        builder.UseSetting("Demo:ProvisionPermitsPerWindow", "10000");
        builder.UseSetting("Demo:CleanupIntervalMinutes", "600");
        builder.UseSetting("Security:SecureCookies", "false");
        builder.UseSetting("Spa:RootPath", "does-not-exist");
        builder.UseSetting("Erp:ApiKey", "test-erp-key");
        builder.UseSetting("Erp:BaseUrl", "http://localhost");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            // Route the ERP adapter's HTTP calls into this in-memory server so the mock ERP endpoint is exercised over HTTP.
            services.AddHttpClient<IErpClient, HttpErpClient>(http => http.BaseAddress = new Uri("http://localhost/"))
                .ConfigurePrimaryHttpMessageHandler(() => new ForwardingHandler(() => Server));
        });
    }

    public TestApiClient CreateApiClient() => new(this);

    /// <summary>Runs one dispatcher pass, exactly as the background service would.</summary>
    public async Task<OutboxRunResult> RunOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(CancellationToken.None);
    }

    /// <summary>Unfiltered DbContext for assertions (tenant filters are bypassed with IgnoreQueryFilters()).</summary>
    public async Task<T> WithDbAsync<T>(Func<SiteRoofDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SiteRoofDbContext>();
        return await action(db);
    }

    public Task WithDbAsync(Func<SiteRoofDbContext, Task> action) => WithDbAsync(async db => { await action(db); return 0; });

    private sealed class ForwardingHandler(Func<TestServer> server) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            InnerHandler ??= server().CreateHandler();
            return base.SendAsync(request, ct);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SiteRoofFactory>
{
    public const string Name = "api";
}

public static class DbExtensions
{
    public static IQueryable<T> All<T>(this DbSet<T> set) where T : class => set.IgnoreQueryFilters();
}
