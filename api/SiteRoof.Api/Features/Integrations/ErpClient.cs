using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace SiteRoof.Api.Features.Integrations;

public sealed class ErpOptions
{
    public const string Section = "Erp";

    /// <summary>Base URL of the ERP endpoint. Defaults to this process's own mock ERP.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Shared key for the self-hosted mock ERP. Generated at startup when not configured.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public bool DispatcherEnabled { get; set; } = true;
    public double PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
    public double BaseDelaySeconds { get; set; } = 5;
    public double MaxDelaySeconds { get; set; } = 300;
    public double LockSeconds { get; set; } = 60;

    /// <summary>Exponential backoff with ±10% jitter: base * 2^(attempt-1), capped.</summary>
    public TimeSpan BackoffFor(int attempt, double jitter01)
    {
        var raw = BaseDelaySeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var capped = Math.Min(raw, MaxDelaySeconds);
        var factor = 0.9 + 0.2 * Math.Clamp(jitter01, 0, 1);
        return TimeSpan.FromSeconds(capped * factor);
    }
}

public static class OutboxKeys
{
    public static string ForApproval(Guid materialRequestId) => $"mr-approved:{materialRequestId:N}";
}

public sealed record RequisitionPayload(
    string RequisitionKey,
    Guid TenantId,
    Guid MaterialRequestId,
    string ProjectCode,
    string WorkOrderKey,
    string ItemName,
    decimal Quantity,
    string Unit,
    string RequestedBy,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

public sealed record ErpAcknowledgement(string ExternalReference, bool Duplicate);

public abstract record ErpResult
{
    public sealed record Success(string ExternalReference, bool Duplicate) : ErpResult;
    public sealed record Transient(string Error) : ErpResult;
    public sealed record Permanent(string Error) : ErpResult;
}

public interface IErpClient
{
    Task<ErpResult> SendRequisitionAsync(string idempotencyKey, string payloadJson, CancellationToken ct);
    Task<bool> PingAsync(CancellationToken ct);
}

/// <summary>HTTP adapter for the ERP. Sends the outbox idempotency key so the receiver can de-duplicate.</summary>
public sealed class HttpErpClient(HttpClient http, IOptions<ErpOptions> options, ILogger<HttpErpClient> logger) : IErpClient
{
    public const string KeyHeader = "X-Mock-Erp-Key";

    public async Task<ErpResult> SendRequisitionAsync(string idempotencyKey, string payloadJson, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "mock-erp/requisitions")
        {
            Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add(KeyHeader, options.Value.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var ack = await response.Content.ReadFromJsonAsync<ErpAcknowledgement>(ct);
                return ack is null ? new ErpResult.Transient("Empty acknowledgement") : new ErpResult.Success(ack.ExternalReference, ack.Duplicate);
            }
            var error = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            var transient = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
                or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
                || (int)response.StatusCode >= 500;
            return transient ? new ErpResult.Transient(error) : new ErpResult.Permanent(error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ERP call failed for {IdempotencyKey}", idempotencyKey);
            return new ErpResult.Transient(ex is TaskCanceledException ? "Timed out" : "Connection failed: " + ex.Message);
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, "mock-erp/status");
            request.Headers.Add(KeyHeader, options.Value.ApiKey);
            using var response = await http.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}

public sealed class ErpOptionsSetup : IPostConfigureOptions<ErpOptions>
{
    private static readonly string Generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public void PostConfigure(string? name, ErpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey)) options.ApiKey = Generated;
    }
}
