using System.IO.Compression;

namespace AIStudio.Application.Transfer;

/// <summary>
/// Restores one package into a different database and approved root. v1 is intentionally
/// strict: no identifier remapping, no merge, and no overwrite. Files are written first
/// (never replacing an existing file), then all rows are inserted in one transaction; a
/// database failure removes every file this import wrote.
/// </summary>
public sealed class ProjectImportWorkflow(
    IProjectTransferRepository repository,
    IProjectTransferStore store)
{
    public async Task<ProjectImportResult> ImportAsync(
        Stream package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        return await store.WithStagedPackageAsync(
            package,
            async (staged, token) =>
            {
                ZipArchive archive;
                try
                {
                    archive = new ZipArchive(
                        staged,
                        ZipArchiveMode.Read,
                        leaveOpen: true);
                }
                catch (InvalidDataException exception)
                {
                    throw new ProjectTransferException(
                        "import_package_invalid",
                        "The uploaded package is not a valid ZIP archive.",
                        exception);
                }

                using (archive)
                {
                    var reader = new ProjectPackageReader(store.Limits);
                    ProjectPackageContents contents;
                    try
                    {
                        contents = await reader.ReadAsync(archive, token);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new ProjectTransferException(
                            "import_package_invalid",
                            "The uploaded package contains corrupt ZIP data.",
                            exception);
                    }

                    var projectId = contents.Manifest.Project.Id;

                    if (await repository.ExistsAsync(projectId, token))
                    {
                        throw new ProjectTransferException(
                            "import_project_conflict",
                            $"Content project '{projectId}' already exists; "
                            + "v1 never overwrites or merges.");
                    }

                    var written = new List<string>(contents.Files.Count);
                    try
                    {
                        foreach (var file in contents.Files)
                        {
                            await using var source = file.Entry.Open();
                            var result = await store.WriteAssetAsync(
                                file.Path,
                                source,
                                file.ByteSize,
                                file.Sha256,
                                token);
                            written.Add(result.RelativePath);
                        }

                        var counts = await repository.ImportAsync(contents.Manifest, token);
                        return new ProjectImportResult(projectId, written.Count, counts);
                    }
                    catch (InvalidDataException exception)
                    {
                        Cleanup(written);
                        throw new ProjectTransferException(
                            "import_package_invalid",
                            "The uploaded package contains corrupt ZIP data.",
                            exception);
                    }
                    catch
                    {
                        Cleanup(written);
                        throw;
                    }
                }
            },
            cancellationToken);
    }

    private void Cleanup(List<string> written)
    {
        foreach (var path in written)
        {
            store.DeleteAsset(path);
        }
    }
}
