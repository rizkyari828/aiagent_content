using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class GenerateIdeaApiContractsTests
{
    [Theory]
    [InlineData(JobStatus.Queued, "queued")]
    [InlineData(JobStatus.Running, "running")]
    [InlineData(JobStatus.Succeeded, "completed")]
    [InlineData(JobStatus.Failed, "failed")]
    [InlineData(JobStatus.Cancelled, "cancelled")]
    public void JobResponse_MapsDomainStatusToApiStatus(
        JobStatus status,
        string expectedStatus)
    {
        var now = DateTimeOffset.UtcNow;
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.GenerateIdea,
            status,
            1,
            2,
            null,
            status == JobStatus.Failed ? "ai_timeout" : null,
            status == JobStatus.Failed ? "Timed out." : null,
            now,
            now,
            status == JobStatus.Queued ? null : now,
            status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled
                ? now
                : null);

        var response = JobResponse.From(details);

        Assert.Equal(expectedStatus, response.Status);
        Assert.Equal(details.Id, response.Id);
        Assert.Equal(details.ContentProjectId, response.ContentProjectId);
        Assert.Equal("GenerateIdea", response.Type);
        Assert.Null(response.Result);
    }

    [Fact]
    public void JobResponse_PreservesCompletedGenerateIdeaResult()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new GenerateIdeaResult(
            "Local AI Content",
            "Create privately.",
            "A local workflow.",
            "Privacy",
            "Creators",
            "Tutorial");
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.GenerateIdea,
            JobStatus.Succeeded,
            0,
            2,
            result,
            null,
            null,
            now,
            now,
            now,
            now);

        var response = JobResponse.From(details);

        var responseResult = Assert.IsType<GenerateIdeaResult>(response.Result);
        Assert.Equal("completed", response.Status);
        Assert.Same(result, response.Result);
        Assert.Equal("Local AI Content", responseResult.Title);
        Assert.Equal("Tutorial", responseResult.SuggestedFormat);
    }
}
