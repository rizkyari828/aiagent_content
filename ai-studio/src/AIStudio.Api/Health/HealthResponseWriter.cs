using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIStudio.Api.Health;

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration
            })
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(response, SerializerOptions),
            context.RequestAborted);
    }
}
