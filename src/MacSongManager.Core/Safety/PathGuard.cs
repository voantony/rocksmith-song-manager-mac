namespace MacSongManager.Core.Safety;

/// <summary>
/// Resolves a path and decides whether it stays inside a root.
/// Symlinks are followed so a link inside the library that points elsewhere
/// is rejected. Song files are never deleted by this application.
/// </summary>
public static class PathGuard
{
    public static StringComparison PathComparison =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        return Resolve(full, new HashSet<string>(StringComparer.Ordinal), 0);
    }

    public static bool IsStrictlyInside(string root, string candidate)
    {
        try
        {
            var rootReal = RealPath(root);
            var candidateReal = RealPath(candidate);
            var prefix = rootReal.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidateReal.StartsWith(prefix, PathComparison);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool SamePath(string left, string right)
    {
        var a = Path.GetFullPath(left);
        var b = Path.GetFullPath(right);
        return string.Equals(a, b, PathComparison);
    }

    private static string Resolve(string fullPath, HashSet<string> seen, int depth)
    {
        if (depth > 32)
            throw new IOException($"Too many symlinks while resolving {fullPath}.");

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return fullPath;

        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        if (relative is "." or "")
            return current;

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                var parent = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
                current = string.IsNullOrEmpty(parent) ? root : parent;
                continue;
            }

            var next = Path.Combine(current, segment);
            if (File.Exists(next) || Directory.Exists(next))
            {
                var link = LinkTarget(next);
                if (!string.IsNullOrEmpty(link))
                {
                    if (!seen.Add(next))
                        throw new IOException($"Symlink cycle at {next}.");
                    var target = Path.IsPathRooted(link)
                        ? Path.GetFullPath(link)
                        : Path.GetFullPath(Path.Combine(current, link));
                    next = Resolve(target, seen, depth + 1);
                }
            }

            current = next;
        }

        return current;
    }

    private static string? LinkTarget(string path)
    {
        FileSystemInfo info = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        return info.LinkTarget;
    }
}
