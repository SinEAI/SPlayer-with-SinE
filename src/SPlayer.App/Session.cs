using System.Globalization;

namespace SPlayer;

/// <summary>Playlist, history and libmpv on the UI thread. Player callbacks are posted back here.</summary>
internal sealed class Session : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly SeekCoalescer _seek = new(80);
    private readonly List<string> _pendingSubs = [];
    private int _seenLoad = -1;
    private int _seenRestart;
    private int _resumePhase;
    private double _resumeAt;
    private bool _eofLatched;
    private bool _dragging;
    private long _resumeSince;
    private long _resumeHintUntil;
    private bool _wasPlaying;
    private bool _away;
    private string? _shownError;
    private int _rotate;
    private int _aspect;
    private double? _pendingResume;
    private bool _disposed;
    private bool _historySuppressed;
    private long _checkpointAt;
    private Task _historyWrite = Task.CompletedTask;
    private readonly Dictionary<string, HistoryEntry> _recentPositions = new();
    private string? _savedPlaylist;
    internal bool Loading { get; private set; }
    internal event Action<string>? Notice;
    private bool _saving;
    private bool _saveAgain;

    internal Session()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Settings = AppSettings.Load(AppPaths.SettingsPath);
        History = new HistoryStore(AppPaths.HistoryPath);
        Playlist.Repeat = Settings.Repeat;
        Playlist.SetShuffle(Settings.Shuffle);
        try
        {
            if (File.Exists(AppPaths.PlaylistPath)) Playlist.Replace(PlaylistFile.Read(AppPaths.PlaylistPath));
        }
        catch (Exception ex) { Log.Write(ex.Message); }

        Player = MpvPlayer.Create(render: true);
        if (!Settings.HardwareDecoding) Player.SetOption("hwdec", "no");
        Player.SetOption("volume", Settings.Volume.ToString(CultureInfo.InvariantCulture));
        if (Settings.Muted) Player.SetOption("mute", "yes");
        Player.SetOption("sub-codepage", string.IsNullOrWhiteSpace(Settings.SubtitleCodepage) ? "auto" : Settings.SubtitleCodepage);
        Player.SetOption("audio-channels", string.IsNullOrWhiteSpace(Settings.AudioChannels) ? "auto-safe" : Settings.AudioChannels);
        Player.SetOption("sub-scale", Settings.SubtitleScale.ToString("0.##", CultureInfo.InvariantCulture));
        if (Settings.Speed > 0 && Math.Abs(Settings.Speed - 1) > 0.01) Player.SetOption("speed", Settings.Speed.ToString("0.##", CultureInfo.InvariantCulture));
        Player.Start();
        Player.Changed += (facts, clock) => _ui.Post(_ => { if (!_disposed) OnPlayer(facts, clock); }, null);
        _savedPlaylist = string.Join("\n", Playlist.Items);
    }

    internal MpvPlayer Player { get; }
    internal Playlist Playlist { get; } = new();
    internal AppSettings Settings { get; }
    internal HistoryStore History { get; }
    internal bool Active { get; private set; }
    private bool _discardPlaylist;
    internal string? ResumeHint { get; private set; }
    internal string? Warning { get; private set; }
    internal string AspectLabel { get; private set; } = "适应";
    internal event Action? Changed;
    internal event Action<string, string, string>? Failed;

    /// <summary>Unsticks a resume that never received the expected restart.</summary>
    internal void Pump()
    {
        if (ResumeHint is not null && Environment.TickCount64 >= _resumeHintUntil)
        { ResumeHint = null; Changed?.Invoke(); }
        if (!_disposed && Active && !Loading && !Clock.Paused && Environment.TickCount64 - _checkpointAt >= 15000)
            Checkpoint();
        if (_resumePhase == 0) return;
        if (Environment.TickCount64 - _resumeSince < 2000) return;
        _resumePhase = 0;
        _pendingResume = null;
        Player.SetFlag("pause", _away);
    }

    internal PlaybackClock Clock => Player.ReadClock();
    internal PlaybackFacts Facts => Player.ReadFacts();

    /// <summary>Explorer “Open with” queue. Same-folder videos last only for this process.</summary>
    internal async Task PlayFromOpenWith(IReadOnlyList<string> paths)
    {
        _discardPlaylist = true;
        var media = paths.Where(p => MediaPaths.IsMedia(p) || MediaPaths.IsHttp(p)).ToArray();
        var subs = paths.Where(MediaPaths.IsSubtitle).ToArray();
        var siblings = media.Length == 1 ? await Task.Run(() =>
        {
            var found = MediaPaths.TrySiblingVideos(media[0], out var files, out var index);
            return (found, files, index);
        }) : (false, (IReadOnlyList<string>)Array.Empty<string>(), 0);
        if (_disposed) return;
        if (siblings.Item1)
        {
            Playlist.Replace(siblings.Item2, siblings.Item3);
            _pendingSubs.Clear();
            _pendingSubs.AddRange(subs);
            PlayCurrent(false);
            Changed?.Invoke();
            return;
        }
        PlayPaths(paths);
    }

    internal void PlayPaths(IReadOnlyList<string> paths, bool fromStart = false)
    {
        var media = paths.Where(p => MediaPaths.IsMedia(p) || MediaPaths.IsHttp(p)).ToArray();
        var subs = paths.Where(MediaPaths.IsSubtitle).ToArray();
        if (media.Length == 0)
        {
            if (subs.Length > 0 && Active)
            {
                foreach (var sub in subs) Player.Command("sub-add", sub, "select");
            }
            return;
        }
        Playlist.Replace(media, 0);
        _pendingSubs.Clear();
        _pendingSubs.AddRange(subs);
        PlayCurrent(fromStart);
    }

    internal void PlayPath(string path, bool fromStart = false)
    {
        var index = IndexOf(path);
        if (index < 0)
        {
            Playlist.Add([path]);
            index = Playlist.Items.Count - 1;
        }
        Playlist.Select(index);
        PlayCurrent(fromStart);
    }

    internal void PlayIndex(int index, bool fromStart = false)
    {
        if (index < 0 || index >= Playlist.Items.Count) return;
        Playlist.Select(index);
        PlayCurrent(fromStart);
    }

    internal void AddToPlaylist(IReadOnlyList<string> paths)
    {
        Playlist.Add(paths.Where(p => MediaPaths.IsMedia(p) || MediaPaths.IsHttp(p)));
        SavePlaylist();
        Changed?.Invoke();
    }

    internal void RemoveFromPlaylist(int index)
    {
        var removingCurrent = index == Playlist.Index && Active;
        if (removingCurrent) Checkpoint();
        Playlist.RemoveAt(index);
        if (removingCurrent)
        {
            if (Playlist.Current is not null) PlayCurrent(false);
            else { Player.Command("stop"); Active = false; Loading = false; }
        }
        SavePlaylist();
        Changed?.Invoke();
    }

    internal void Reorder(IReadOnlyList<string> paths)
    {
        Playlist.SetOrder(paths);
        SavePlaylist();
    }

    internal void TogglePause()
    {
        var paused = Clock.Paused;
        Player.SetFlag("pause", !paused);
        if (paused) _wasPlaying = true;
        Checkpoint();
    }

    internal void StopPlayback()
    {
        Player.SetFlag("pause", true);
        Player.Command("seek", "0", "absolute+exact");
        Checkpoint();
    }

    internal void Next() => Follow(Playlist.PlanNext(), fromStart: true);

    internal void Previous()
    {
        var restart = Clock.Position > 3;
        Follow(Playlist.PlanPrevious(restart), fromStart: true);
    }

    internal void PreviewSeek(double seconds)
    {
        _dragging = true;
        if (_seek.Allow(false)) Player.Command("seek", MpvPlayer.Invariant(Math.Max(0, seconds)), "absolute+keyframes");
    }

    internal void CommitSeek(double seconds)
    {
        _dragging = false;
        _seek.Allow(true);
        Player.Command("seek", MpvPlayer.Invariant(Math.Max(0, seconds)), "absolute+exact");
        Checkpoint();
    }

    internal void SeekBy(double delta)
    {
        var duration = Clock.Duration;
        var target = Math.Max(0, Clock.Position + delta);
        if (duration > 0) target = Math.Min(target, duration);
        CommitSeek(target);
    }

    internal void SetVolume(double volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        Player.SetDouble("volume", volume);
        Settings.Volume = (int)Math.Round(volume);
        if (volume > 0 && Clock.Muted)
        {
            Player.SetFlag("mute", false);
            Settings.Muted = false;
        }
        SaveSoon();
    }

    internal void NudgeVolume(int delta) => SetVolume(Clock.Volume + delta);

    internal void ToggleMute()
    {
        var mute = !Clock.Muted;
        Player.SetFlag("mute", mute);
        Settings.Muted = mute;
        SaveSoon();
    }

    internal void SetSpeed(double speed)
    {
        speed = Math.Clamp(speed, 0.25, 4);
        Player.SetDouble("speed", speed);
        Settings.Speed = speed;
        SaveSoon();
        Changed?.Invoke();
    }

    internal void SelectAudio(int id)
    {
        Player.SetString("aid", id.ToString(CultureInfo.InvariantCulture));
    }

    internal void SelectSubtitle(int id)
    {
        Player.SetString("sid", id.ToString(CultureInfo.InvariantCulture));
        Player.SetFlag("sub-visibility", true);
    }

    internal void ToggleSubtitles() => Player.SetFlag("sub-visibility", !Player.GetFlag("sub-visibility", true));

    internal void LoadSubtitle(string path)
    {
        if (Player.Command("sub-add", path, "select") < 0)
            throw new IOException("无法加载字幕，请检查文件格式和访问权限。");
    }

    internal void NudgeSubtitleDelay(double delta) => Player.SetDouble("sub-delay", Player.GetDouble("sub-delay") + delta);

    internal void NudgeAudioDelay(double delta) => Player.SetDouble("audio-delay", Player.GetDouble("audio-delay") + delta);

    internal void SetSubtitleScale(double scale)
    {
        scale = Math.Clamp(scale, 0.5, 3);
        Player.SetDouble("sub-scale", scale);
        Settings.SubtitleScale = scale;
        SaveSoon();
    }

    internal void SetSubtitlePosition(double position)
    {
        position = Math.Clamp(position, 0, 100);
        if (_subtitleLifted) _subtitleRest = Math.Min(100, position + 12);
        else _subtitleRest = position;
        Player.SetDouble("sub-pos", position);
    }

    private double _subtitleRest = 100;
    private bool _subtitleLifted;

    /// <summary>Move plain subtitles up while the control bar covers the bottom of the picture.</summary>
    internal void SetSubtitleChrome(bool open)
    {
        if (!Facts.HasVideo) return;
        if (open && !_subtitleLifted)
        {
            var current = Player.GetDouble("sub-pos", 100);
            _subtitleRest = current;
            Player.SetDouble("sub-pos", Math.Max(0, _subtitleRest - 12));
            _subtitleLifted = true;
        }
        else if (!open && _subtitleLifted)
        {
            Player.SetDouble("sub-pos", _subtitleRest);
            _subtitleLifted = false;
        }
    }

    internal void SetSubtitleCodepage(string codepage)
    {
        Settings.SubtitleCodepage = codepage;
        SaveSoon();
        Player.SetString("sub-codepage", codepage);
        Player.Command("sub-reload");
    }

    internal void CycleAspect()
    {
        _aspect = (_aspect + 1) % 5;
        ApplyAspect();
        Changed?.Invoke();
    }

    internal void CycleRotate()
    {
        _rotate = (_rotate + 90) % 360;
        Player.SetDouble("video-rotate", _rotate);
    }

    internal void FrameStep() => Player.Command("frame-step");

    internal string Screenshot()
    {
        var folder = string.IsNullOrWhiteSpace(Settings.ScreenshotDirectory) ? AppPaths.DefaultScreenshots() : Settings.ScreenshotDirectory;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "SPlayer " + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".png");
        ScreenshotTo(path);
        return path;
    }

    internal void ScreenshotTo(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        if (Player.Command("screenshot-to-file", path, "subtitles") < 0 || !File.Exists(path))
            throw new IOException("截图没有保存，请确认视频已加载且目标文件夹可写。");
    }

    internal void SetRepeat(RepeatMode mode)
    {
        Playlist.Repeat = mode;
        Settings.Repeat = mode;
        SaveSoon();
        Changed?.Invoke();
    }

    internal void SetShuffle(bool enabled)
    {
        Playlist.SetShuffle(enabled);
        Settings.Shuffle = enabled;
        SaveSoon();
        Changed?.Invoke();
    }

    internal void RestartFromBeginning()
    {
        ResumeHint = null;
        _pendingResume = null;
        CommitSeek(0);
    }

    internal void LeavePlayer()
    {
        _away = true;
        _wasPlaying = Active && (!Clock.Paused || Loading);
        Checkpoint();
        if (_wasPlaying) Player.SetFlag("pause", true);
    }

    internal void ReturnToPlayer()
    {
        _away = false;
        if (_wasPlaying && !Loading) Player.SetFlag("pause", false);
        _wasPlaying = false;
    }

    internal void ApplyHardware(bool enabled)
    {
        Settings.HardwareDecoding = enabled;
        SaveSoon();
        Player.SetString("hwdec", enabled ? "auto-safe" : "no");
    }

    internal void ApplyChannels(string value)
    {
        Settings.AudioChannels = value;
        SaveSoon();
        Player.SetString("audio-channels", value);
    }

    internal void Checkpoint()
    {
        _checkpointAt = Environment.TickCount64;
        if (!Settings.RecordHistory || !Active || Loading || _historySuppressed) return;
        var facts = Facts;
        var clock = Clock;
        if (!facts.FileLoaded || string.IsNullOrWhiteSpace(facts.Path)) return;
        var duration = clock.Duration > 0 ? clock.Duration : 0;
        if (!MediaPaths.CanPersist(facts.Path)) return;
        var entry = new HistoryEntry
        {
            Path = facts.Path,
            Position = clock.Eof ? duration : clock.Position,
            Duration = duration,
            PlayedAt = DateTimeOffset.Now,
            Completed = clock.Eof || ResumePolicy.IsCompleted(clock.Position, duration),
            Title = string.IsNullOrWhiteSpace(facts.MediaTitle) ? MediaPaths.DisplayName(facts.Path) : facts.MediaTitle,
        };
        _recentPositions[facts.Path] = entry;
        _historyWrite = _historyWrite.ContinueWith(_ =>
        {
            try { History.Upsert(entry); _ui.Post(_ => { if (!_disposed) Changed?.Invoke(); }, null); }
            catch (Exception ex) { Log.Write(ex.ToString()); _ui.Post(_ => Notice?.Invoke("播放记录保存失败，请检查磁盘和权限。"), null); }
        }, TaskScheduler.Default);
    }

    internal async Task ClearHistory()
    {
        _historySuppressed = true;
        _recentPositions.Clear();
        await _historyWrite;
        await Task.Run(History.Clear);
        Changed?.Invoke();
    }

    internal async void SaveSoon()
    {
        if (_disposed) return;
        if (_saving) { _saveAgain = true; return; }
        _saving = true;
        try
        {
            do
            {
                _saveAgain = false;
                await Task.Delay(500);
                if (!_disposed) Save();
            } while (_saveAgain && !_disposed);
        }
        finally { _saving = false; }
    }

    internal void Save()
    {
        try
        {
            Settings.Save(AppPaths.SettingsPath);
            SavePlaylist();
        }
        catch (Exception ex) { Log.Write(ex.ToString()); }
    }

    internal void PrepareClose()
    {
        if (_disposed) return;
        Checkpoint(); Save(); _disposed = true;
    }

    public void Dispose()
    {
        PrepareClose();
        _historyWrite.GetAwaiter().GetResult();
        Player.Dispose();
    }

    private void PlayCurrent(bool fromStart)
    {
        var path = Playlist.Current;
        if (string.IsNullOrWhiteSpace(path)) return;
        Checkpoint();
        Active = true;
        Loading = true;
        _historySuppressed = false;
        _shownError = null;
        _rotate = 0;
        _aspect = 0;
        AspectLabel = "适应";
        _subtitleLifted = false;
        Player.SetFlag("keepaspect", true);
        Player.SetDouble("sub-pos", _subtitleRest);
        ResumeHint = null;
        Warning = null;
        _eofLatched = false;
        _pendingResume = null;
        _resumePhase = 0;
        if (!fromStart && Settings.RecordHistory && Settings.RememberPosition)
        {
            var entry = _recentPositions.TryGetValue(path, out var recent) ? recent : History.Find(path);
            if (entry is not null && ResumePolicy.ShouldResume(entry))
            {
                _pendingResume = entry.Position;
                ResumeHint = "从 " + ClockText.Format(entry.Position) + " 继续";
                _resumeHintUntil = Environment.TickCount64 + 4500;
            }
        }
        Player.SetFlag("pause", true);
        Player.Load(path);
        SavePlaylist();
        Changed?.Invoke();
    }

    private void OnPlayer(PlaybackFacts facts, PlaybackClock clock)
    {
        if (facts.LoadEpoch != _seenLoad && facts.FileLoaded)
        {
            Loading = false;
            _seenLoad = facts.LoadEpoch;
            _seenRestart = facts.RestartEpoch;
            _eofLatched = true;
            _resumeSince = Environment.TickCount64;
            foreach (var sub in _pendingSubs) Player.Command("sub-add", sub, "select");
            _pendingSubs.Clear();
            if (_pendingResume is double position)
            {
                _resumePhase = 1;
                _resumeAt = position;
            }
            else
            {
                _resumePhase = 0;
                Player.SetFlag("pause", _away);
            }
        }
        else if (_resumePhase == 1 && facts.RestartEpoch > _seenRestart)
        {
            _seenRestart = facts.RestartEpoch;
            _resumePhase = 2;
            _resumeSince = Environment.TickCount64;
            Player.Command("seek", MpvPlayer.Invariant(_resumeAt), "absolute+exact");
        }
        else if (_resumePhase == 2 && facts.RestartEpoch > _seenRestart)
        {
            _seenRestart = facts.RestartEpoch;
            _resumePhase = 0;
            _pendingResume = null;
            Player.SetFlag("pause", _away);
        }
        if (clock.Eof && !_eofLatched && !_dragging && _resumePhase == 0 && facts.FileLoaded)
        {
            _eofLatched = true;
            Checkpoint();
            Follow(Playlist.PlanOnEnd(), fromStart: true);
        }
        if (!clock.Eof) _eofLatched = false;
        if (!string.IsNullOrEmpty(facts.ErrorTitle) && facts.ErrorDetail != _shownError)
        {
            Loading = false;
            _shownError = facts.ErrorDetail;
            Failed?.Invoke(facts.ErrorTitle, facts.ErrorSummary ?? "", facts.ErrorDetail ?? "");
        }
        NoteDecoder(facts);
        Changed?.Invoke();
    }

    private void NoteDecoder(PlaybackFacts facts)
    {
        if (!Settings.HardwareDecoding || !facts.HasVideo || !facts.FileLoaded || facts.RestartEpoch == 0)
            return;
        if (string.IsNullOrEmpty(facts.Hwdec)) return;
        Warning = facts.Hwdec == "no" ? "此视频正在使用软件解码。" : null;
    }

    private void Follow(Advance advance, bool fromStart)
    {
        switch (advance.Kind)
        {
            case AdvanceKind.Replay:
                Player.Command("seek", "0", "absolute+exact");
                Player.SetFlag("pause", false);
                break;
            case AdvanceKind.Goto:
                PlayIndex(advance.Index, fromStart);
                break;
        }
    }

    private void ApplyAspect()
    {
        Player.SetFlag("video-unscaled", false);
        switch (_aspect)
        {
            case 1:
                AspectLabel = "铺满";
                Player.SetString("video-aspect-override", "no");
                Player.SetFlag("keepaspect", true);
                Player.SetDouble("panscan", 1);
                break;
            case 2:
                AspectLabel = "拉伸";
                Player.SetString("video-aspect-override", "no");
                Player.SetFlag("keepaspect", false);
                Player.SetDouble("panscan", 0);
                break;
            case 3:
                AspectLabel = "16:9";
                Player.SetString("video-aspect-override", "1.777");
                Player.SetFlag("keepaspect", true);
                Player.SetDouble("panscan", 0);
                break;
            case 4:
                AspectLabel = "4:3";
                Player.SetString("video-aspect-override", "1.333");
                Player.SetFlag("keepaspect", true);
                Player.SetDouble("panscan", 0);
                break;
            default:
                AspectLabel = "适应";
                Player.SetString("video-aspect-override", "no");
                Player.SetFlag("keepaspect", true);
                Player.SetDouble("panscan", 0);
                break;
        }
    }

    private int IndexOf(string path)
    {
        for (var i = 0; i < Playlist.Items.Count; i++)
        {
            if (MediaPaths.Same(Playlist.Items[i], path)) return i;
        }
        return -1;
    }

    private void SavePlaylist()
    {
        if (_discardPlaylist) return;
        var items = Playlist.Items.Where(MediaPaths.CanPersist).ToArray();
        var key = string.Join("\n", items);
        if (key == _savedPlaylist) return;
        try { AtomicFile.Locked(AppPaths.PlaylistPath, () => PlaylistFile.Write(AppPaths.PlaylistPath, items)); _savedPlaylist = key; }
        catch (Exception ex) { Log.Write(ex.Message); }
    }
}
