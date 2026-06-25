using System.IO.Compression;
using System.Security.Cryptography;

namespace QuickDrop.Core;

public sealed class PreparedPackage : IAsyncDisposable
{
    public required string ArchivePath { get; init; }

    public required string PackageName { get; init; }

    public required string ExtractMode { get; init; }

    public required int ItemCount { get; init; }

    public required long ArchiveBytes { get; init; }

    public required string ArchiveSha256 { get; init; }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(ArchivePath))
            {
                File.Delete(ArchivePath);
            }
        }
        catch
        {
            // Temp cleanup can wait.
        }

        return ValueTask.CompletedTask;
    }
}

public static class FilePackaging
{
    public static async Task<PreparedPackage> CreateAsync(IReadOnlyList<string> selectedPaths, CancellationToken cancellationToken)
    {
        if (selectedPaths.Count == 0)
        {
            throw new ArgumentException("No files or folders were selected.", nameof(selectedPaths));
        }

        QuickDropPaths.EnsureDirectories();
        var roots = selectedPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(root) && !Directory.Exists(root))
            {
                throw new FileNotFoundException("Selected path does not exist.", root);
            }
        }

        var archivePath = Path.Combine(QuickDropPaths.TempDirectory, $"quickdrop-{Guid.NewGuid():N}.zip");
        var rootNames = BuildUniqueRootNames(roots);
        var extractMode = GetExtractMode(roots);
        var packageName = GetPackageName(roots, extractMode);

        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var topName = rootNames[root];
                if (File.Exists(root))
                {
                    AddFile(archive, root, topName);
                }
                else
                {
                    AddDirectory(archive, root, topName, cancellationToken);
                }
            }
        }

        var info = new FileInfo(archivePath);
        var hash = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
        return new PreparedPackage
        {
            ArchivePath = archivePath,
            PackageName = packageName,
            ExtractMode = extractMode,
            ItemCount = roots.Length,
            ArchiveBytes = info.Length,
            ArchiveSha256 = hash
        };
    }

    public static async Task<string> ExtractAsync(string archivePath, ProtocolHeader header, CancellationToken cancellationToken)
    {
        QuickDropPaths.EnsureDirectories();
        var downloads = Path.GetFullPath(QuickDropPaths.DownloadsDirectory);
        using var archive = ZipFile.OpenRead(archivePath);

        if (header.ExtractMode == "SingleFile")
        {
            var fileEntry = archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name))
                ?? throw new InvalidDataException("Archive contains no file entries.");
            var destination = GetAvailableFilePath(Path.Combine(downloads, fileEntry.Name));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            fileEntry.ExtractToFile(destination);
            File.SetLastWriteTime(destination, fileEntry.LastWriteTime.LocalDateTime);
            return destination;
        }

        var baseDirectory = header.ExtractMode == "SingleFolder"
            ? downloads
            : GetAvailableDirectoryPath(Path.Combine(downloads, $"QuickDrop-{DateTime.Now:yyyyMMdd-HHmmss}"));

        string? singleFolderName = null;
        string? renamedSingleFolder = null;
        if (header.ExtractMode == "SingleFolder")
        {
            singleFolderName = FindFirstPathComponent(archive);
            if (!string.IsNullOrWhiteSpace(singleFolderName))
            {
                renamedSingleFolder = Path.GetFileName(GetAvailableDirectoryPath(Path.Combine(downloads, singleFolderName)));
            }
        }

        Directory.CreateDirectory(baseDirectory);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeZipPath(entry.FullName);
            if (singleFolderName is not null && renamedSingleFolder is not null)
            {
                relative = ReplaceFirstPathComponent(relative, singleFolderName, renamedSingleFolder);
            }

            var destination = Path.GetFullPath(Path.Combine(baseDirectory, relative));
            if (!IsUnderDirectory(destination, baseDirectory) && !IsUnderDirectory(destination, downloads))
            {
                throw new InvalidDataException("Archive entry tried to leave the destination folder.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
            File.SetLastWriteTime(destination, entry.LastWriteTime.LocalDateTime);
        }

        return header.ExtractMode == "SingleFolder" && renamedSingleFolder is not null
            ? Path.Combine(downloads, renamedSingleFolder)
            : baseDirectory;
    }

    private static void AddDirectory(ZipArchive archive, string directoryPath, string topName, CancellationToken cancellationToken)
    {
        var rootFullPath = Path.GetFullPath(directoryPath);
        if (!Directory.EnumerateFileSystemEntries(rootFullPath).Any())
        {
            archive.CreateEntry(ToZipPath(topName) + "/");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(rootFullPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(rootFullPath, file);
            AddFile(archive, file, Path.Combine(topName, relative));
        }

        foreach (var directory in Directory.EnumerateDirectories(rootFullPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                continue;
            }

            var relative = Path.GetRelativePath(rootFullPath, directory);
            var entryName = ToZipPath(Path.Combine(topName, relative)) + "/";
            archive.CreateEntry(entryName);
        }
    }

    private static void AddFile(ZipArchive archive, string filePath, string entryName)
    {
        var entry = archive.CreateEntry(ToZipPath(entryName), CompressionLevel.Fastest);
        entry.LastWriteTime = File.GetLastWriteTime(filePath);
        using var input = File.OpenRead(filePath);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    private static string GetExtractMode(IReadOnlyList<string> roots)
    {
        if (roots.Count > 1)
        {
            return "Multiple";
        }

        return File.Exists(roots[0]) ? "SingleFile" : "SingleFolder";
    }

    private static string GetPackageName(IReadOnlyList<string> roots, string extractMode)
    {
        if (extractMode == "Multiple")
        {
            return $"QuickDrop-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        return Path.GetFileName(roots[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static Dictionary<string, string> BuildUniqueRootNames(IEnumerable<string> roots)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var baseName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Drive";
            }

            var name = baseName;
            var suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName} ({suffix++})";
            }

            result[root] = name;
        }

        return result;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ToZipPath(string path) => path.Replace('\\', '/');

    private static string NormalizeZipPath(string value)
    {
        var relative = value.Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.Split('/').Any(part => part == ".."))
        {
            throw new InvalidDataException("Archive contains an unsafe path.");
        }

        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string? FindFirstPathComponent(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeZipPath(entry.FullName);
            var separator = normalized.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
            return separator < 0 ? normalized : normalized[..separator];
        }

        return null;
    }

    private static string ReplaceFirstPathComponent(string relative, string original, string replacement)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar, 2);
        if (parts.Length == 0 || !string.Equals(parts[0], original, StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        return parts.Length == 1 ? replacement : Path.Combine(replacement, parts[1]);
    }

    private static string GetAvailableFilePath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string GetAvailableDirectoryPath(string desiredPath)
    {
        if (!Directory.Exists(desiredPath) && !File.Exists(desiredPath))
        {
            return desiredPath;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{desiredPath} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, directory, StringComparison.OrdinalIgnoreCase);
    }
}
