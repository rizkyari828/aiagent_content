namespace AIStudio.Application.Content;

public interface IContentProjectReader
{
    Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
}
