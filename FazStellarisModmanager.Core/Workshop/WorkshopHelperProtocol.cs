using System.Text.Json;
using System.Text.Json.Serialization;

namespace FazStellarisModmanager.Core.Workshop;

/// <summary>One line the Workshop helper process writes to its standard output.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelperProgress), "progress")]
[JsonDerivedType(typeof(HelperResults), "results")]
[JsonDerivedType(typeof(HelperUnavailable), "unavailable")]
public abstract record HelperMessage;

public sealed record HelperProgress(WorkshopProgress Progress) : HelperMessage;

/// <summary>The helper's answer; it writes this last.</summary>
public sealed record HelperResults(IReadOnlyList<WorkshopItemResult> Items) : HelperMessage;

/// <summary>Steam could not be reached; carries the <see cref="WorkshopUnavailableException"/> message.</summary>
public sealed record HelperUnavailable(string Message) : HelperMessage;

/// <summary>
/// The line protocol between the app and its Workshop helper process (the app's own exe started with
/// <see cref="Argument"/>). The app writes one request line (<see cref="WriteRequest"/>) to the helper's standard input
/// and keeps it open; closing it asks the helper to cancel. The helper writes one JSON line per message to its standard
/// output: progress lines, then results (or "unavailable"), then exits. Lines are plain ASCII (other characters are
/// escaped), so the console code page doesn't matter. Steam's native library may print its own lines to the same
/// output; <see cref="Parse"/> ignores them.
/// </summary>
public static class WorkshopHelperProtocol
{
    public const string Argument = "--workshop-helper";

    /// <summary>Exit codes. The app trusts the messages over these; they only explain a helper that said nothing.</summary>
    public const int ExitOk = 0, ExitFailed = 1, ExitUnavailable = 2, ExitBadRequest = 3, ExitCancelled = 4;

    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    sealed record Request(IReadOnlyList<ulong> Ids);

    public static string Write(HelperMessage message) => JsonSerializer.Serialize(message, Options);

    /// <summary>The message on a line, or null for anything else (blank lines, Steam's own output, malformed JSON).</summary>
    public static HelperMessage? Parse(string? line)
    {
        // Steam's native output may lack a line break, so our message can follow it on the same line.
        var start = line?.IndexOf('{') ?? -1;
        if (start < 0) return null;
        try
        {
            return JsonSerializer.Deserialize<HelperMessage>(line.AsSpan(start), Options) switch
            {
                HelperProgress { Progress: not null } p => p,
                HelperResults { Items: not null } r when r.Items.All(i => i is not null) => r,
                HelperUnavailable { Message: not null } u => u,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static string WriteRequest(IReadOnlyList<ulong> ids) => JsonSerializer.Serialize(new Request(ids), Options);

    /// <summary>The ids in a request line, or null when the line isn't a request.</summary>
    public static IReadOnlyList<ulong>? ParseRequest(string? line)
    {
        // Skips a byte order mark, which the helper's console input may decode as one or more stray characters.
        var start = line?.IndexOf('{') ?? -1;
        if (start < 0) return null;
        try
        {
            return JsonSerializer.Deserialize<Request>(line.AsSpan(start), Options)?.Ids;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
