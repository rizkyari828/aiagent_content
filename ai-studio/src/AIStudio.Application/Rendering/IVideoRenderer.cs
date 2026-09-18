namespace AIStudio.Application.Rendering;

public interface IVideoRenderer
{
    Task<VideoRenderOutput> RenderAsync(
        VideoRenderRequest request,
        CancellationToken cancellationToken);
}
