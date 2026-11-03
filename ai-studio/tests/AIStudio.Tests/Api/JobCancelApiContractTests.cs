using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class JobCancelApiContractTests
{
    [Fact]
    public async Task Cancel_CancelledJobReturnsGoneStatus()
    {
        var jobId = Guid.NewGuid();
        var endpoint = CreateEndpoint(
            new JobCancelResult(JobCancelOutcome.Cancelled, JobStatus.Cancelled));

        var result = await JobCancelEndpoints.CancelAsync(
            jobId.ToString(),
            endpoint,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<JobCancelResponse>>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.NotNull(ok.Value);
        Assert.Equal(jobId, ok.Value.JobId);
        Assert.Equal("cancelled", ok.Value.Status);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded, "completed")]
    [InlineData(JobStatus.Failed, "failed")]
    [InlineData(JobStatus.Cancelled, "cancelled")]
    public async Task Cancel_TerminalJobReturnsConflictWithStableCode(
        JobStatus status,
        string apiStatus)
    {
        var jobId = Guid.NewGuid();
        var endpoint = CreateEndpoint(
            new JobCancelResult(JobCancelOutcome.NotAllowed, status));

        var result = await JobCancelEndpoints.CancelAsync(
            jobId.ToString(),
            endpoint,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal(
            "job_cancel_not_allowed",
            problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(apiStatus, problem.ProblemDetails.Extensions["status"]);
    }

    [Fact]
    public async Task Cancel_UnknownJobReturnsCodedNotFound()
    {
        var endpoint = CreateEndpoint(
            new JobCancelResult(JobCancelOutcome.NotFound, null));

        var result = await JobCancelEndpoints.CancelAsync(
            Guid.NewGuid().ToString(),
            endpoint,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("job_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Cancel_RejectsMalformedIdentifier(string jobId)
    {
        var endpoint = CreateEndpoint(
            new JobCancelResult(JobCancelOutcome.NotFound, null));

        var result = await JobCancelEndpoints.CancelAsync(
            jobId,
            endpoint,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            "job_cancel_invalid_request",
            problem.ProblemDetails.Extensions["code"]);
    }

    private static CancelJobWorkflow CreateEndpoint(JobCancelResult result) =>
        new(new StubJobQueue(result), TimeProvider.System);

    private sealed class StubJobQueue(JobCancelResult result) : IJobQueue
    {
        public Task<ClaimedJob?> ClaimNextAsync(
            string workerId,
            DateTimeOffset utcNow,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This stub only cancels jobs.");

        public Task<bool> CompleteAsync(
            Guid jobId,
            string workerId,
            string result,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This stub only cancels jobs.");

        public Task<JobFailureResult?> FailAsync(
            Guid jobId,
            string workerId,
            string errorCode,
            string errorSummary,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This stub only cancels jobs.");

        public Task<bool> RenewLeaseAsync(
            Guid jobId,
            string workerId,
            DateTimeOffset utcNow,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This stub only cancels jobs.");

        public Task<JobCancelResult> CancelAsync(
            Guid jobId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
