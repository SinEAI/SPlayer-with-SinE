namespace SPlayer;

/// <summary>File kinds the player will try. Extensions only choose the picker and folder scan; decoding is decided by the media core.</summary>
public static class MediaPaths
{
    public static readonly string[] VideoExtensions =
    [
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".asf", ".flv", ".f4v",
        ".mpeg", ".mpg", ".mpe", ".ts", ".mts", ".m2ts", ".m2t", ".vob", ".3gp", ".3g2",
        ".ogv", ".ogg", ".rm", ".rmvb", ".mxf", ".divx", ".wtv", ".dat", ".tod", ".mod",
    ];

    public static readonly string[] AudioExtensions =
    [
        ".mp3", ".aac", ".m4a", ".flac", ".wav", ".wave", ".ogg", ".opus", ".wma", ".ac3",
        ".eac3", ".dts", ".aiff", ".aif", ".alac", ".ape", ".wv", ".tta", ".mka", ".mp2",
        ".caf", ".oga",
    ];

    public static readonly string[] SubtitleExtensions =
    [
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".txt",
    ];

    public static bool Same(string a, string b) => string.Equals(a, b,
        IsHttp(a) || IsHttp(b) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    // Signed URLs remain usable in memory, but are not written to history or queues.
    public static bool CanPersist(string path) => !IsHttp(path) ||
        (Uri.TryCreate(path, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.UserInfo) &&
         string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment));

    public static bool IsVideo(string path) => Has(path, VideoExtensions);
    public static bool IsAudio(string path) => Has(path, AudioExtensions);
    public static bool IsSubtitle(string path) => Has(path, SubtitleExtensions);
    public static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

    public static bool IsHttp(string path)
    {
        if (!Uri.TryCreate(path.Trim(), UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);

    public static string DisplayName(string path)
    {
        if (IsHttp(path))
        {
            if (Uri.TryCreate(path.Trim(), UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(name)) return Uri.UnescapeDataString(name);
                return uri.Host;
            }
        }
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (Exception)
        {
            return path;
        }
    }

    public static IReadOnlyList<string> MediaInFolder(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(IsMedia)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Videos beside a file opened from Explorer. Audio-only files in that folder stay out.
    /// Returns false for audio, web addresses, and folders that cannot be listed.
    /// </summary>
    public static bool TrySiblingVideos(string path, out IReadOnlyList<string> files, out int index)
    {
        files = [];
        index = 0;
        if (!IsVideo(path) || IsHttp(path)) return false;
        string full;
        string? folder;
        try
        {
            full = Path.GetFullPath(path);
            folder = Path.GetDirectoryName(full);
        }
        catch (Exception)
        {
            return false;
        }
        if (string.IsNullOrEmpty(folder)) return false;
        string[] listed;
        try
        {
            listed = Directory.EnumerateFiles(folder)
                .Where(IsVideo)
                .Select(Path.GetFullPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return false;
        }
        var at = Array.FindIndex(listed, p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return false;
        files = listed;
        index = at;
        return true;
    }

    /// <summary>External subtitles that belong to a media file: same base name, optionally with a language tag.</summary>
    public static IReadOnlyList<string> MatchingSubtitles(string mediaPath)
    {
        string? folder;
        string name;
        try
        {
            folder = Path.GetDirectoryName(mediaPath);
            name = Path.GetFileNameWithoutExtension(mediaPath);
        }
        catch (Exception)
        {
            return [];
        }
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder)) return [];
        var prefix = name + ".";
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => IsSubtitle(f) && Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool Has(string path, string[] extensions)
    {
        string ext;
        try { ext = Path.GetExtension(path); }
        catch (Exception) { return false; }
        if (ext.Length == 0) return false;
        foreach (var candidate in extensions)
        {
            if (ext.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
