using Xunit;
using System.Text.Json;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.Domain.Tracking;

namespace AIStudio.Tests.Transfer;

public sealed class ProjectPackageManifestTests
{
    [Fact]
    public void Serialize_Deserialize_RoundTripsEverySection()
    {
        var manifest = FullManifest();

        var json = ProjectPackageSerializer.Serialize(manifest);
        var restored = ProjectPackageSerializer.Deserialize(json);

        Assert.Equal(ProjectPackageSerializer.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(manifest.ExportedAtUtc, restored.ExportedAtUtc);
        Assert.Equal("manifest-test", restored.AppVersion);
        Assert.Equal(manifest.Project, restored.Project);

        Assert.NotNull(restored.ReviewedScript);
        Assert.Equal(manifest.ReviewedScript!.Id, restored.ReviewedScript.Id);
        Assert.Equal(manifest.ReviewedScript.Status, restored.ReviewedScript.Status);
        Assert.Equal(manifest.ReviewedScript.Revision, restored.ReviewedScript.Revision);
        Assert.True(JsonElement.DeepEquals(
            manifest.ReviewedScript.Content,
            restored.ReviewedScript.Content));

        var job = Assert.Single(restored.SourceJobs);
        var expectedJob = Assert.Single(manifest.SourceJobs);
        Assert.Equal(expectedJob.Id, job.Id);
        Assert.Equal(expectedJob.Type, job.Type);
        Assert.Equal(expectedJob.Status, job.Status);
        Assert.True(JsonElement.DeepEquals(expectedJob.Payload, job.Payload));
        Assert.True(JsonElement.DeepEquals(
            expectedJob.Result!.Value,
            job.Result!.Value));

        Assert.Equal(manifest.SceneAssets.ToArray(), restored.SceneAssets.ToArray());
        Assert.Equal(manifest.NarrationTrack, restored.NarrationTrack);
        Assert.Equal(manifest.SubtitleTrack, restored.SubtitleTrack);
        Assert.Equal(manifest.FinalApproval, restored.FinalApproval);
        Assert.Equal(manifest.PublicationRecord, restored.PublicationRecord);
        Assert.Equal(manifest.MetricSnapshots.ToArray(), restored.MetricSnapshots.ToArray());
        Assert.Equal(manifest.TimeEntries.ToArray(), restored.TimeEntries.ToArray());
        Assert.Equal(manifest.CostEntries.ToArray(), restored.CostEntries.ToArray());
        Assert.Equal(manifest.Files.ToArray(), restored.Files.ToArray());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Draft", root.GetProperty("project").GetProperty("status").GetString());
        Assert.Equal("RenderVideo", root.GetProperty("sourceJobs")[0].GetProperty("type").GetString());
        Assert.Equal("human", root.GetProperty("timeEntries")[0]
            .GetProperty("kind").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public void Deserialize_RejectsUnknownMembers()
    {
        var json = ProjectPackageSerializer.Serialize(FullManifest());
        var text = System.Text.Encoding.UTF8.GetString(json);
        var withUnknown = text.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 1,\n  \"hostPath\": \"/Users/example\"",
            StringComparison.Ordinal);

        var error = Assert.Throws<ProjectTransferException>(
            () => ProjectPackageSerializer.Deserialize(
                System.Text.Encoding.UTF8.GetBytes(withUnknown)));

        Assert.Equal("import_package_invalid", error.ErrorCode);
    }

    [Fact]
    public void Deserialize_RejectsUnknownEnumValue()
    {
        var json = ProjectPackageSerializer.Serialize(FullManifest());
        var text = System.Text.Encoding.UTF8.GetString(json);
        var invalid = text.Replace("\"Draft\"", "\"NotAStatus\"", StringComparison.Ordinal);

        var error = Assert.Throws<ProjectTransferException>(
            () => ProjectPackageSerializer.Deserialize(
                System.Text.Encoding.UTF8.GetBytes(invalid)));

        Assert.Equal("import_package_invalid", error.ErrorCode);
    }

    private static ProjectTransferManifest FullManifest()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var storyboardJobId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var publicationId = Guid.NewGuid();
        var content = JsonSerializer.Deserialize<JsonElement>("""{"title":"Approved"}""");
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"contentProjectId":"fixture"}""");
        var result = JsonSerializer.Deserialize<JsonElement>("""{"title":"Storyboard"}""");
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var artifactPath = "renders/video.mp4";

        return new ProjectTransferManifest(
            1,
            now,
            "manifest-test",
            new ProjectManifestProject(
                projectId,
                "Manifest project",
                "Brief",
                ContentStatus.Draft,
                now.AddHours(-3),
                now.AddHours(-2)),
            new ProjectManifestReviewedScript(
                Guid.NewGuid(),
                projectId,
                storyboardJobId,
                content,
                ScriptReviewStatus.Approved,
                2,
                now.AddHours(-2),
                now.AddHours(-1),
                now.AddHours(-1)),
            [
                new ProjectManifestJob(
                    renderJobId,
                    projectId,
                    JobType.RenderVideo,
                    JobStatus.Succeeded,
                    "input-hash",
                    0,
                    1,
                    payload,
                    result,
                    now.AddHours(-2),
                    now.AddHours(-1),
                    now.AddHours(-2),
                    now.AddHours(-1))
            ],
            [
                new ProjectManifestSceneAsset(
                    Guid.NewGuid(),
                    projectId,
                    storyboardJobId,
                    0,
                    AssetType.Image,
                    "renders/scene.png",
                    10,
                    hash,
                    AssetOrigin.Local,
                    null,
                    "test",
                    null,
                    null,
                    now.AddHours(-2))
            ],
            new ProjectManifestMediaTrack(
                Guid.NewGuid(),
                projectId,
                storyboardJobId,
                "renders/narration.wav",
                20,
                hash,
                AssetOrigin.Local,
                null,
                "test",
                null,
                null,
                now.AddHours(-2)),
            new ProjectManifestMediaTrack(
                Guid.NewGuid(),
                projectId,
                storyboardJobId,
                "renders/subtitle.srt",
                30,
                hash,
                AssetOrigin.Local,
                null,
                "test",
                null,
                null,
                now.AddHours(-2)),
            new ProjectManifestFinalApproval(
                approvalId,
                projectId,
                renderJobId,
                artifactPath,
                hash,
                100,
                "youtube",
                "notes",
                "operator",
                1,
                now.AddHours(-1),
                now.AddHours(-1),
                now.AddHours(-1)),
            new ProjectManifestPublication(
                publicationId,
                projectId,
                approvalId,
                1,
                hash,
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                "dQw4w9WgXcQ",
                now.AddMinutes(-30),
                "notes",
                now.AddMinutes(-30)),
            [
                new ProjectManifestMetricSnapshot(
                    Guid.NewGuid(),
                    projectId,
                    publicationId,
                    100,
                    1000,
                    0.1m,
                    8.5m,
                    17m,
                    2,
                    now.AddMinutes(-20),
                    "manual",
                    "notes",
                    now.AddMinutes(-20))
            ],
            [
                new ProjectManifestTimeEntry(
                    Guid.NewGuid(),
                    projectId,
                    TimeEntryKind.Human,
                    "editing",
                    45,
                    now.AddMinutes(-20),
                    "notes",
                    now.AddMinutes(-20))
            ],
            [
                new ProjectManifestCostEntry(
                    Guid.NewGuid(),
                    projectId,
                    12.5m,
                    "USD",
                    CostEntryKind.Actual,
                    "tools",
                    now.AddMinutes(-10),
                    "notes",
                    now.AddMinutes(-10))
            ],
            [
                new ProjectManifestFile(artifactPath, hash, 100),
                new ProjectManifestFile("renders/scene.png", hash, 10)
            ]);
    }
}
