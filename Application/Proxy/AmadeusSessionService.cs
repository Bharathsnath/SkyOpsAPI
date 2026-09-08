using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Proxy;

public sealed class AmadeusSessionService : IAmadeusSessionService
{
    private readonly ConcurrentDictionary<string, int> _sequenceNumbers = new(StringComparer.Ordinal);
    private readonly HttpClient _httpClient;
    private readonly Queue7PollingOptions _options;
    private readonly ICredentialStore _credentialStore;
    private readonly IQueueActionRepository _repository;
    private readonly ILogger<AmadeusSessionService> _logger;

    public AmadeusSessionService(HttpClient httpClient, IOptions<Queue7PollingOptions> options,
        ICredentialStore credentialStore, IQueueActionRepository repository, ILogger<AmadeusSessionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _credentialStore = credentialStore;
        _repository = repository;
        _logger = logger;
    }

    public async Task<AmadeusSession?> CreateSessionAsync(string pccCode, CancellationToken cancellationToken = default)
    {
        var credentials = GetCredentials(pccCode);
        var sourceOffice = Get(credentials, "SourceOffice") ?? pccCode;
        var originator = Get(credentials, "Originator") ?? Get(credentials, "UserName");
        var organization = Get(credentials, "Organization") ?? "NMC-INDIA";
        var password = Get(credentials, "Password");
        var passwordData = Get(credentials, "BinaryData") ?? (password is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));
        if (string.IsNullOrWhiteSpace(originator) || string.IsNullOrWhiteSpace(passwordData))
            throw new KeyNotFoundException($"Amadeus Originator/Password credentials not found for PCC {pccCode}.");

