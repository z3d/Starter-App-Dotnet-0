using System.Net;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Middleware;

public class ValidationExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ValidationExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ArgumentNullException ex)
        {
            Log.Warning("Missing required data: {Message}", ex.Message);
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Log.Warning("Data out of range: {Message}", ex.Message);
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (ArgumentException ex)
        {
            Log.Warning("Validation error: {Message}", ex.Message);
            await HandleValidationExceptionAsync(context, ex);
        }
    }

    private static async Task HandleValidationExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var response = new
        {
            error = exception.Message,
            type = "ValidationError"
        };

        var jsonResponse = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(jsonResponse);
    }
}