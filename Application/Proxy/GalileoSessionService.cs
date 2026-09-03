using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using System.Xml;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Proxy;

public sealed class GalileoSessionService : IGalileoSessionService
{
    private readonly HttpClient _httpClient;
    private readonly GalileoApiOptions _options;
    private readonly IQueueActionRepository _repository;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<GalileoSessionService> _logger;

    public GalileoSessionService(
        HttpClient httpClient,
        IOptions<Queue7PollingOptions> options,
        IQueueActionRepository repository,
        ICredentialStore credentialStore,
        ILogger<GalileoSessionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.GalileoApi;
        _repository = repository;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public async Task<GalileoSession?> CreateSessionAsync(
        string pccCode,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pccCode))
            throw new ArgumentException("Galileo PCC code is required.");

        var credentials = _credentialStore.GetByPcc(pccCode)
            .Where(credential => credential.Provider.Equals("1G", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var username = credentials.FirstOrDefault(c => c.TagName.Trim().Equals("SessionUserName", StringComparison.OrdinalIgnoreCase))?.TagValue.Trim();
        var password = credentials.FirstOrDefault(c => c.TagName.Trim().Equals("SessionPassword", StringComparison.OrdinalIgnoreCase))?.TagValue.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new KeyNotFoundException($"Galileo SessionUserName/SessionPassword not found for PCC {pccCode}.");

        var sessionProfile = string.IsNullOrWhiteSpace(profile) ? _options.Profile : profile.Trim();
        if (string.IsNullOrWhiteSpace(sessionProfile))
            throw new ArgumentException("Galileo profile is required.");

        var uplId = Guid.NewGuid().ToString("N")[..20];
        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <soap:Body>
                <BeginSession xmlns="http://webservices.galileo.com">
                  <Profile>{Escape(sessionProfile)}</Profile>
                  <SessionTimeoutOverride>{_options.SessionTimeoutOverride}</SessionTimeoutOverride>
                </BeginSession>
              </soap:Body>
            </soap:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "http://webservices.galileo.com/BeginSession");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;

            await _repository.SaveApiLogAsync(
                sessionProfile, "GalileoSession", "BeginSession", envelope, responseText,
                statusCode, response.IsSuccessStatusCode ? "Success" : "Failed", uplId,
                workFlow: "GalileoSession", moduleName: "SkyOpsQueueIntelligence", moduleCode: "GALILEO",
                cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Galileo BeginSession failed: HTTP {statusCode} {response.ReasonPhrase}");

            var token = ExtractSessionToken(responseText);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Galileo BeginSession returned no session token.");

            _logger.LogInformation("Galileo session created for profile {Profile}. UplId: {UplId}", sessionProfile, uplId);
            return new GalileoSession(token, sessionProfile, uplId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Galileo BeginSession failed for profile {Profile}.", sessionProfile);
            throw;
        }
    }

    private static string? ExtractSessionToken(string responseText)
    {
        try
        {
            var document = XDocument.Parse(responseText);
            return document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "BeginSessionResult")?.Value.Trim();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}