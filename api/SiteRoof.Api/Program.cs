using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Activity;
using SiteRoof.Api.Features.Auth;
using SiteRoof.Api.Features.Dashboard;
using SiteRoof.Api.Features.Demo;
using SiteRoof.Api.Features.Insights;
using SiteRoof.Api.Features.Integrations;
using SiteRoof.Api.Features.Materials;
using SiteRoof.Api.Features.Projects;
using SiteRoof.Api.Features.WorkOrders;
using SiteRoof.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Hosts such as Render inject PORT; honour it so the single container listens where the platform expects,
// and point the outbox worker at this process's own mock ERP on that port unless configured explicitly.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Erp__BaseUrl")))
    {
        config["Erp:BaseUrl"] = $"http://localhost:{port}";
    }
}

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

// ---------------------------------------------------------------- data
var connectionString = config.GetConnectionString("SiteRoof")
    ?? throw new InvalidOperationException("ConnectionStrings:SiteRoof is not configured. Set ConnectionStrings__SiteRoof.");
builder.Services.AddDbContext<SiteRoofDbContext>(o => o.UseSqlServer(connectionString, sql =>
{
    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
    sql.CommandTimeout(30);
}));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<SiteClock>();
builder.Services.AddScoped<ProjectAccess>();
builder.Services.AddScoped<AuditWriter>();

// ---------------------------------------------------------------- identity + cookies
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 12;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<SiteRoofDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<SiteRoofClaimsFactory>();

var secureCookies = config.GetValue("Security:SecureCookies", !builder.Environment.IsDevelopment());
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(c => c.ApplicationCookie!.Configure(o =>
{
    o.Cookie.Name = "siteroof.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    // An API never redirects to a login page: return status codes the SPA can handle.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
}));
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "siteroof.af";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

// ---------------------------------------------------------------- features
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<WorkOrderService>();
builder.Services.AddScoped<MaterialRequestService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<BlockerSummaryService>();
builder.Services.AddScoped<IBlockerSummaryProvider, RulesBasedBlockerSummaryProvider>();
builder.Services.Configure<InsightsOptions>(config.GetSection(InsightsOptions.Section));

builder.Services.Configure<DemoOptions>(config.GetSection(DemoOptions.Section));
builder.Services.AddScoped<DemoSeeder>();
builder.Services.AddScoped<DemoProvisioner>();
builder.Services.AddScoped<TenantDeleter>();
builder.Services.AddHostedService<DemoCleanupService>();

builder.Services.Configure<ErpOptions>(config.GetSection(ErpOptions.Section));
builder.Services.AddSingleton<IPostConfigureOptions<ErpOptions>, ErpOptionsSetup>();
builder.Services.Configure<OutboxOptions>(config.GetSection(OutboxOptions.Section));
builder.Services.AddSingleton<WorkerHeartbeat>();
builder.Services.AddHttpClient<IErpClient, HttpErpClient>((sp, http) =>
{
    var erp = sp.GetRequiredService<IOptions<ErpOptions>>().Value;
    http.BaseAddress = new Uri(erp.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(erp.TimeoutSeconds);
});
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddHostedService<OutboxDispatcher>();

// ---------------------------------------------------------------- web
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    var inner = o.InvalidModelStateResponseFactory;
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var result = inner(ctx);
        if (result is ObjectResult { Value: ProblemDetails pd }) pd.Extensions["code"] = "validation";
        return result;
    };
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "SiteRoof API";
    doc.Info.Description = "Construction operations API: projects, milestones, work orders, progress, material approvals, " +
                           "integration outbox and dashboards. Cookie-authenticated; unsafe requests require the X-XSRF-TOKEN header.";
    return Task.CompletedTask;
}));
builder.Services.AddSiteRoofRateLimits(config);
builder.Services.AddHealthChecks().AddDbContextCheck<SiteRoofDbContext>("database", tags: ["ready"]);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------- startup tasks
if (config.GetValue("Database:MigrateOnStartup", true) || args.Contains("--migrate-only"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SiteRoofDbContext>();
    await db.Database.MigrateAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in TenantRoles.All)
    {
        if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
    }
    if (args.Contains("--migrate-only"))
    {
        app.Logger.LogInformation("Migrations applied; exiting (--migrate-only).");
        return;
    }
}

// ---------------------------------------------------------------- pipeline
if (config.GetValue("ForwardedHeaders:Enabled", false)) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

var spaRoot = config["Spa:RootPath"] is { Length: > 0 } configured
    ? Path.GetFullPath(configured, app.Environment.ContentRootPath)
    : Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var spaFiles = Directory.Exists(spaRoot) ? new PhysicalFileProvider(spaRoot) : null;
if (spaFiles is not null)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = spaFiles,
        OnPrepareResponse = ctx =>
        {
            // Angular emits content-hashed bundles; index.html must always be revalidated.
            ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "index.html" ? "no-cache" : "public, max-age=31536000, immutable";
        },
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfValidationMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapOpenApi("/openapi/{documentName}.json");
if (app.Environment.IsDevelopment()) app.MapScalarApiReference(o => o.WithTitle("SiteRoof API"));
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

// Unknown /api routes are API 404s, never the SPA shell.
app.Map("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Not found", detail: "No such API endpoint.",
    extensions: new Dictionary<string, object?> { ["code"] = "not-found" }));
if (spaFiles is not null)
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = spaFiles,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
    });
}

app.Run();

public partial class Program;
