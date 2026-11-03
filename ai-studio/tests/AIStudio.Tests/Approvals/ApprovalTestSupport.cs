using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Approvals;

internal static class ApprovalTestData
{
    public static readonly DateTimeOffset Now =
        new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    public const string RenderPath = "renders/project/video.mp4";
    public const string RenderHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string ChangedHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const long RenderByteSize = 4096;

    public static RenderVideoResult RenderResult(
        string? contentHash = null,
        long? byteSize = null,
        string? outputPath = null) =>
        new(
            outputPath ?? RenderPath,
            contentHash ?? RenderHash,
            byteSize ?? RenderByteSize,
            30,
            1280,
            720,
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            ChangedHash);

    public static string RenderResultJson(
        string? contentHash = null,
        long? byteSize = null,
        string? outputPath = null) =>
        RenderResult(contentHash, byteSize, outputPath).Serialize();

    public static FinalVideoQaResult QaResult(string? contentHash = null) =>
        new(
            RenderPath,
            contentHash ?? RenderHash,
            30,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: false);

    public static string QaResultJson(string? contentHash = null) =>
        QaResult(contentHash).Serialize();

    public static JobSnapshot RenderJob(
        Guid contentProjectId,
        Guid renderJobId,
        DateTimeOffset? createdAt = null,
        JobStatus status = JobStatus.Succeeded,
        string? result = null) =>
        new(
            renderJobId,
            contentProjectId,
            JobType.RenderVideo,
            status,
            0,
            2,
            result ?? RenderResultJson(),
            null,
            null,
            createdAt ?? Now,
            createdAt ?? Now,
            createdAt ?? Now,
            status == JobStatus.Succeeded ? createdAt ?? Now : null);

    public static FinalVideoQaEvidence QaEvidence(
        Guid qaJobId,
        string? result = null,
        bool succeeded = true) =>
        new(qaJobId, result ?? QaResultJson(), succeeded, Now.AddMinutes(1));

    public static FinalApproval CreateApproval(
        Guid contentProjectId,
        Guid renderJobId,
        string destination = "youtube",
        string? notes = null,
        string? approver = null,
        DateTimeOffset? approvedAt = null) =>
        FinalApproval.Create(
            contentProjectId,
            renderJobId,
            RenderPath,
            RenderHash,
            RenderByteSize,
            destination,
            notes,
            approver,
            approvedAt ?? Now);
}

internal sealed class FakeFinalApprovalRepository : IFinalApprovalRepository
{
    public FinalApproval? Approval { get; set; }

    public FinalVideoQaEvidence? QaEvidence { get; set; }

    public bool HasNewerRender { get; set; }

    public int SaveCount { get; private set; }

    public Task<FinalApproval?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Approval?.ContentProjectId == contentProjectId ? Approval : null);
    }

    public Task<FinalVideoQaEvidence?> FindLatestQaAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QaEvidence);
    }

    public Task<bool> HasNewerRenderAsync(
        Guid contentProjectId,
        Guid renderJobId,
        DateTimeOffset renderCreatedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HasNewerRender);
    }

    public void Add(FinalApproval approval) => Approval = approval;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FakePublicationRepository : IPublicationRepository
{
    public PublicationRecord? Record { get; set; }

    public int SaveCount { get; private set; }

    public Task<PublicationRecord?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Record?.ContentProjectId == contentProjectId ? Record : null);
    }

    public void Add(PublicationRecord publication) => Record = publication;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeAssetFileStore : IAssetFileStore
{
    public Dictionary<string, AssetFileInfo> Files { get; } =
        new(StringComparer.Ordinal);

    public FakeAssetFileStore()
    {
        Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            $"/tmp/{ApprovalTestData.RenderPath}",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.RenderHash);
    }

    public AssetFileInfo Register(string relativePath)
    {
        if (!Files.TryGetValue(relativePath, out var info))
        {
            throw new AssetCollectionException(
                "asset_file_not_found",
                $"'{relativePath}' does not exist.");
        }

        return info;
    }

    public Task<AssetFileInfo> WriteAsync(
        string relativePath,
        byte[] content,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The approval slice never writes artifacts.");
}

internal sealed class JobReaderStub : IJobReader
{
    public Dictionary<Guid, JobSnapshot> Jobs { get; } = [];

    public Task<JobSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Jobs.TryGetValue(id, out var job) ? job : null);
    }
}

internal sealed class ProjectReaderStub(ContentProjectSnapshot? project)
    : IContentProjectReader
{
    public Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(project?.Id == id ? project : null);
    }
}

internal sealed class SettableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
