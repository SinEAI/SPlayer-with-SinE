using System.Globalization;
using System.Runtime.InteropServices;

namespace SPlayer;

public sealed record MediaTrack(
    int Id,
    string Kind,
    string Title,
    string Language,
    string Codec,
    bool Selected,
    bool AlbumArt,
    bool External,
    int Channels,
    int Width,
    int Height);

public sealed record PlaybackClock
{
    public double Position { get; init; }
    public double Duration { get; init; }
    public double Volume { get; init; } = 100;
    public double Speed { get; init; } = 1;
    public bool Paused { get; init; } = true;
    public bool Muted { get; init; }
    public bool Seeking { get; init; }
    public bool Eof { get; init; }
    public bool Idle { get; init; } = true;
}

public sealed class PlaybackFacts
{
    public string Path { get; init; } = "";
    public string MediaTitle { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public bool FileLoaded { get; init; }
    public bool HasVideo { get; init; }
    public bool AlbumArtOnly { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public string VideoCodec { get; init; } = "";
    public string AudioCodec { get; init; } = "";
    public string PixelFormat { get; init; } = "";
    public string Primaries { get; init; } = "";
    public string Gamma { get; init; } = "";
    public string Container { get; init; } = "";
    public string Hwdec { get; init; } = "";
    public string ChannelLayout { get; init; } = "";
    public int SampleRate { get; init; }
    public int AudioId { get; init; }
    public int SubtitleId { get; init; }
    public bool SubtitlesVisible { get; init; } = true;
    public IReadOnlyList<MediaTrack> Tracks { get; init; } = [];
    public string? ErrorTitle { get; init; }
    public string? ErrorSummary { get; init; }
    public string? ErrorDetail { get; init; }
    public string LogTail { get; init; } = "";
    public int LoadEpoch { get; init; }
    public int RestartEpoch { get; init; }
}

/// <summary>
/// One libmpv instance. Property snapshots are safe to read from the UI thread.
/// <see cref="Changed"/> is raised on the internal event thread; marshal before touching UI.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private readonly object _gate = new();
    private IntPtr _handle;
    private Thread? _thread;
    private volatile bool _running;
    private bool _started;
    private readonly bool _render;
    private PlaybackClock _clock = new();
    private PlaybackFacts _facts = new();
    private readonly List<string> _log = [];
    private string _path = "";
    private int _loadEpoch;
    private int _restartEpoch;

    /// <summary>Raised on the mpv thread with the facts and clock captured for that event.</summary>
    public event Action<PlaybackFacts, PlaybackClock>? Changed;

    private MpvPlayer(bool render) => _render = render;

    public static MpvPlayer Create(bool render)
    {
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"));
        var player = new MpvPlayer(render) { _handle = MpvNative.mpv_create() };
        if (player._handle == IntPtr.Zero) throw new InvalidOperationException("mpv_create 失败。");
        player.ApplyDefaults();
        return player;
    }

    public void SetOption(string name, string value)
    {
        var code = MpvNative.SetOption(_handle, name, value);
        if (code == -5)
        {
            Log.Write("忽略未知选项 " + name);
            return;
        }
        if (code < 0) throw new InvalidOperationException($"无法设置 {name}={value}：{MpvNative.ErrorString(code)}");
    }

    public void Start()
    {
        if (_started) return;
        var code = MpvNative.mpv_initialize(_handle);
        if (code < 0) throw new InvalidOperationException("mpv 初始化失败：" + MpvNative.ErrorString(code));
        _started = true;
        Observe(1, "time-pos", MpvNative.Double);
        Observe(2, "duration", MpvNative.Double);
        Observe(3, "pause", MpvNative.Flag);
        Observe(4, "volume", MpvNative.Double);
        Observe(5, "mute", MpvNative.Flag);
        Observe(6, "speed", MpvNative.Double);
        Observe(7, "eof-reached", MpvNative.Flag);
        Observe(8, "seeking", MpvNative.Flag);
        Observe(9, "idle-active", MpvNative.Flag);
        Observe(10, "track-list", MpvNative.Node);
        Observe(11, "sub-visibility", MpvNative.Flag);
        var level = Utf8.Alloc("warn");
        try { MpvNative.mpv_request_log_messages(_handle, level); }
        finally { Marshal.FreeHGlobal(level); }
        _running = true;
        _thread = new Thread(EventLoop) { IsBackground = true, Name = "SPlayer.mpv" };
        _thread.Start();
    }

    public MpvRenderContext AttachRender(IntPtr device)
    {
        if (!_started) throw new InvalidOperationException("播放内核尚未初始化。");
        return MpvRenderContext.Create(_handle, device);
    }

    public void Load(string path)
    {
        _path = path;
        lock (_gate)
        {
            _facts = new PlaybackFacts { Path = path, LogTail = Tail() };
            _clock = new PlaybackClock { Volume = _clock.Volume, Muted = _clock.Muted, Speed = _clock.Speed, Paused = true };
        }
        Raise();
        Command("loadfile", path, "replace");
    }

    public int Command(params string[] args)
    {
        var code = MpvNative.Command(_handle, args);
        if (code < 0) Log.Write("命令失败 " + string.Join(" ", args) + "：" + MpvNative.ErrorString(code));
        return code;
    }

    public void SetFlag(string name, bool value)
    {
        var memory = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(memory, value ? 1 : 0);
            var code = MpvNative.Set(_handle, name, MpvNative.Flag, memory);
            if (code < 0) Log.Write($"设置 {name} 失败：{MpvNative.ErrorString(code)}");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void SetDouble(string name, double value)
    {
        var memory = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(value, memory, false);
            var code = MpvNative.Set(_handle, name, MpvNative.Double, memory);
            if (code < 0) Log.Write($"设置 {name} 失败：{MpvNative.ErrorString(code)}");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void SetString(string name, string value)
    {
        var memory = Marshal.AllocHGlobal(IntPtr.Size);
        var text = Utf8.Alloc(value);
        try
        {
            Marshal.WriteIntPtr(memory, text);
            var code = MpvNative.Set(_handle, name, MpvNative.String, memory);
            if (code < 0) Log.Write($"设置 {name} 失败：{MpvNative.ErrorString(code)}");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(text);
        }
    }

    public double GetDouble(string name, double fallback = 0)
    {
        var memory = Marshal.AllocHGlobal(8);
        try
        {
            if (MpvNative.Get(_handle, name, MpvNative.Double, memory) < 0) return fallback;
            return Marshal.PtrToStructure<double>(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public bool GetFlag(string name, bool fallback = false)
    {
        var memory = Marshal.AllocHGlobal(4);
        try
        {
            if (MpvNative.Get(_handle, name, MpvNative.Flag, memory) < 0) return fallback;
            return Marshal.ReadInt32(memory) != 0;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public string? GetString(string name)
    {
        var memory = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (MpvNative.Get(_handle, name, MpvNative.String, memory) < 0) return null;
            var pointer = Marshal.ReadIntPtr(memory);
            if (pointer == IntPtr.Zero) return null;
            try { return Utf8.From(pointer); }
            finally { MpvNative.mpv_free(pointer); }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public PlaybackClock ReadClock()
    {
        lock (_gate) return _clock;
    }

    public PlaybackFacts ReadFacts()
    {
        lock (_gate) return _facts;
    }

    public bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var end = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < end)
        {
            if (predicate()) return true;
            Thread.Sleep(20);
        }
        return predicate();
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join();
        if (_handle != IntPtr.Zero)
        {
            MpvNative.mpv_terminate_destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void ApplyDefaults()
    {
        SetOption("config", "no");
        SetOption("load-scripts", "no");
        SetOption("ytdl", "no");
        SetOption("osc", "no");
        SetOption("osd-level", "0");
        SetOption("input-default-bindings", "no");
        SetOption("input-builtin-bindings", "no");
        SetOption("input-vo-keyboard", "no");
        SetOption("input-media-keys", "no");
        SetOption("idle", "yes");
        SetOption("keep-open", "yes");
        SetOption("keep-open-pause", "yes");
        SetOption("force-window", "no");
        SetOption("pause", "yes");
        SetOption("volume-max", "100");
        SetOption("sub-auto", "fuzzy");
        SetOption("audio-file-auto", "fuzzy");
        SetOption("sub-codepage", "auto");
        SetOption("embeddedfonts", "yes");
        SetOption("sub-ass-override", "scale");
        SetOption("slang", "zh,chi,zho,zh-Hans,zh-CN,sc,chs,en,eng");
        SetOption("subs-with-matching-audio", "no");
        SetOption("sid", "auto");
        SetOption("aid", "auto");
        SetOption("audio-channels", "auto-safe");
        SetOption("audio-fallback-to-null", "yes");
        SetOption("cache", "yes");
        SetOption("network-timeout", "15");
        SetOption("hr-seek", "yes");
        SetOption("reset-on-next-file", "sub-delay,audio-delay,video-rotate,video-aspect-override,panscan,video-unscaled,video-zoom");
        SetOption("screenshot-format", "png");
        SetOption("tone-mapping", "auto");
        SetOption("target-colorspace-hint", "no");
        SetOption("target-prim", "bt.709");
        SetOption("target-trc", "gamma2.2");
        SetOption("hdr-compute-peak", "yes");
        SetOption("dither-depth", "auto");
        SetOption("vd-lavc-dr", "yes");
        if (_render)
        {
            SetOption("scale", "ewa_lanczossharp");
            SetOption("cscale", "ewa_lanczossharp");
            SetOption("dscale", "mitchell");
            SetOption("correct-downscaling", "yes");
            SetOption("linear-downscaling", "yes");
            SetOption("sigmoid-upscaling", "yes");
        }
        SetOption("hwdec", _render ? "auto-safe" : "no");
        if (_render)
        {
            // The render context is attached before any file loads. Without this,
            // mpv opens its own window on the first video.
            SetOption("vo", "libmpv");
            SetOption("ao", "wasapi");
            SetOption("video-sync", "audio");
        }
        else
        {
            SetOption("vo", "null");
            SetOption("ao", "null");
        }
    }

    private void Observe(ulong id, string name, int format)
    {
        var code = MpvNative.Observe(_handle, id, name, format);
        if (code < 0) Log.Write($"无法观察 {name}：{MpvNative.ErrorString(code)}");
    }

    private void EventLoop()
    {
        while (_running)
        {
            var pointer = MpvNative.mpv_wait_event(_handle, 0.1);
            if (pointer == IntPtr.Zero) continue;
            var ev = Marshal.PtrToStructure<MpvEvent>(pointer);
            if (ev.Id == MpvNative.EventNone) continue;
            try { Handle(ev); }
            catch (Exception ex) { Log.Write(ex.ToString()); }
        }
    }

    private void Handle(MpvEvent ev)
    {
        switch (ev.Id)
        {
            case MpvNative.EventShutdown:
                _running = false;
                break;
            case MpvNative.EventLog:
                var log = Marshal.PtrToStructure<MpvEventLog>(ev.Data);
                Remember(Utf8.From(log.Prefix) + "/" + Utf8.From(log.Level).Trim() + ": " + Utf8.From(log.Text).Trim());
                break;
            case MpvNative.EventFileLoaded:
                _loadEpoch++;
                RefreshFacts(clearError: true, loaded: true);
                Raise();
                break;
            case MpvNative.EventPlaybackRestart:
                _restartEpoch++;
                RefreshFacts(clearError: false, loaded: true);
                Raise();
                break;
            case MpvNative.EventSeek:
                Raise();
                break;
            case MpvNative.EventEndFile:
                var end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                if (end.Reason == MpvNative.EndError) RememberFailure(end.Error);
                Raise();
                break;
            case MpvNative.EventProperty:
                ApplyProperty(Marshal.PtrToStructure<MpvEventProperty>(ev.Data), ev.Reply);
                break;
        }
    }

    private void ApplyProperty(MpvEventProperty property, ulong id)
    {
        if (property.Format == MpvNative.None)
        {
            if (id == 1) UpdateClock(c => c with { Position = 0 });
            return;
        }
        switch (id)
        {
            case 1: UpdateClock(c => c with { Position = ReadDouble(property.Data) }); break;
            case 2: UpdateClock(c => c with { Duration = ReadDouble(property.Data) }); break;
            case 3:
                UpdateClock(c => c with { Paused = ReadFlag(property.Data) });
                Raise();
                break;
            case 4: UpdateClock(c => c with { Volume = ReadDouble(property.Data) }); break;
            case 5:
                UpdateClock(c => c with { Muted = ReadFlag(property.Data) });
                Raise();
                break;
            case 6: UpdateClock(c => c with { Speed = ReadDouble(property.Data) }); break;
            case 7:
                UpdateClock(c => c with { Eof = ReadFlag(property.Data) });
                Raise();
                break;
            case 8: UpdateClock(c => c with { Seeking = ReadFlag(property.Data) }); break;
            case 9: UpdateClock(c => c with { Idle = ReadFlag(property.Data) }); break;
            case 11:
                RefreshFacts(clearError: false, loaded: false);
                Raise();
                break;
            case 10:
                if (property.Format == MpvNative.Node)
                {
                    var tracks = ReadTracks(Marshal.PtrToStructure<MpvNode>(property.Data));
                    lock (_gate) _facts = CopyFacts(tracks);
                    Raise();
                }
                break;
        }
    }

    private void UpdateClock(Func<PlaybackClock, PlaybackClock> change)
    {
        lock (_gate) _clock = change(_clock);
    }

    private void RefreshFacts(bool clearError, bool loaded)
    {
        var tracks = ReadTrackList();
        var title = GetString("media-title") ?? "";
        var artist = GetString("metadata/by-key/artist") ?? GetString("metadata/by-key/Artist") ?? "";
        var album = GetString("metadata/by-key/album") ?? GetString("metadata/by-key/Album") ?? "";
        var video = tracks.Where(t => t.Kind == "video" && !t.AlbumArt).ToArray();
        var art = tracks.Where(t => t.Kind == "video" && t.AlbumArt).ToArray();
        var selectedSub = tracks.FirstOrDefault(t => t.Kind == "sub" && t.Selected);
        var selectedAudio = tracks.FirstOrDefault(t => t.Kind == "audio" && t.Selected);
        lock (_gate)
        {
            _facts = new PlaybackFacts
            {
                Path = _path,
                MediaTitle = title,
                Artist = artist,
                Album = album,
                FileLoaded = loaded || _facts.FileLoaded,
                HasVideo = video.Length > 0,
                AlbumArtOnly = video.Length == 0 && art.Length > 0,
                Width = (int)GetDouble("width"),
                Height = (int)GetDouble("height"),
                Fps = GetDouble("container-fps"),
                VideoCodec = GetString("video-codec") ?? "",
                AudioCodec = GetString("audio-codec") ?? "",
                PixelFormat = GetString("video-params/pixelformat") ?? "",
                Primaries = GetString("video-params/primaries") ?? "",
                Gamma = GetString("video-params/gamma") ?? "",
                Container = GetString("file-format") ?? "",
                Hwdec = GetString("hwdec-current") ?? "",
                ChannelLayout = GetString("audio-params/hr-channels") ?? "",
                SampleRate = (int)GetDouble("audio-params/samplerate"),
                AudioId = selectedAudio?.Id ?? 0,
                SubtitleId = selectedSub?.Id ?? 0,
                SubtitlesVisible = GetFlag("sub-visibility", true),
                Tracks = tracks,
                ErrorTitle = clearError ? null : _facts.ErrorTitle,
                ErrorSummary = clearError ? null : _facts.ErrorSummary,
                ErrorDetail = clearError ? null : _facts.ErrorDetail,
                LogTail = Tail(),
                LoadEpoch = _loadEpoch,
                RestartEpoch = _restartEpoch,
            };
        }
    }

    private PlaybackFacts CopyFacts(IReadOnlyList<MediaTrack> tracks)
    {
        var video = tracks.Where(t => t.Kind == "video" && !t.AlbumArt).ToArray();
        var art = tracks.Where(t => t.Kind == "video" && t.AlbumArt).ToArray();
        var selectedSub = tracks.FirstOrDefault(t => t.Kind == "sub" && t.Selected);
        var selectedAudio = tracks.FirstOrDefault(t => t.Kind == "audio" && t.Selected);
        return new PlaybackFacts
        {
            Path = _facts.Path,
            MediaTitle = _facts.MediaTitle,
            Artist = _facts.Artist,
            Album = _facts.Album,
            FileLoaded = _facts.FileLoaded,
            HasVideo = video.Length > 0,
            AlbumArtOnly = video.Length == 0 && art.Length > 0,
            Width = video.FirstOrDefault(t => t.Selected)?.Width ?? _facts.Width,
            Height = video.FirstOrDefault(t => t.Selected)?.Height ?? _facts.Height,
            Fps = _facts.Fps,
            VideoCodec = _facts.VideoCodec,
            AudioCodec = _facts.AudioCodec,
            PixelFormat = _facts.PixelFormat,
            Primaries = _facts.Primaries,
            Gamma = _facts.Gamma,
            Container = _facts.Container,
            Hwdec = _facts.Hwdec,
            ChannelLayout = _facts.ChannelLayout,
            SampleRate = selectedAudio?.Channels > 0 ? _facts.SampleRate : _facts.SampleRate,
            AudioId = selectedAudio?.Id ?? 0,
            SubtitleId = selectedSub?.Id ?? 0,
            SubtitlesVisible = _facts.SubtitlesVisible,
            Tracks = tracks,
            ErrorTitle = _facts.ErrorTitle,
            ErrorSummary = _facts.ErrorSummary,
            ErrorDetail = _facts.ErrorDetail,
            LogTail = Tail(),
            LoadEpoch = _facts.LoadEpoch,
            RestartEpoch = _facts.RestartEpoch,
        };
    }

    private void RememberFailure(int code)
    {
        var title = PlaybackError.Title(code, _path);
        var summary = PlaybackError.Summary(code, _path);
        var detail = Log.Redact(MpvNative.ErrorString(code) + Environment.NewLine + Tail());
        lock (_gate)
        {
            _facts = new PlaybackFacts
            {
                Path = _path,
                MediaTitle = _facts.MediaTitle,
                FileLoaded = false,
                Tracks = _facts.Tracks,
                ErrorTitle = title,
                ErrorSummary = summary,
                ErrorDetail = detail,
                LogTail = Tail(),
                LoadEpoch = _loadEpoch,
                RestartEpoch = _restartEpoch,
            };
        }
        Log.Write(title + " " + detail);
    }

    private List<MediaTrack> ReadTrackList()
    {
        var memory = Marshal.AllocHGlobal(16);
        try
        {
            if (MpvNative.Get(_handle, "track-list", MpvNative.Node, memory) < 0) return [];
            try { return ReadTracks(Marshal.PtrToStructure<MpvNode>(memory)); }
            finally { MpvNative.mpv_free_node_contents(memory); }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static List<MediaTrack> ReadTracks(MpvNode node)
    {
        var tracks = new List<MediaTrack>();
        if (node.Format != MpvNative.NodeArray || node.List == IntPtr.Zero) return tracks;
        var list = Marshal.PtrToStructure<MpvNodeList>(node.List);
        for (var i = 0; i < list.Num; i++)
        {
            var item = Marshal.PtrToStructure<MpvNode>(list.Values + i * 16);
            if (item.Format != MpvNative.NodeMap) continue;
            var map = ReadMap(item);
            tracks.Add(new MediaTrack(
                MapInt(map, "id"),
                MapString(map, "type"),
                MapString(map, "title"),
                MapString(map, "lang"),
                MapString(map, "codec"),
                MapBool(map, "selected"),
                MapBool(map, "albumart"),
                MapBool(map, "external"),
                MapInt(map, "demux-channel-count"),
                MapInt(map, "demux-w"),
                MapInt(map, "demux-h")));
        }
        return tracks;
    }

    private static Dictionary<string, MpvNode> ReadMap(MpvNode node)
    {
        var result = new Dictionary<string, MpvNode>(StringComparer.Ordinal);
        if (node.List == IntPtr.Zero) return result;
        var list = Marshal.PtrToStructure<MpvNodeList>(node.List);
        for (var i = 0; i < list.Num; i++)
        {
            var key = Utf8.From(Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size));
            result[key] = Marshal.PtrToStructure<MpvNode>(list.Values + i * 16);
        }
        return result;
    }

    private static string MapString(Dictionary<string, MpvNode> map, string key)
    {
        if (!map.TryGetValue(key, out var node) || node.Format != MpvNative.String) return "";
        return Utf8.From(node.String);
    }

    private static int MapInt(Dictionary<string, MpvNode> map, string key)
    {
        if (!map.TryGetValue(key, out var node)) return 0;
        return node.Format switch
        {
            MpvNative.Int64 => (int)node.Int64,
            MpvNative.Double => (int)node.Double,
            MpvNative.Flag => node.Flag,
            _ => 0,
        };
    }

    private static bool MapBool(Dictionary<string, MpvNode> map, string key)
    {
        if (!map.TryGetValue(key, out var node)) return false;
        return node.Format switch
        {
            MpvNative.Flag => node.Flag != 0,
            MpvNative.Int64 => node.Int64 != 0,
            _ => false,
        };
    }

    private static double ReadDouble(IntPtr data) => data == IntPtr.Zero ? 0 : Marshal.PtrToStructure<double>(data);
    private static bool ReadFlag(IntPtr data) => data != IntPtr.Zero && Marshal.ReadInt32(data) != 0;

    private void Remember(string line)
    {
        lock (_gate)
        {
            _log.Add(line);
            if (_log.Count > 80) _log.RemoveAt(0);
        }
        Log.Write(line);
    }

    private string Tail()
    {
        lock (_gate) return string.Join(Environment.NewLine, _log.TakeLast(12));
    }

    private void Raise()
    {
        PlaybackFacts facts;
        PlaybackClock clock;
        lock (_gate)
        {
            facts = _facts;
            clock = _clock;
        }
        try { Changed?.Invoke(facts, clock); }
        catch (Exception ex) { Log.Write(ex.ToString()); }
    }

    public static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
