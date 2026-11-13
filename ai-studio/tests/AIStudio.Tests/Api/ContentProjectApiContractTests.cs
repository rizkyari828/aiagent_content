using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Content;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class ContentProjectApiContractTests
{
    [Fact]
    public async Task Get_ReturnsDetailWithoutJobInternals()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.GenerateIdea, JobStatus.Succeeded, ContentProjectFixture.Now);

        var result = await ContentProjectEndpoints.GetAsync(
            fixture.ProjectId.ToString(),
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<ContentProjectDetailResponse>>(result);
        Assert.Equal(fixture.ProjectId, ok.Value!.Id);
        Assert.Equal("Readiness fixture", ok.Value.Title);
        Assert.Equal("Disposable fixture row.", ok.Value.Brief);
        Assert.Equal("draft", ok.Value.Status);
        Assert.NotNull(ok.Value.LastJob);
        Assert.Equal("GenerateIdea", ok.Value.LastJob.Type);
        Assert.Equal("completed", ok.Value.LastJob.Status);

        var json = Serialize(ok.Value);
        Assert.False(json.TryGetProperty("payload", out _));
        Assert.False(json.TryGetProperty("result", out _));
        Assert.False(json.TryGetProperty("workerId", out _));
        Assert.False(json.TryGetProperty("errorSummary", out _));
        Assert.Equal(fixture.ProjectId, json.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Get_ReturnsCodedNotFound()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var result = await ContentProjectEndpoints.GetAsync(
            fixture.ProjectId.ToString(),
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("content_project_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task List_ReturnsSummariesInDeterministicOrder()
    {
        var fixture = new ContentProjectFixture();
        var newer = ContentProject.Create(
            "Newer project",
            null,
            ContentProjectFixture.Now.AddMinutes(1));
        fixture.Projects.Projects[newer.Id] = newer;

        var result = await ContentProjectEndpoints.ListAsync(
            null,
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<ContentProjectSummaryResponse[]>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(2, ok.Value.Length);
        Assert.Equal(newer.Id, ok.Value[0].Id);
        Assert.Equal("draft", ok.Value[0].Status);
        Assert.Equal(fixture.ProjectId, ok.Value[1].Id);

        var json = Serialize(ok.Value[0]);
        Assert.True(json.TryGetProperty("updatedAt", out _));
        Assert.False(json.TryGetProperty("brief", out _));
        Assert.False(json.TryGetProperty("lastJob", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_RejectsTakeOutsideBoundsWithStableCode(int take)
    {
        var fixture = new ContentProjectFixture();

        var result = await ContentProjectEndpoints.ListAsync(
            take,
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            ContentProjectEndpoints.InvalidRequestCode,
            problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedDetail()
    {
        var fixture = new ContentProjectFixture();
        fixture.Time.UtcNow = ContentProjectFixture.Now.AddMinutes(3);

        var result = await ContentProjectEndpoints.UpdateAsync(
            fixture.ProjectId.ToString(),
            new UpdateContentProjectRequest
            {
                Title = "  Renamed project  ",
                Brief = "Updated brief"
            },
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<ContentProjectDetailResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("Renamed project", ok.Value.Title);
        Assert.Equal("Updated brief", ok.Value.Brief);
        Assert.Equal("draft", ok.Value.Status);
        Assert.Equal(ContentProjectFixture.Now.AddMinutes(3), ok.Value.UpdatedAt);
    }

    [Fact]
    public async Task Update_RejectsUnknownAndImmutableFields()
    {
        var fixture = new ContentProjectFixture();

        var result = await ContentProjectEndpoints.UpdateAsync(
            fixture.ProjectId.ToString(),
            new UpdateContentProjectRequest
            {
                Title = "Renamed project",
                AdditionalProperties = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("published"),
                    ["createdAt"] = JsonSerializer.SerializeToElement(
                        ContentProjectFixture.Now)
                }
            },
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            ContentProjectEndpoints.InvalidRequestCode,
            problem.ProblemDetails.Extensions["code"]);
        var validation = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Contains("status", validation.Errors.Keys);
        Assert.Contains("createdAt", validation.Errors.Keys);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Update_RejectsBlankTitleWithStableCode()
    {
        var fixture = new ContentProjectFixture();

        var result = await ContentProjectEndpoints.UpdateAsync(
            fixture.ProjectId.ToString(),
            new UpdateContentProjectRequest { Title = "   " },
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            ContentProjectEndpoints.InvalidRequestCode,
            problem.ProblemDetails.Extensions["code"]);
        var validation = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Contains("title", validation.Errors.Keys);
    }

    [Fact]
    public async Task Readiness_ReturnsComputedStatusAndReasons()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();

        var result = await ContentProjectEndpoints.GetReadinessAsync(
            fixture.ProjectId.ToString(),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<ContentReadinessResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("ready_to_publish", ok.Value.Status);
        Assert.True(ok.Value.ReadyToPublish);
        Assert.Equal("draft", ok.Value.ProjectStatus);
        var reason = Assert.Single(ok.Value.Reasons);
        Assert.Equal(ContentReadinessCodes.PublicationMissing, reason.Code);
        Assert.False(reason.Blocking);
    }

    [Fact]
    public async Task Readiness_ReturnsCodedNotFound()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var result = await ContentProjectEndpoints.GetReadinessAsync(
            fixture.ProjectId.ToString(),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("content_project_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Transition_MarksReadyToPublishAndIsIdempotent()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();

        var first = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("ready_to_publish"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);
        var second = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("ready_to_publish"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var firstOk = Assert.IsType<Ok<ContentProjectTransitionResponse>>(first);
        Assert.NotNull(firstOk.Value);
        Assert.True(firstOk.Value.Transitioned);
        Assert.Equal("ready_to_publish", firstOk.Value.ProjectStatus);

        var secondOk = Assert.IsType<Ok<ContentProjectTransitionResponse>>(second);
        Assert.NotNull(secondOk.Value);
        Assert.False(secondOk.Value.Transitioned);
        Assert.Equal(ContentStatus.ReadyToPublish, fixture.Project.Status);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Transition_RejectsNotReadyProjectWithReasonCodes()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.GenerateIdea, JobStatus.Succeeded, ContentProjectFixture.Now);

        var result = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("ready_to_publish"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("content_project_not_ready", problem.ProblemDetails.Extensions["code"]);
        var reasons = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            problem.ProblemDetails.Extensions["reasons"]);
        Assert.Equal(new[] { ContentReadinessCodes.RenderMissing }, reasons);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
    }

    [Fact]
    public async Task Transition_RejectsPublishedProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Project.MarkReadyToPublish(ContentProjectFixture.Now);
        fixture.Project.TransitionTo(
            ContentStatus.Published,
            ContentProjectFixture.Now.AddMinutes(1));

        var result = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("ready_to_publish"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal(
            "content_project_transition_not_allowed",
            problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Transition_Published_MarksPublishedAndIsIdempotent()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();
        fixture.Project.MarkReadyToPublish(ContentProjectFixture.Now);

        var first = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("published"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);
        var second = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("published"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var firstOk = Assert.IsType<Ok<ContentProjectTransitionResponse>>(first);
        Assert.NotNull(firstOk.Value);
        Assert.True(firstOk.Value.Transitioned);
        Assert.Equal("published", firstOk.Value.ProjectStatus);

        var secondOk = Assert.IsType<Ok<ContentProjectTransitionResponse>>(second);
        Assert.NotNull(secondOk.Value);
        Assert.False(secondOk.Value.Transitioned);
        Assert.Equal("published", secondOk.Value.ProjectStatus);
        Assert.Equal(ContentStatus.Published, fixture.Project.Status);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Transition_Published_RejectsWithoutPublicationRecord()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Project.MarkReadyToPublish(ContentProjectFixture.Now);

        var result = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("published"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("content_project_not_ready", problem.ProblemDetails.Extensions["code"]);
        var reasons = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            problem.ProblemDetails.Extensions["reasons"]);
        Assert.Equal(new[] { ContentReadinessCodes.PublicationMissing }, reasons);
        Assert.Equal(ContentStatus.ReadyToPublish, fixture.Project.Status);
    }

    [Fact]
    public async Task Transition_Published_RejectsArchivedProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();
        fixture.Project.MarkReadyToPublish(ContentProjectFixture.Now);
        fixture.Project.TransitionTo(
            ContentStatus.Archived,
            ContentProjectFixture.Now.AddMinutes(1));

        var result = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest("published"),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal(
            "content_project_transition_not_allowed",
            problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("unknown_stage")]
    [InlineData("")]
    public async Task Transition_RejectsUnsupportedTarget(string target)
    {
        var fixture = new ContentProjectFixture();

        var result = await ContentProjectEndpoints.TransitionAsync(
            fixture.ProjectId.ToString(),
            new TransitionContentProjectRequest(target),
            fixture.ReadinessWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            "content_project_transition_not_supported",
            problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
    }

    [Fact]
    public async Task Endpoints_RejectMalformedIdentifiers()
    {
        var fixture = new ContentProjectFixture();

        var result = await ContentProjectEndpoints.GetAsync(
            "not-a-guid",
            fixture.ProjectWorkflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            ContentProjectEndpoints.InvalidRequestCode,
            problem.ProblemDetails.Extensions["code"]);
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
