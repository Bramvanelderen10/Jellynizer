using Microsoft.Extensions.Logging;

namespace Jellyniser.Logging;

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception);
