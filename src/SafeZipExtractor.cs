using System.IO.Compression;

namespace MadNegar;

/// <summary>ZIP extraction with explicit path-containment checks before writing any entry.</summary>
internal static class SafeZipExtractor
{
    public static void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var entryName = entry.FullName;
            if (string.IsNullOrWhiteSpace(entryName)) continue;
            if (Path.IsPathRooted(entryName) || entryName.StartsWith('\\') || entryName.StartsWith('/'))
                throw new InvalidDataException($"The ZIP contains a rooted path that is not safe to extract: {entryName}");

            var normalizedName = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, normalizedName));
            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The ZIP contains a path outside the target folder: {entryName}");

            if (entryName.EndsWith('/') || entryName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
