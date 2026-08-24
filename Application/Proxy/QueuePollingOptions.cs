namespace SkyOpsQueueIntelligence.Application.Proxy;

public sealed class Queue7PollingOptions
{
    public const string SectionName = "Queue7Polling";

    public static readonly IReadOnlySet<string> AllowedHostCommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Q/7", "Q/379", "Q/62", "I", "QXI", "EWR" ,"QR"};

    public bool Enabled { get; init; } = true;

    public string Source { get; init; } = "SabreApi";

    public int IntervalMinutes { get; init; } = 15;

    public string LogFilePath { get; init; } = "logs/queue7-polling.log";

    public SabreApiOptions SabreApi { get; init; } = new();
    public GalileoApiOptions GalileoApi { get; init; } = new();

    public GalileoPollingOptions GalileoPolling { get; init; } = new();

    public IReadOnlyList<QueuePollingEntry> Queues { get; init; } =
    [
        new() { QueueNumber = 7, HostCommand = "Q/7" },
        new() { QueueNumber = 379, HostCommand = "Q/379" },
        new() { QueueNumber = 62, HostCommand = "Q/62" }
    ];
}

public sealed class QueuePollingEntry
{
    public int QueueNumber { get; init; }
    public string HostCommand { get; init; } = string.Empty;
}

public sealed class SabreApiOptions
{
    public string Endpoint { get; init; } = "https://webservices.platform.sabre.com";

    public string BinarySecurityToken { get; init; } = "";

    public string ConversationId { get; init; } = "";

    public string FromPartyId { get; init; } = "com.abacus.SWSSession";

    public string ToPartyId { get; init; } = "webservices.sabre.com";

    public string HostCommand { get; init; } = "Q/7";
}

public sealed class GalileoApiOptions
{
    public string Endpoint { get; init; } = "https://apac.webservices.travelport.com/B2BGateway/service/XMLSelect";

    public string Profile { get; init; } = string.Empty;

    public int SessionTimeoutOverride { get; init; } = 60000;
}

public sealed class GalileoPollingOptions
{
    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 15;

    public IReadOnlyList<int> Queues { get; init; } = [22, 23];
}
