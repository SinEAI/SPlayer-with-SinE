using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPlayer;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Store = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

public static class AtomicFile
{
    // Readers keep a snapshot of the old file while a writer atomically replaces its name.
    public static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static string Read(string path)
    {
        using var reader = new StreamReader(OpenRead(path));
        return reader.ReadToEnd();
    }

    public static void PreserveUnreadable(string path)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), overwrite: false); }
        catch (Exception ex) { Log.Write("无法备份损坏的数据文件：" + ex.Message); }
    }

    public static void Locked(string path, Action action)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\SPlayer-" + key);
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("数据正在由另一个窗口保存，请稍后重试。");
            action();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    public static void Write(string path, string text)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class HistoryEntry
{
    public string Path { get; set; } = "";
    public double Position { get; set; }
    public double Duration { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
    public bool Completed { get; set; }
    public string Title { get; set; } = "";
}

public static class ResumePolicy
{
    public static bool ShouldResume(HistoryEntry entry)
    {
        if (entry.Completed) return false;
        if (entry.Position < 3) return false;
        if (entry.Duration > 0 && entry.Position >= entry.Duration - 2) return false;
        return true;
    }

    public static bool IsCompleted(double position, double duration)
    {
        if (duration <= 0 || position < 0) return false;
        if (duration < 15) return position >= duration - 0.5;
        return position >= duration - 10 || position / duration >= 0.98;
    }
}

public sealed class HistoryStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private List<HistoryEntry> _entries;

    public HistoryStore(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_gate) return _entries.OrderByDescending(e => e.PlayedAt).ToArray(); }
    }

    public HistoryEntry? Find(string path)
    {
        lock (_gate) return _entries.FirstOrDefault(e => MediaPaths.Same(e.Path, path));
    }

    public void Upsert(HistoryEntry entry)
    {
        AtomicFile.Locked(_path, () =>
        {
            var entries = Load(_path);
            var existing = entries.FindIndex(e => MediaPaths.Same(e.Path, entry.Path));
            if (existing >= 0) entries[existing] = entry;
            else entries.Add(entry);
            if (entries.Count > 200) entries = entries.OrderByDescending(e => e.PlayedAt).Take(100).ToList();
            AtomicFile.Write(_path, JsonSerializer.Serialize(entries, JsonOptions.Store));
            lock (_gate) _entries = entries;
        });
    }

    public void Remove(string path)
    {
        AtomicFile.Locked(_path, () =>
        {
            var entries = Load(_path);
            entries.RemoveAll(e => MediaPaths.Same(e.Path, path));
            AtomicFile.Write(_path, JsonSerializer.Serialize(entries, JsonOptions.Store));
            lock (_gate) _entries = entries;
        });
    }

    public void Clear()
    {
        AtomicFile.Locked(_path, () =>
        {
            AtomicFile.Write(_path, "[]");
            lock (_gate) _entries = [];
        });
    }

    private static List<HistoryEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<HistoryEntry>>(AtomicFile.Read(path), JsonOptions.Store) ?? [];
        }
        catch (JsonException)
        {
            AtomicFile.PreserveUnreadable(path);
            return [];
        }
    }
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public bool HardwareDecoding { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public bool RecordHistory { get; set; } = true;
    public bool WheelAdjustsVolume { get; set; } = true;
    public bool FitWindowToVideo { get; set; } = true;
    public int SeekSeconds { get; set; } = 5;
    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }
    public double Speed { get; set; } = 1;
    public double SubtitleScale { get; set; } = 1;
    public string SubtitleCodepage { get; set; } = "auto";
    public string AudioChannels { get; set; } = "auto-safe";
    public string ScreenshotDirectory { get; set; } = "";
    public RepeatMode Repeat { get; set; } = RepeatMode.None;
    public bool Shuffle { get; set; }
    public WindowPlacement Shell { get; set; } = new();
    public WindowPlacement Player { get; set; } = new();

    private System.Text.Json.Nodes.JsonObject? _baseline;

    public static AppSettings Load(string path)
    {
        try
        {
            var settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(AtomicFile.Read(path), JsonOptions.Store) ?? new AppSettings()
                : new AppSettings();
            settings.Volume = Math.Clamp(settings.Volume, 0, 100);
            settings.Speed = double.IsFinite(settings.Speed) ? Math.Clamp(settings.Speed, .25, 4) : 1;
            settings.SubtitleScale = double.IsFinite(settings.SubtitleScale) ? Math.Clamp(settings.SubtitleScale, .5, 3) : 1;
            settings.SeekSeconds = Math.Clamp(settings.SeekSeconds, 1, 60);
            settings.Shell ??= new();
            settings.Player ??= new();
            settings._baseline = JsonSerializer.SerializeToNode(settings, JsonOptions.Store)!.AsObject();
            return settings;
        }
        catch (JsonException)
        {
            AtomicFile.PreserveUnreadable(path);
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        AtomicFile.Locked(path, () =>
        {
            var current = JsonSerializer.SerializeToNode(this, JsonOptions.Store)!.AsObject();
            var merged = JsonSerializer.SerializeToNode(Load(path), JsonOptions.Store)!.AsObject();
            foreach (var item in current)
                if (_baseline is null || !System.Text.Json.Nodes.JsonNode.DeepEquals(item.Value, _baseline[item.Key]))
                    merged[item.Key] = item.Value?.DeepClone();
            AtomicFile.Write(path, merged.ToJsonString(JsonOptions.Store));
            _baseline = current;
        });
    }
}

public static class AppPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("SPLAYER_DATA");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinE", "SPlayer");
    }

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string HistoryPath => Path.Combine(DataDirectory, "history.json");
    public static string PlaylistPath => Path.Combine(DataDirectory, "playlist.json");
    public static string LogPath => Path.Combine(DataDirectory, "player.log");

    public static string DefaultScreenshots()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures)) pictures = DataDirectory;
        return Path.Combine(pictures, "SPlayer");
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static string Redact(string message) => System.Text.RegularExpressions.Regex.Replace(
        message, @"https?://[^\s<>""']+", match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[网络地址]";
            return uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", "") + "/[媒体地址]";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static void Write(string message)
    {
        try
        {
            var line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + Redact(message) + Environment.NewLine;
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(AppPaths.LogPath, line);
                var info = new FileInfo(AppPaths.LogPath);
                if (info.Length > 1_000_000)
                {
                    var text = File.ReadAllText(AppPaths.LogPath);
                    File.WriteAllText(AppPaths.LogPath, text[^400_000..]);
                }
            }
        }
        catch (Exception)
        {
            // A log failure must not take down playback.
        }
    }
}
