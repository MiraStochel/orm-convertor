namespace ORMConvertorAPI.Dtos;

/// <summary>
/// Files to pack into a ZIP archive, named by the client (decision 033). The endpoint
/// packs them verbatim and translates nothing; packing lives on the server because the
/// frontend takes no dependency that would need a package manager (decision 032).
/// </summary>
internal record ArchiveRequest(List<ArchiveFile> Files);

internal record ArchiveFile(string Name, string Content);
