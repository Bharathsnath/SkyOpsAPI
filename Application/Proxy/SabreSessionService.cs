using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Proxy;

public sealed class SabreSessionService : ISabreSessionService
{
    private readonly HttpClient _httpClient;
    private readonly Queue7PollingOptions _options;
    private readonly IQueueActionRepository _repository;
    private readonly ILogger<SabreSessionService> _logger;
    private readonly IErrorLogService _errorLogService;

    public SabreSessionService(HttpClient httpClient, IOptions<Queue7PollingOptions> options, IQueueActionRepository repository, ILogger<SabreSessionService> logger, IErrorLogService errorLogService)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _repository = repository;
        _logger = logger;
        _errorLogService = errorLogService;
    }

    public async Task<SabreSession?> CreateSessionAsync(string username, string password, string pcc, CancellationToken cancellationToken = default)
    {
        var uplId = Guid.NewGuid().ToString("N")[..20];
        var endpoint = "https://webservices.platform.sabre.com";
        var conversationId = $"{Guid.NewGuid()}@{_options.SabreApi.FromPartyId}";
        var messageId = $"{Guid.NewGuid()}@{_options.SabreApi.FromPartyId}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
                           xmlns:eb="http://www.ebxml.org/namespaces/messageHeader"
                           xmlns:wsse="http://schemas.xmlsoap.org/ws/2002/12/secext">
              <soap:Header>
                <eb:MessageHeader soap:mustUnderstand="1" eb:version="1.0">
                  <eb:From>
                    <eb:PartyId eb:type="urn:x12.org.IO5:01">{Escape(_options.SabreApi.FromPartyId)}</eb:PartyId>
                  </eb:From>
                  <eb:To>
                    <eb:PartyId eb:type="urn:x12.org.IO5:01">{Escape(_options.SabreApi.ToPartyId)}</eb:PartyId>
                  </eb:To>
                  <CPAId>{Escape(pcc)}</CPAId>
                  <eb:ConversationId>{Escape(conversationId)}</eb:ConversationId>
                  <eb:Service>SessionCreateRQ</eb:Service>
                  <eb:Action>SessionCreateRQ</eb:Action>
                  <eb:MessageData>
                    <eb:MessageId>{Escape(messageId)}</eb:MessageId>
                    <eb:Timestamp>{timestamp}</eb:Timestamp>
                  </eb:MessageData>
                </eb:MessageHeader>
                <wsse:Security>
                  <wsse:UsernameToken>
                    <wsse:Username>{Escape(username)}</wsse:Username>
                    <wsse:Password>{Escape(password)}</wsse:Password>
                    <Organization>{Escape(pcc)}</Organization>
                    <Domain>DEFAULT</Domain>
                  </wsse:UsernameToken>
                </wsse:Security>
              </soap:Header>
              <soap:Body>
                <SessionCreateRQ Version="1.0.0" xmlns="http://www.opentravel.org/OTA/2002/11">
                  <POS>
                    <Source PseudoCityCode="{Escape(pcc)}" />
                  </POS>
                </SessionCreateRQ>
              </soap:Body>
            </soap:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "SessionCreateRQ");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;
            var logStatus = response.IsSuccessStatusCode ? "Success" : "Failed";

            await _repository.SaveApiLogAsync(pcc, "SessionCreateRQ", "", envelope, responseText, statusCode, logStatus, uplId, cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var failure = new HttpRequestException($"SessionCreateRQ failed for PCC {pcc}: HTTP {statusCode}");
                await _errorLogService.LogAsync(failure, "SessionCreateRQ", "SkyOpsQueueIntelligence", "SABRE", "CreateSessionAsync", nameof(SabreSessionService), null, cancellationToken);
                _logger.LogError(failure, "SessionCreateRQ failed for PCC {Pcc}: HTTP {Status}", pcc, statusCode);
                return null;
            }

            var token = ExtractSecurityToken(responseText);
            if (string.IsNullOrWhiteSpace(token))
            {
                var missingTokenException = new InvalidOperationException($"SessionCreateRQ returned no token for PCC {pcc}.");
                await _errorLogService.LogAsync(missingTokenException, "SessionCreateRQ", "SkyOpsQueueIntelligence", "SABRE", "CreateSessionAsync", nameof(SabreSessionService), null, cancellationToken);
                _logger.LogError(missingTokenException, "SessionCreateRQ returned no token for PCC {Pcc}.", pcc);
                return null;
            }

            _logger.LogInformation("Session created for PCC {Pcc}. UplId: {UplId}", pcc, uplId);
            return new SabreSession(token, conversationId, uplId);
        }
        catch (Exception ex)
        {
            await _errorLogService.LogAsync(ex, "SessionCreateRQ", "SkyOpsQueueIntelligence", "SABRE", "CreateSessionAsync", nameof(SabreSessionService), null, cancellationToken);
            throw;
        }

    }

    public async Task CloseSessionAsync(SabreSession session, CancellationToken cancellationToken = default)
    {
        var endpoint = "https://webservices.platform.sabre.com";
        var messageId = $"{Guid.NewGuid()}@{_options.SabreApi.FromPartyId}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
                           xmlns:eb="http://www.ebxml.org/namespaces/messageHeader"
                           xmlns:wsse="http://schemas.xmlsoap.org/ws/2002/12/secext">
              <soap:Header>
                <eb:MessageHeader soap:mustUnderstand="1" eb:version="1.0">
                  <eb:From>
                    <eb:PartyId eb:type="urn:x12.org.IO5:01">{Escape(_options.SabreApi.FromPartyId)}</eb:PartyId>
                  </eb:From>
                  <eb:To>
                    <eb:PartyId eb:type="urn:x12.org.IO5:01">{Escape(_options.SabreApi.ToPartyId)}</eb:PartyId>
                  </eb:To>
                  <eb:ConversationId>{Escape(session.ConversationId)}</eb:ConversationId>
                  <eb:Service>SessionCloseRQ</eb:Service>
                  <eb:Action>SessionCloseRQ</eb:Action>
                  <eb:MessageData>
                    <eb:MessageId>{Escape(messageId)}</eb:MessageId>
                    <eb:Timestamp>{timestamp}</eb:Timestamp>
                  </eb:MessageData>
                </eb:MessageHeader>
                <wsse:Security>
                  <wsse:BinarySecurityToken>{Escape(session.BinarySecurityToken)}</wsse:BinarySecurityToken>
                </wsse:Security>
              </soap:Header>
              <soap:Body>
                <SessionCloseRQ Version="1.0.0" xmlns="http://www.opentravel.org/OTA/2002/11"/>
              </soap:Body>
            </soap:Envelope>
            """;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            request.Headers.TryAddWithoutValidation("SOAPAction", "SessionCloseRQ");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;

            await _repository.SaveApiLogAsync("", "SessionCloseRQ", "", envelope, responseText, statusCode, response.IsSuccessStatusCode ? "Success" : "Failed", session.UplId, cancellationToken: cancellationToken);
            _logger.LogInformation("Session closed. UplId: {UplId}", session.UplId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SessionCloseRQ failed (non-critical).");
        }
    }

    private static string? ExtractSecurityToken(string soapResponse)
    {
        try
        {
            var doc = XDocument.Parse(soapResponse);
            var element = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "BinarySecurityToken");
            return element?.Value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
