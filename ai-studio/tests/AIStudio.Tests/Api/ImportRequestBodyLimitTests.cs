using AIStudio.Api.Endpoints;
using AIStudio.Api.UploadLimits;
using AIStudio.Infrastructure.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using System.Text;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class ImportRequestBodyLimitTests
{
    private const string Boundary = "----aistudio-import-boundary";
    private const long PackageBytes = 8 * 1024 * 1024;
    private const long RequestBytes =
        PackageBytes + ImportRequestBodyLimit.MultipartOverheadBytes;

    [Fact]
    public void Post_ToImportRoute_RaisesBodyAndMultipartLimits()
    {
        var context = CreateContext(HttpMethods.Post, ProjectTransferEndpoints.ImportRoute);
        var feature = new FakeMaxRequestBodySizeFeature();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        var matched = ImportRequestBodyLimit.Apply(context, RequestBytes);

        Assert.True(matched);
        Assert.Equal(RequestBytes, feature.MaxRequestBodySize);
        Assert.IsType<FormFeature>(context.Features.Get<IFormFeature>());
    }

    [Fact]
    public async Task ImportRequest_AppliesThePackageBoundToMultipartBinding()
    {
        const long multipartBound = 128;

        var allowed = CreateMultipartContext();
        ImportRequestBodyLimit.Apply(allowed, RequestBytes);
        var form = await allowed.Request.ReadFormAsync(
            TestContext.Current.CancellationToken);
        Assert.Single(form.Files);

        // The same valid body must fail only because the import request carries the
        // smaller bound; Form binding runs before endpoint code and endpoint filters.
        var rejected = CreateMultipartContext();
        ImportRequestBodyLimit.Apply(rejected, multipartBound);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => rejected.Request.ReadFormAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("GET", ProjectTransferEndpoints.ImportRoute)]
    [InlineData("PUT", ProjectTransferEndpoints.ImportRoute)]
    [InlineData("POST", "/api/projects/imports/other")]
    [InlineData("POST", "/api/content-projects")]
    [InlineData("POST", "/api/projects/exports")]
    public void OtherRequests_KeepProcessDefaults(string method, string path)
    {
        var context = CreateContext(method, new PathString(path));
        var feature = new FakeMaxRequestBodySizeFeature
        {
            MaxRequestBodySize = 30_000_000
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        var matched = ImportRequestBodyLimit.Apply(context, RequestBytes);

        Assert.False(matched);
        Assert.Equal(30_000_000, feature.MaxRequestBodySize);
        Assert.Null(context.Features.Get<IFormFeature>());
    }

    [Fact]
    public void ImportRoute_IsCaseInsensitiveLikeRouting()
    {
        var context = CreateContext(HttpMethods.Post, "/API/Projects/Imports");
        var feature = new FakeMaxRequestBodySizeFeature
        {
            MaxRequestBodySize = 30_000_000
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        var matched = ImportRequestBodyLimit.Apply(context, RequestBytes);

        Assert.True(matched);
        Assert.Equal(RequestBytes, feature.MaxRequestBodySize);
    }

    [Fact]
    public void ReadOnlyBodySizeFeature_IsNotChanged()
    {
        var context = CreateContext(HttpMethods.Post, ProjectTransferEndpoints.ImportRoute);
        var feature = new FakeMaxRequestBodySizeFeature
        {
            IsReadOnly = true,
            MaxRequestBodySize = 30_000_000
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        var matched = ImportRequestBodyLimit.Apply(context, RequestBytes);

        Assert.True(matched);
        Assert.Equal(30_000_000, feature.MaxRequestBodySize);
    }

    [Fact]
    public void MissingBodySizeFeature_DoesNotThrow()
    {
        var context = CreateContext(HttpMethods.Post, ProjectTransferEndpoints.ImportRoute);

        var matched = ImportRequestBodyLimit.Apply(context, RequestBytes);

        Assert.True(matched);
        Assert.NotNull(context.Features.Get<IFormFeature>());
    }

    [Fact]
    public async Task Middleware_AppliesLimitToImportRequestOnly()
    {
        var seen = -1L;
        var middleware = new ImportRequestBodyLimitMiddleware(
            context =>
            {
                seen = context.Features
                    .Get<IHttpMaxRequestBodySizeFeature>()?
                    .MaxRequestBodySize ?? -1;
                return Task.CompletedTask;
            },
            Options.Create(new ProjectTransferOptions { MaxPackageBytes = PackageBytes }));

        var import = CreateContext(HttpMethods.Post, ProjectTransferEndpoints.ImportRoute);
        import.Features.Set<IHttpMaxRequestBodySizeFeature>(
            new FakeMaxRequestBodySizeFeature());
        await middleware.InvokeAsync(import);
        Assert.Equal(RequestBytes, seen);

        var other = CreateContext(HttpMethods.Post, "/api/content-projects");
        other.Features.Set<IHttpMaxRequestBodySizeFeature>(
            new FakeMaxRequestBodySizeFeature { MaxRequestBodySize = 30_000_000 });
        await middleware.InvokeAsync(other);
        Assert.Equal(30_000_000, seen);
    }

    [Fact]
    public void ResolveMaxRequestBodyBytes_AddsMultipartOverhead()
    {
        Assert.Equal(
            PackageBytes + (1024 * 1024),
            ImportRequestBodyLimit.ResolveMaxRequestBodyBytes(PackageBytes));
    }

    private static DefaultHttpContext CreateContext(string method, PathString path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    private static DefaultHttpContext CreateMultipartContext()
    {
        var context = CreateContext(HttpMethods.Post, ProjectTransferEndpoints.ImportRoute);
        context.Request.ContentType = $"multipart/form-data; boundary={Boundary}";
        context.Request.Body = new MemoryStream(CreateMultipartBody(512));
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(
            new FakeMaxRequestBodySizeFeature());
        return context;
    }

    private static byte[] CreateMultipartBody(int contentBytes)
    {
        var payload = string.Join(
            "\r\n",
            $"--{Boundary}",
            "Content-Disposition: form-data; name=\"file\"; filename=\"package.zip\"",
            "Content-Type: application/zip",
            "",
            new string('x', contentBytes),
            $"--{Boundary}--",
            "");
        return Encoding.UTF8.GetBytes(payload);
    }

    private sealed class FakeMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; set; }

        public long? MaxRequestBodySize { get; set; }
    }
}
