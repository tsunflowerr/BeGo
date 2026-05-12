using System.Net;
using System.Text.Json;
using FluentValidation;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Api.Middleware;

public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.BadRequest, "validation_failed", "Validation failed.", ex.Errors.Select(e => e.ErrorMessage));
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Forbidden, "domain_error", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Unauthorized, "unauthorized", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "External HTTP dependency failed.");
            await WriteProblemAsync(context, HttpStatusCode.BadGateway, "external_dependency_failed", "External dependency failed.");
        }
        catch (InvalidOperationException ex) when (IsExternalDependencyError(ex.Message))
        {
            _logger.LogWarning(ex, "External dependency operation failed.");
            await WriteProblemAsync(context, HttpStatusCode.BadGateway, "external_dependency_failed", "External dependency failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled API exception.");
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError, "internal_error", "An unexpected error occurred.");
        }
    }

    private static bool IsExternalDependencyError(string message) =>
        message.Contains("Mapbox", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Groq", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteProblemAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string code,
        string message,
        IEnumerable<string>? details = null)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = new
            {
                code,
                message,
                details = details?.ToArray() ?? []
            }
        }));
    }
}
