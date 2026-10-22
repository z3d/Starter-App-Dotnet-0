using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace StarterApp.Api.Infrastructure.Validation;

public sealed class UtcInstantParameterMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<MethodInfo, string[]> InstantParameters = new();

    public Task InvokeAsync(HttpContext context)
    {
        var handler = context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>();
        if (handler is null)
            return next(context);

        var errors = InstantParameters.GetOrAdd(handler, NamesOfInstantParameters)
            .Where(name => HasNonZeroOffset(context.Request.RouteValues.TryGetValue(name, out var routeValue)
                ? routeValue?.ToString()
                : context.Request.Query[name].ToString()))
            .Select(NonUtcInstantException.ErrorFor)
            .ToList();

        return errors.Count == 0 ? next(context) : throw new ValidationException(errors);
    }

    private static string[] NamesOfInstantParameters(MethodInfo handler) =>
        handler.GetParameters()
            .Where(parameter => parameter.ParameterType == typeof(DateTimeOffset) || parameter.ParameterType == typeof(DateTimeOffset?))
            .Select(parameter => parameter.GetCustomAttribute<FromQueryAttribute>()?.Name
                ?? parameter.GetCustomAttribute<FromRouteAttribute>()?.Name
                ?? parameter.Name!)
            .ToArray();

    private static bool HasNonZeroOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
        && instant.Offset != TimeSpan.Zero;
}
