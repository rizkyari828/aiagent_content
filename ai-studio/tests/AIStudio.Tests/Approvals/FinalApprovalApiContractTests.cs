using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Approvals;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Approvals;

public sealed class FinalApprovalApiContractTests
{
    [Fact]
    public async Task Approve_ReturnsCreatedWithoutWorkerInternals()
    {
        var fixture = new ApiFixture();

        var result = await FinalApprovalEndpoints.ApproveAsync(
            fixture.ProjectId.ToString(),
            new ApproveFinalApprovalRequest(
                fixture.RenderJobId,
                "youtube",
                "manual upload",
                "operator-1"),
            fixture.ApprovalWorkflow,
            TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<FinalApprovalResponse>>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.NotNull(created.Value);
        Assert.Equal(fixture.RenderJobId, created.Value.SourceRenderJobId);
        Assert.Equal(1, created.Value.Revision);

        var json = JsonSerializer.SerializeToElement(
            created.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("youtube", json.GetProperty("destination").GetString());
        Assert.Equal(
            ApprovalTestData.RenderHash,
            json.GetProperty("artifactContentHash").GetString());
        Assert.False(json.TryGetProperty("payload", out _));
        Assert.False(json.TryGetProperty("workerId", out _));
        Assert.False(json.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Approve_ReturnsConflictWithStableCode()
    {
        var fixture = new ApiFixture();
        fixture.Approvals.QaEvidence = null;

        var result = await FinalApprovalEndpoints.ApproveAsync(
            fixture.ProjectId.ToString(),
            new ApproveFinalApprovalRequest(fixture.RenderJobId, "youtube", null, null),
            fixture.ApprovalWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal(
            "final_approval_qa_required",
            problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Approve_ReturnsValidationProblemForInvalidRequest()
    {
        var fixture = new ApiFixture();

        var result = await FinalApprovalEndpoints.ApproveAsync(
            fixture.ProjectId.ToString(),
            new ApproveFinalApprovalRequest(fixture.RenderJobId, "bad platform!", null, null),
            fixture.ApprovalWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            "final_approval_invalid_request",
            problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task GetStatus_ReturnsMissingApprovedAndInvalidStates()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var missing = await FinalApprovalEndpoints.GetStatusAsync(
            fixture.ProjectId.ToString(),
            fixture.ApprovalWorkflow,
            cancellationToken);
        var missingOk = Assert.IsType<Ok<FinalApprovalStatusResponse>>(missing);
        Assert.Equal("missing", missingOk.Value!.Status);
        Assert.Null(missingOk.Value.Approval);

        await fixture.ApprovalWorkflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);
        var approved = await FinalApprovalEndpoints.GetStatusAsync(
            fixture.ProjectId.ToString(),
            fixture.ApprovalWorkflow,
            cancellationToken);
        var approvedOk = Assert.IsType<Ok<FinalApprovalStatusResponse>>(approved);
        Assert.Equal("approved", approvedOk.Value!.Status);
        Assert.Null(approvedOk.Value.Reason);
        Assert.NotNull(approvedOk.Value.Approval);

        fixture.Store.Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            "/tmp/video.mp4",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.ChangedHash);
        var invalid = await FinalApprovalEndpoints.GetStatusAsync(
            fixture.ProjectId.ToString(),
            fixture.ApprovalWorkflow,
            cancellationToken);
        var invalidOk = Assert.IsType<Ok<FinalApprovalStatusResponse>>(invalid);
        Assert.Equal("invalid", invalidOk.Value!.Status);
        Assert.Equal(
            "final_approval_artifact_mismatch",
            invalidOk.Value.Reason);
        Assert.NotNull(invalidOk.Value.Approval);
    }

    [Fact]
    public async Task GetStatus_ReturnsNotFoundWhenProjectDoesNotExist()
    {
        var fixture = new ApiFixture(projectExists: false);

        var result = await FinalApprovalEndpoints.GetStatusAsync(
            fixture.ProjectId.ToString(),
            fixture.ApprovalWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("content_project_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Publication_CreateAndGetRoundTripWithStableShape()
    {
        var fixture = new ApiFixture(seedApproval: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new CreatePublicationRequest(
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "dQw4w9WgXcQ",
            ApprovalTestData.Now.AddHours(-2),
            "manual upload");

        var created = await PublicationEndpoints.CreateAsync(
            fixture.ProjectId.ToString(),
            request,
            fixture.PublicationWorkflow,
            cancellationToken);
        var createdResult = Assert.IsType<Created<PublicationResponse>>(created);
        Assert.Equal(201, createdResult.StatusCode);
        Assert.NotNull(createdResult.Value);
        Assert.Equal("youtube", createdResult.Value.Platform);
        Assert.Equal("dQw4w9WgXcQ", createdResult.Value.VideoId);
        Assert.Equal(fixture.Approval!.Id, createdResult.Value.FinalApprovalId);

        var repeated = await PublicationEndpoints.CreateAsync(
            fixture.ProjectId.ToString(),
            request,
            fixture.PublicationWorkflow,
            cancellationToken);
        Assert.IsType<Ok<PublicationResponse>>(repeated);

        var fetched = await PublicationEndpoints.GetAsync(
            fixture.ProjectId.ToString(),
            fixture.PublicationWorkflow,
            cancellationToken);
        var fetchedOk = Assert.IsType<Ok<PublicationResponse>>(fetched);
        Assert.Equal(createdResult.Value.Id, fetchedOk.Value!.Id);

        var json = JsonSerializer.SerializeToElement(
            fetchedOk.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("dQw4w9WgXcQ", json.GetProperty("videoId").GetString());
        Assert.False(json.TryGetProperty("workerId", out _));
    }

    [Fact]
    public async Task Publication_ReturnsConflictAndNotFoundCodes()
    {
        var fixture = new ApiFixture();

        var conflict = await PublicationEndpoints.CreateAsync(
            fixture.ProjectId.ToString(),
            new CreatePublicationRequest(
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                "dQw4w9WgXcQ",
                ApprovalTestData.Now,
                null),
            fixture.PublicationWorkflow,
            TestContext.Current.CancellationToken);

        var conflictProblem = Assert.IsType<ProblemHttpResult>(conflict);
        Assert.Equal(409, conflictProblem.StatusCode);
        Assert.Equal(
            "publication_approval_required",
            conflictProblem.ProblemDetails.Extensions["code"]);

        var missing = await PublicationEndpoints.GetAsync(
            fixture.ProjectId.ToString(),
            fixture.PublicationWorkflow,
            TestContext.Current.CancellationToken);
        var missingProblem = Assert.IsType<ProblemHttpResult>(missing);
        Assert.Equal(404, missingProblem.StatusCode);
        Assert.Equal(
            "publication_not_found",
            missingProblem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Publication_ReturnsBadRequestCodeForInvalidVideoId()
    {
        var fixture = new ApiFixture();

        var result = await PublicationEndpoints.CreateAsync(
            fixture.ProjectId.ToString(),
            new CreatePublicationRequest(
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                "https://youtu.be/dQw4w9WgXcQ",
                ApprovalTestData.Now,
                null),
            fixture.PublicationWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            "publication_video_id_invalid",
            problem.ProblemDetails.Extensions["code"]);
    }

    private sealed class ApiFixture
    {
        public ApiFixture(bool projectExists = true, bool seedApproval = false)
        {
            if (seedApproval)
            {
                Approvals.Approval = ApprovalTestData.CreateApproval(
                    ProjectId,
                    RenderJobId);
            }

            Approvals.QaEvidence = ApprovalTestData.QaEvidence(QaJobId);
            Jobs.Jobs[RenderJobId] = ApprovalTestData.RenderJob(ProjectId, RenderJobId);

            var projectReader = new ProjectReaderStub(
                projectExists
                    ? new ContentProjectSnapshot(ProjectId, "API fixture", null)
                    : null);
            ApprovalWorkflow = new FinalApprovalWorkflow(
                Approvals,
                projectReader,
                Jobs,
                Store,
                Time);
            PublicationWorkflow = new PublicationWorkflow(
                Publications,
                projectReader,
                ApprovalWorkflow,
                Time);
        }

        public Guid ProjectId { get; } = Guid.NewGuid();

        public Guid RenderJobId { get; } = Guid.NewGuid();

        public Guid QaJobId { get; } = Guid.NewGuid();

        public FakeFinalApprovalRepository Approvals { get; } = new();

        public FakePublicationRepository Publications { get; } = new();

        public JobReaderStub Jobs { get; } = new();

        public FakeAssetFileStore Store { get; } = new();

        public SettableTimeProvider Time { get; } = new(ApprovalTestData.Now);

        public FinalApproval? Approval => Approvals.Approval;

        public FinalApprovalWorkflow ApprovalWorkflow { get; }

        public PublicationWorkflow PublicationWorkflow { get; }
    }
}
