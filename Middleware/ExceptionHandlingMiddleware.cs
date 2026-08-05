using System.Net;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IErrorLogService errorLogService)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var workflow = context.Request.Method + " " + (context.Request.Path.Value ?? "unknown");
            var moduleName = "SkyOpsQueueIntelligence";
            var moduleCode = "API";
            var className = nameof(ExceptionHandlingMiddleware);
            var procedureName = context.Request.Path.Value ?? "Unknown";

            await errorLogService.LogAsync(
                ex,
                workflow,
                moduleName,
                moduleCode,
                procedureName,
                className,
                context);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"An unexpected error occurred.\"}");
        }
    }
}
