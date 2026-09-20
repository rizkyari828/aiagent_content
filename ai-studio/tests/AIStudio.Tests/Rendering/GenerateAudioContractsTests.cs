using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateAudioContractsTests
{
    [Fact]
    public void Result_RoundTripsArtifactsAndReuseFlags()
    {
        var result = new GenerateAudioResult(
            Guid.NewGuid(),
            Artifact("audio/project/narration.wav"),
            Artifact("audio/project/music.wav"),
            Artifact("audio/project/master.wav"),
            NarrationReused: true,
            MusicReused: false,
            MasterReused: true);

        var roundTripped = GenerateAudioResult.Deserialize(result.Serialize());

        Assert.Equal(result.StoryboardJobId, roundTripped.StoryboardJobId);
        Assert.Equal(result.Narration, roundTripped.Narration);
        Assert.Equal(result.Music, roundTripped.Music);
        Assert.Equal(result.Master, roundTripped.Master);
        Assert.True(roundTripped.NarrationReused);
        Assert.False(roundTripped.MusicReused);
        Assert.True(roundTripped.MasterReused);
    }

    [Fact]
    public void Result_RejectsEmptyArtifactPath()
    {
        var result = new GenerateAudioResult(
            Guid.NewGuid(),
            Artifact(" "),
            Artifact("audio/music.wav"),
            Artifact("audio/master.wav"),
            false,
            false,
            false);

        var exception = Assert.Throws<Application.Jobs.JobExecutionException>(
            () => GenerateAudioResult.Deserialize(result.Serialize()));

        Assert.Equal("audio_result_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Payload_DefaultsVoiceAndLocale()
    {
        var projectId = Guid.NewGuid();
        var storyboardJobId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(
            new GenerateAudioJobPayload(projectId, storyboardJobId),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var payload = GenerateAudioJobPayload.Deserialize(json);

        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(storyboardJobId, payload.StoryboardJobId);
        Assert.Equal("Formal", payload.VoiceProfile);
        Assert.Equal("id-ID", payload.Locale);
    }

    [Fact]
    public void Payload_RejectsEmptyStoryboardJobId()
    {
        var json = JsonSerializer.Serialize(
            new { contentProjectId = Guid.NewGuid(), storyboardJobId = Guid.Empty },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var exception = Assert.Throws<Application.Jobs.JobExecutionException>(
            () => GenerateAudioJobPayload.Deserialize(json));

        Assert.Equal("audio_job_invalid_payload", exception.ErrorCode);
    }

    [Fact]
    public void JobResponse_ExposesCompletedGenerateAudioResult()
    {
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var result = new GenerateAudioResult(
            Guid.NewGuid(),
            Artifact("audio/narration.wav"),
            Artifact("audio/music.wav"),
            Artifact("audio/master.wav"),
            NarrationReused: true,
            MusicReused: false,
            MasterReused: true);
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.GenerateAudio,
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
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("GenerateAudio", json.GetProperty("type").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());
        var audioResult = json.GetProperty("result");
        Assert.True(audioResult.GetProperty("masterReused").GetBoolean());
        Assert.Equal(
            "audio/master.wav",
            audioResult.GetProperty("master").GetProperty("path").GetString());
    }

    private static GenerateAudioArtifact Artifact(string path) =>
        new(path, new string('a', 64), 1_024, 6.5, 48000, 2);
}