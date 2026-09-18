namespace AIStudio.Application.Assets;

public interface IAssetFileStore
{
    AssetFileInfo Register(string relativePath);
}

public sealed record AssetFileInfo(string RelativePath, long ByteSize, string ContentHash);
