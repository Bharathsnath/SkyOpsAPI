using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Filters;

public sealed class ExceptionLoggingFilter : IAsyncExceptionFilter
{
    private readonly IErrorLogService _errorLogService;

    public ExceptionLoggingFilter(IErrorLogService errorLogService)
    {
        _errorLogService = errorLogService;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.ExceptionHandled)
        {
            return;
        }

        var workflow = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        var controllerName = context.ActionDescriptor?.RouteValues.TryGetValue("controller", out var controllerValue) == true
            ? controllerValue?.ToString() ?? "Controller"
            : "Controller";
        var actionName = context.ActionDescriptor?.RouteValues.TryGetValue("action", out var actionValue) == true
            ? actionValue?.ToString() ?? "Unknown"
            : "Unknown";

        await _errorLogService.LogAsync(
            context.Exception,
            workflow,
            "SkyOpsQueueIntelligence",
            "CONTROLLER",
            actionName,
            controllerName,
            context.HttpContext);

        context.Result = new ObjectResult(new { message = "An unexpected error occurred." })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
        context.ExceptionHandled = true;
    }
}
