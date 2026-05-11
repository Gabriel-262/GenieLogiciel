using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasySave.Protocol;

// Canal de communication line-delimited JSON par-dessus un Stream (TCP).
// Une enveloppe = une ligne UTF-8 terminée par '\n'.
//
// Conventions :
//  - Pas de framing binaire : '\n' suffit car JsonSerializer ne produit jamais
//    de saut de ligne brut dans le JSON sérialisé (les \n dans les chaînes sont
//    échappés en \\n).
//  - SendAsync sérialise en mémoire puis écrit en un seul WriteAsync : évite
//    les writes partiels concurrents qui mélangeraient deux messages.
//  - ReadAsync renvoie null si la connexion est fermée proprement.
public sealed class NdjsonChannel : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public NdjsonChannel(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

    public async Task<Envelope?> ReadAsync(CancellationToken ct = default)
    {
        var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) return null;          // EOF
        if (line.Length == 0) return await ReadAsync(ct).ConfigureAwait(false); // skip lignes vides
        return JsonSerializer.Deserialize<Envelope>(line, JsonOptions);
    }

    public async Task SendAsync(Envelope envelope, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writeLock.Dispose();
    }
}
