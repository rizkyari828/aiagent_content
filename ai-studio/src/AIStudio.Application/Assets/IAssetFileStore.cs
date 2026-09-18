namespace AIStudio.Application.Assets;

public interface IAssetFileStore
{
    AssetFileInfo Register(string relativePath);
}

public sealed record AssetFileInfo(
    string RelativePath,
    string AbsolutePath,
    long ByteSize,
    string ContentHash);
