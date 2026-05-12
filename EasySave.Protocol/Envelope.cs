using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasySave.Protocol;

// Format-fil unique : une ligne JSON = une enveloppe.
//
//   { "type": "cmd.jobs.run",
//     "correlationId": "9f7e...",
//     "payload": { "jobIds": [1, 2] } }
//
// CorrelationId : présent sur les commandes et leur réponse (le client peut
// faire correspondre rsp ↔ cmd même si plusieurs commandes en vol). Absent
// sur les events broadcast.
//
// Payload : JsonElement brut, décodé à la demande par TryDecode<T>(). Évite
// d'avoir une hiérarchie polymorphique gigantesque côté System.Text.Json.
public sealed class Envelope
{
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    public JsonElement? Payload { get; set; }

    public static Envelope Command(string type, object? payload, string? correlationId = null)
        => new()
        {
            Type = type,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Payload = payload is null ? null : Wrap(payload)
        };

    public static Envelope Event(string type, object? payload)
        => new() { Type = type, Payload = payload is null ? null : Wrap(payload) };

    public static Envelope Response(string type, string correlationId, object? payload)
        => new() { Type = type, CorrelationId = correlationId, Payload = payload is null ? null : Wrap(payload) };

    public T? TryDecode<T>()
    {
        if (Payload is null) return default;
        return Payload.Value.Deserialize<T>(NdjsonChannel.JsonOptions);
    }

    private static JsonElement Wrap(object payload)
    {
        // Sérialise puis reparse → évite les soucis de typage dynamique.
        var json = JsonSerializer.Serialize(payload, payload.GetType(), NdjsonChannel.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