        var uplId = Guid.NewGuid().ToString("N")[..20];
        var envelope = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
                        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:add="http://www.w3.org/2005/08/addressing" xmlns:vls="{{_options.AmadeusApi.AuthenticationNamespace}}" xmlns:ses="http://xml.amadeus.com/2010/06/Session_v3" xmlns:link="http://wsdl.amadeus.com/2010/06/ws/Link_v1">
                            <soap:Header>
                                <ses:Session TransactionStatusCode="Start"><ses:SessionId/><ses:SequenceNumber>1</ses:SequenceNumber><ses:SecurityToken/></ses:Session>
                                <add:MessageID>{{Guid.NewGuid()}}</add:MessageID><add:Action>{{Escape(_options.AmadeusApi.AuthenticationSoapAction)}}</add:Action><add:To>{{Escape(_options.AmadeusApi.Endpoint)}}</add:To><link:TransactionFlowLink />
                            </soap:Header>
              <soap:Body><vls:Security_Authenticate>
                <vls:userIdentifier><vls:originIdentification><vls:sourceOffice>{{Escape(sourceOffice)}}</vls:sourceOffice></vls:originIdentification><vls:originatorTypeCode>U</vls:originatorTypeCode><vls:originator>{{Escape(originator)}}</vls:originator></vls:userIdentifier>
                <vls:dutyCode><vls:dutyCodeDetails><vls:referenceQualifier>DUT</vls:referenceQualifier><vls:referenceIdentifier>SU</vls:referenceIdentifier></vls:dutyCodeDetails></vls:dutyCode>
                <vls:systemDetails><vls:organizationDetails><vls:organizationId>{{Escape(organization)}}</vls:organizationId></vls:organizationDetails></vls:systemDetails>
                <vls:passwordInfo><vls:dataLength>{{Get(credentials, "PasswordLength") ?? "8"}}</vls:dataLength><vls:dataType>E</vls:dataType><vls:binaryData>{{Escape(passwordData)}}</vls:binaryData></vls:passwordInfo>
              </vls:Security_Authenticate></soap:Body>
            </soap:Envelope>
            """;

        var responseText = await PostAsync(envelope, _options.AmadeusApi.AuthenticationOperation, _options.AmadeusApi.Endpoint, _options.AmadeusApi.AuthenticationSoapAction, _options.AmadeusApi.AuthenticationOperation, pccCode, uplId, cancellationToken);
        var sessionElement = XDocument.Parse(responseText).Descendants().FirstOrDefault(e => e.Name.LocalName == "Session");
        var sessionId = sessionElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "SessionId")?.Value.Trim();
        var token = sessionElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "SecurityToken")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Amadeus authentication returned no session.");
        _sequenceNumbers[sessionId] = 1;
        _logger.LogInformation("Amadeus session created for PCC {PccCode}. UplId: {UplId}", pccCode, uplId);
        var commandEndpoint = XDocument.Parse(responseText).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "To")?.Value.Trim();
        commandEndpoint = string.IsNullOrWhiteSpace(commandEndpoint)
            ? _options.AmadeusApi.CommandEndpoint
            : commandEndpoint;
        if (string.IsNullOrWhiteSpace(commandEndpoint))
            commandEndpoint = _options.AmadeusApi.Endpoint;
        return new AmadeusSession(sessionId, 1, token, pccCode, uplId, commandEndpoint);
    }

    public async Task<string> SendCommandAsync(AmadeusSession session, string command, CancellationToken cancellationToken = default)
    {
        var envelope = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
                        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:add="http://www.w3.org/2005/08/addressing" xmlns:cmd="{{_options.AmadeusApi.Namespace}}" xmlns:ses="http://xml.amadeus.com/2010/06/Session_v3" xmlns:link="http://wsdl.amadeus.com/2010/06/ws/Link_v1">
                            <soap:Header>
                                <ses:Session TransactionStatusCode="InSeries"><ses:SessionId>{{Escape(session.SessionId)}}</ses:SessionId><ses:SequenceNumber>{{NextSequence(session.SessionId)}}</ses:SequenceNumber><ses:SecurityToken>{{Escape(session.SecurityToken)}}</ses:SecurityToken></ses:Session>
                                <add:MessageID>{{Escape(session.SecurityToken)}}</add:MessageID><add:Action>{{Escape(_options.AmadeusApi.SoapAction)}}</add:Action><add:To>{{Escape(session.CommandEndpoint)}}</add:To><link:TransactionFlowLink />
                            </soap:Header>
                            <soap:Body><cmd:{{_options.AmadeusApi.CommandOperation}}><cmd:messageAction><cmd:messageFunctionDetails><cmd:messageFunction>M</cmd:messageFunction></cmd:messageFunctionDetails></cmd:messageAction><cmd:longTextString><cmd:textStringDetails>{{Escape(command)}}</cmd:textStringDetails></cmd:longTextString></cmd:{{_options.AmadeusApi.CommandOperation}}></soap:Body>
            </soap:Envelope>
            """;
        var responseText = await PostAsync(envelope, _options.AmadeusApi.CommandOperation, session.CommandEndpoint, _options.AmadeusApi.SoapAction, command, session.PccCode, session.UplId, cancellationToken);
        return ExtractResponseText(responseText);
    }

    public async Task CloseSessionAsync(AmadeusSession session, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    private async Task<string> PostAsync(string envelope, string operation, string endpoint, string soapAction, string hostCommand, string pccCode, string uplId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        await _repository.SaveApiLogAsync(pccCode, "Amadeus", hostCommand, envelope, text, (int)response.StatusCode,
            response.IsSuccessStatusCode ? "Success" : "Failed", uplId, workFlow: "Amadeus", moduleName: "SkyOpsQueueIntelligence", moduleCode: "AMADEUS", cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Amadeus {operation} failed: HTTP {(int)response.StatusCode}.");
        return text;
    }

    private List<StorePccCredential> GetCredentials(string pccCode)
        => _credentialStore.GetByPcc(pccCode).Where(c => c.Provider.Equals("AM", StringComparison.OrdinalIgnoreCase) || c.Provider.Equals("AMADEUS", StringComparison.OrdinalIgnoreCase)).ToList();

    private static string? Get(IEnumerable<StorePccCredential> credentials, string name)
        => credentials.FirstOrDefault(c => c.TagName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))?.TagValue.Trim();

    private static string ExtractResponseText(string text)
    {
        try
        {
            var document = XDocument.Parse(text);
            var fault = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
            if (fault is not null) throw new InvalidOperationException($"Amadeus SOAP fault: {fault.Value.Trim()}");
            return string.Join(Environment.NewLine, document.Descendants().Where(e => !e.HasElements).Select(e => e.Value.Trim()).Where(v => v.Length > 0));
        }
        catch (System.Xml.XmlException) { return text.Trim(); }
    }

    private int NextSequence(string sessionId)
        => _sequenceNumbers.AddOrUpdate(sessionId, 2, (_, sequence) => sequence + 1);

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}