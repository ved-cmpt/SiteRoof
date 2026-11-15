using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace SiteRoof.Api.Tests.Infrastructure;

/// <summary>
/// A browser-like client: keeps cookies and echoes the XSRF-TOKEN cookie in the X-XSRF-TOKEN header on
/// unsafe requests, exactly like Angular's HttpClient does.
/// </summary>
public sealed class TestApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly CookieContainer _cookies = new();
    public HttpClient Http { get; }
    public bool SendCsrf { get; set; } = true;

    public TestApiClient(SiteRoofFactory factory)
    {
        Http = factory.CreateDefaultClient(new Uri("http://localhost/"), new CookieContainerHandler(_cookies), new CsrfHandler(this));
    }

    public string? CsrfToken => _cookies.GetCookies(new Uri("http://localhost/"))["XSRF-TOKEN"]?.Value;

    public async Task<JsonElement> StartDemoAsync(string persona = "manager")
    {
        await Http.GetAsync("/api/auth/me");
        var res = await Http.PostAsJsonAsync("/api/demo/start", new { persona });
        await res.EnsureOk();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public async Task<JsonElement> SwitchPersonaAsync(string persona)
    {
        var res = await Http.PostAsJsonAsync("/api/demo/persona", new { persona });
        await res.EnsureOk();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var res = await Http.GetAsync(url);
        await res.EnsureOk();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public Task<HttpResponseMessage> PostAsync(string url, object body) => Http.PostAsJsonAsync(url, body, Json);

    public Task<HttpResponseMessage> PatchAsync(string url, object body) =>
        Http.PatchAsync(url, JsonContent.Create(body, options: Json));

    public async Task<JsonElement> PostJsonAsync(string url, object body)
    {
        var res = await PostAsync(url, body);
        await res.EnsureOk();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public async Task<Guid> ProjectIdAsync(string code)
    {
        var projects = await GetJsonAsync("/api/projects");
        return projects.EnumerateArray().Single(p => p.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    public async Task<JsonElement> WorkOrderByKeyAsync(Guid projectId, string key)
    {
        var board = await GetJsonAsync($"/api/projects/{projectId}/work-orders?pageSize=100");
        var id = board.GetProperty("items").EnumerateArray().Single(w => w.GetProperty("key").GetString() == key).GetProperty("id").GetGuid();
        return await GetJsonAsync($"/api/work-orders/{id}");
    }

    private sealed class CsrfHandler(TestApiClient owner) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (owner.SendCsrf && request.Method != HttpMethod.Get && owner.CsrfToken is { } token)
            {
                request.Headers.Add("X-XSRF-TOKEN", token);
            }
            return base.SendAsync(request, ct);
        }
    }
}

public static class HttpAssertions
{
    public static async Task EnsureOk(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode} {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri}: {body}");
        }
    }

    public static async Task<JsonElement> Problem(this HttpResponseMessage res, HttpStatusCode expected)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {(int)expected} but got {(int)res.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    public static string? Code(this JsonElement problem) =>
        problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("code", out var c) ? c.GetString() : null;
}
