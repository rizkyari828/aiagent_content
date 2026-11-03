using AIStudio.Infrastructure.Transfer;
using Microsoft.Extensions.Options;

namespace AIStudio.Api.UploadLimits;

/// <summary>
/// Raises the request-body and multipart limits only for the project-package import
/// request; every other request keeps the process defaults.
/// </summary>
public sealed class ImportRequestBodyLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxRequestBodyBytes;

    public ImportRequestBodyLimitMiddleware(
        RequestDelegate next,
        IOptions<ProjectTransferOptions> options)
    {
        _next = next;
        _maxRequestBodyBytes = ImportRequestBodyLimit.ResolveMaxRequestBodyBytes(
            options.Value.MaxPackageBytes);
    }

    public Task InvokeAsync(HttpContext context)
    {
        ImportRequestBodyLimit.Apply(context, _maxRequestBodyBytes);
        return _next(context);
    }
}

public static class ImportRequestBodyLimitExtensions
{
    public static IApplicationBuilder UseImportRequestBodyLimit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ImportRequestBodyLimitMiddleware>();
    }
}
