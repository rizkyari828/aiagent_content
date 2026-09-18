namespace AIStudio.Infrastructure.Assets;

public sealed class AssetStorageOptions
{
    public const string SectionName = "Assets";

    public string RootPath { get; set; } = "assets";
}
