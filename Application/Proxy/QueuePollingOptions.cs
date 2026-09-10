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
    public AmadeusApiOptions AmadeusApi { get; init; } = new();

    public GalileoPollingOptions GalileoPolling { get; init; } = new();
    public AmadeusPollingOptions AmadeusPolling { get; init; } = new();

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

public sealed class AmadeusApiOptions
{
    public string Endpoint { get; init; } = "https://production.webservices.amadeus.com";
    public string CommandEndpoint { get; init; } = "";
    public string Namespace { get; init; } = "http://xml.amadeus.com/HSFREQ_07_3_1A";
    public string AuthenticationNamespace { get; init; } = "http://xml.amadeus.com/VLSSLQ_06_1_1A";
    public string AuthenticationOperation { get; init; } = "Security_Authenticate";
    public string AuthenticationSoapAction { get; init; } = "http://webservices.amadeus.com/1ASIWATOAKB/VLSSLQ_06_1_1A";
    public string CommandOperation { get; init; } = "Command_Cryptic";
    public string SoapAction { get; init; } = "http://webservices.amadeus.com/1ASIWATOAKB/HSFREQ_07_3_1A";
}

public sealed class AmadeusPollingOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 15;
    public string PccCode { get; init; } = "AM_BOMAK3303_DOM_MUM T DESK";
    public IReadOnlyList<int> Queues { get; init; } = [7, 41];

    public IReadOnlyDictionary<int, string> QueueCommands { get; init; } = new Dictionary<int, string>
    {
        [7] = "QSB7C0",
        [41] = "QSB41C5"
    };
}
