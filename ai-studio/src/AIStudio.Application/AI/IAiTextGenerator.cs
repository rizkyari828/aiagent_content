namespace AIStudio.Application.AI;

public interface IAiTextGenerator
{
    Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken);
}
