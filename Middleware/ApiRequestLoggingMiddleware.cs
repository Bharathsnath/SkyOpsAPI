using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Middleware;

public sealed class ApiRequestLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IQueueActionRepository repository, ISettingsService settingsService)
    {
        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase) || path == "/")
        {
            await next(context);
            return;
        }

        // Check if API request logging is enabled
        var configs = await settingsService.GetLoggingConfigurationsAsync(default);
        var isApiRequestLogEnabled = configs.Find(c => c.ConfigKey == "EnableApiRequestLog")?.IsEnabled ?? true;
        if (!isApiRequestLogEnabled)
        {
            await next(context);
            return;
        }

        var uplId = Guid.NewGuid().ToString("N")[..20];
        var method = context.Request.Method;

        // Capture request body
        context.Request.EnableBuffering();
        var requestBody = string.Empty;
        if (context.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        var requestXml = $"<Request><Method>{method}</Method><Path>{path}</Path><QueryString>{context.Request.QueryString}</QueryString><Body>{System.Security.SecurityElement.Escape(requestBody)}</Body></Request>";

        // Capture response body
        var originalBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await next(context);
        }
        catch
        {
            // Let exception-handling middleware write to the real response stream.
            context.Response.Body = originalBody;
            throw;
        }

        responseBuffer.Position = 0;
        var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        var statusCode = context.Response.StatusCode;
        var status = statusCode < 400 ? "SUCCESS" : "FAILED";
        var responseXml = $"<Response><StatusCode>{statusCode}</StatusCode><Body>{System.Security.SecurityElement.Escape(responseBody)}</Body></Response>";

        await repository.SaveApiLogAsync(
            pccCode: "",
            serviceName: "ApiController",
            hostCommand: $"{method} {path}",
            requestXml: requestXml,
            responseXml: responseXml,
            httpStatusCode: statusCode,
            status: status,
            uplId: uplId,
            workFlow: "ApiRequest",
            moduleName: "SkyOpsAPI",
            moduleCode: "API",
            cancellationToken: default);

    }
}
