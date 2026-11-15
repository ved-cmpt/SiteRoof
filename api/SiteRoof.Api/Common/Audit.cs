using System.Text.Json;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Common;

/// <summary>
/// Writes audit events into the same DbContext unit of work as the change they describe, so the
/// audit row commits (or rolls back) atomically with the business change.
/// </summary>
public sealed class AuditWriter(SiteRoofDbContext db, ICurrentUser user, TimeProvider clock)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public AuditEvent Record(
        Guid tenantId,
        Guid? projectId,
        string entityType,
        Guid entityId,
        string action,
        string summary,
        IReadOnlyDictionary<string, object?>? before = null,
        IReadOnlyDictionary<string, object?>? after = null,
        Guid? actorId = null)
    {
        var (b, a) = Diff(before, after);
        var evt = new AuditEvent
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ActorId = actorId ?? (user.IsAuthenticated && user.UserId != Guid.Empty ? user.UserId : null),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Summary = summary.Length > 400 ? summary[..400] : summary,
            BeforeJson = b is null ? null : JsonSerializer.Serialize(b, JsonOptions),
            AfterJson = a is null ? null : JsonSerializer.Serialize(a, JsonOptions),
            CreatedAt = clock.GetUtcNow(),
        };
        db.AuditEvents.Add(evt);
        return evt;
    }

    /// <summary>Keeps only the fields whose values changed. Either side may be null (create/delete).</summary>
    public static (Dictionary<string, object?>? Before, Dictionary<string, object?>? After) Diff(
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after)
    {
        if (before is null || after is null)
        {
            return (before?.ToDictionary(k => k.Key, v => v.Value), after?.ToDictionary(k => k.Key, v => v.Value));
        }

        var b = new Dictionary<string, object?>();
        var a = new Dictionary<string, object?>();
        foreach (var key in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(key, out var bv);
            after.TryGetValue(key, out var av);
            if (!Equals(Normalize(bv), Normalize(av)))
            {
                b[key] = bv;
                a[key] = av;
            }
        }
        return (b, a);
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        Enum e => e.ToString(),
        decimal d => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
