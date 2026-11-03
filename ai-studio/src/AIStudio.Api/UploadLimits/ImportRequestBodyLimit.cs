using AIStudio.Api.Endpoints;
using Microsoft.AspNetCore.Http.Features;

namespace AIStudio.Api.UploadLimits;

/// <summary>
/// Request-body bound for the one project-package import request. Kestrel's
/// <c>MaxRequestBodySize</c> and <see cref="FormOptions"/> keep their process defaults for
/// every other endpoint; only <c>POST /api/projects/imports</c> raises its body and
/// multipart limits to <c>ProjectTransfer:MaxPackageBytes</c> plus multipart overhead.
/// Multipart binding runs before endpoint code and endpoint filters, so middleware must
/// apply this feature instead of the endpoint.
/// </summary>
public static class ImportRequestBodyLimit
{
    /// <summary>Room for multipart boundaries, headers, and the form field envelope.</summary>
    public const long MultipartOverheadBytes = 1024 * 1024;

    private static readonly PathString ImportPath =
        new(ProjectTransferEndpoints.ImportRoute);

    public static long ResolveMaxRequestBodyBytes(long maxPackageBytes) =>
        maxPackageBytes + MultipartOverheadBytes;

    public static bool AppliesTo(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals(ImportPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies the package bound when the request targets the import route. Returns
    /// <see langword="true"/> when the request matched; a server that exposes no writable
    /// <see cref="IHttpMaxRequestBodySizeFeature"/> keeps its own limit, while the
    /// per-request multipart bound is still set.
    /// </summary>
    public static bool Apply(HttpContext context, long maxRequestBodyBytes)
    {
        if (!AppliesTo(context.Request))
        {
            return false;
        }

        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = maxRequestBodyBytes;
        }

        // FormOptions' 128 MB multipart default is process-wide and would reject a valid
        // package before the endpoint ran; replace only this request's form feature.
        if (context.Features.Get<IFormFeature>() is null)
        {
            context.Features.Set<IFormFeature>(
                new FormFeature(
                    context.Request,
                    new FormOptions { MultipartBodyLengthLimit = maxRequestBodyBytes }));
        }

        return true;
    }
}
