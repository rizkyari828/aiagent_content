namespace AIStudio.Application.Transfer;

/// <summary>
/// Hard bounds applied to a single package. They are configuration-driven so a large
/// project and a small test fixture can share one code path without magic numbers.
/// </summary>
public sealed record ProjectTransferLimits(
    long MaxFileBytes,
    long MaxPackageBytes,
    long MaxManifestBytes,
    int MaxFileCount)
{
    public static ProjectTransferLimits Default { get; } = new(
        512L * 1024 * 1024,
        2L * 1024 * 1024 * 1024,
        16L * 1024 * 1024,
        10_000);

    public void EnsureValid()
    {
        if (MaxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFileBytes),
                "The maximum file size must be positive.");
        }

        if (MaxPackageBytes < MaxFileBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPackageBytes),
                "The maximum package size cannot be smaller than the maximum file size.");
        }

        if (MaxManifestBytes is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxManifestBytes),
                "The maximum manifest size must be between 1 byte and 2 GB.");
        }

        if (MaxFileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFileCount),
                "The maximum file count must be positive.");
        }
    }
}
