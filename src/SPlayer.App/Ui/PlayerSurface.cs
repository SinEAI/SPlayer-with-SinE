using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SPlayer.Ui;

/// <summary>The video or audio surface and the controls that appear over it.</summary>
internal sealed class PlayerSurface : Grid
{
    private readonly Session _session;
    private readonly SwapChainPanel _panel = new();
    private readonly Grid _videoHost = new() { Background = new SolidColorBrush(Colors.Black) };
    private readonly Grid _audioHost = new() { Background = Kit.White };
    private readonly Grid _artHost = new() { Width = 280, Height = 280, Background = Kit.Chrome };
    private readonly TextBlock _audioTitle;
    private readonly TextBlock _audioArtist;
    private readonly TextBlock _audioAlbum;
    private readonly Grid _top;
    private readonly Grid _bottom;
    private readonly Border _drag;
    private readonly Button _back;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly Button _play;
    private readonly TextBlock _time;
    private readonly Slider _seek;
    private readonly Button _mute;
    private readonly Slider _volume;
    private readonly Button _speed;
    private readonly Button _subs;
    private readonly Button _audio;
    private readonly Button _playlistButton;
    private readonly Button _full;
    private readonly ListView _playlist;
    private readonly Border _playlistHost;
    private readonly Border _error;
    private readonly TextBlock _errorText;
    private readonly Border _catcher = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(2500) };
    private readonly MenuFlyout _menu = new();
    private PlaybackFacts _facts = new();
    private bool _scrub;
    private bool _volumeDrag;
    private bool _chromeShown = true;
    private bool _overChrome;
    private bool _menuOpen;
    private bool _playlistOpen;
    private bool _fullscreen;
    private bool _video = true;
    private bool _suppressShow;
    private bool _pendingDismiss;
    private bool _haveDismissAt;
    private Windows.Foundation.Point _dismissAt;
    private Windows.Foundation.Point _pressAt;
    private int _clicks;
    private string _playlistKey = "";
    private string? _notice;
    private long _noticeUntil;

    internal PlayerSurface(Session session, VideoPresenter presenter)
    {
        _session = session;
        presenter.Attach(_panel);
        _videoHost.Children.Add(_panel);
        (_audioTitle, _audioArtist, _audioAlbum) = BuildAudio();
        Children.Add(_videoHost);
        Children.Add(_audioHost);
        Children.Add(_catcher);
        (_top, _drag, _back, _title, _hint) = BuildTop();
        (_bottom, _play, _time, _seek, _mute, _volume, _speed, _subs, _audio, _playlistButton, _full) = BuildBottom();
        (_playlistHost, _playlist) = BuildPlaylist();
        (_error, _errorText) = BuildError();
        Children.Add(_playlistHost);
        Children.Add(_bottom);
        Children.Add(_top);
        Children.Add(_error);
        _catcher.ContextFlyout = _menu;
        _menu.Opening += (_, _) => RebuildMenu();
        _menu.Opened += (_, _) => { _menuOpen = true; ShowChrome(); };
        _menu.Closed += (_, _) => { _menuOpen = false; ShowChrome(); };
        _catcher.PointerPressed += OnSurfacePressed;
        _catcher.PointerMoved += (_, e) => ShowChrome(e.GetCurrentPoint(_catcher).Position);
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            if (_clicks == 1)
            {
                if (_pendingDismiss)
                {
                    _dismissAt = _pressAt;
                    _haveDismissAt = true;
                    HideChrome();
                }
                else _session.TogglePause();
            }
            _clicks = 0;
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (KeepChrome()) return;
            _chromeShown = false;
            Motion.Fade(_top, false);
            Motion.Fade(_bottom, false);
            _session.SetSubtitleChrome(false);
            ChromeChanged?.Invoke();
        };
        PointerMoved += (_, e) => ShowChrome(e.GetCurrentPoint(this).Position);
        _top.PointerEntered += (_, _) => { _overChrome = true; _suppressShow = false; ShowChrome(force: true); };
        _top.PointerExited += (_, _) => { _overChrome = false; ShowChrome(); };
        _bottom.PointerEntered += (_, _) => { _overChrome = true; _suppressShow = false; ShowChrome(force: true); };
        _bottom.PointerExited += (_, _) => { _overChrome = false; ShowChrome(); };
        AllowBack = true;
        ApplyMode(video: true);
        ShowChrome();
    }

    internal bool AllowBack
    {
        get => _back.Visibility == Visibility.Visible;
        set
        {
            _back.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_drag, value ? 1 : 0);
            Grid.SetColumnSpan(_drag, value ? 1 : 2);
        }
    }

    internal UIElement DragRegion => _drag;
    internal bool ChromeOpen => _chromeShown;
    internal bool ShowingVideo => _video;
    internal event Action? BackRequested;
    internal event Action? FullscreenToggle;
    internal event Action? OpenFilesRequested;
    internal event Action? OpenFolderRequested;
    internal event Action? OpenUrlRequested;
    internal event Action? SubtitleRequested;
    internal event Action? ChromeChanged;

    internal void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        SetGlyph(_full, fullscreen ? Kit.G.Restore : Kit.G.FullScreen);
        ToolTipService.SetToolTip(_full, fullscreen ? "退出全屏" : "全屏");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_full, fullscreen ? "退出全屏" : "全屏");
    }

    internal void Refresh(PlaybackFacts facts)
    {
        _facts = facts;
        var video = facts.HasVideo;
        if (video != _video || facts.AlbumArtOnly) ApplyMode(video);
        var name = string.IsNullOrWhiteSpace(facts.MediaTitle) ? MediaPaths.DisplayName(facts.Path) : facts.MediaTitle;
        _title.Text = string.IsNullOrWhiteSpace(facts.Path) ? "SPlayer" : name;
        _audioTitle.Text = string.IsNullOrWhiteSpace(name) ? "未在播放" : name;
        _audioArtist.Text = facts.Artist ?? "";
        _audioArtist.Visibility = string.IsNullOrWhiteSpace(facts.Artist) ? Visibility.Collapsed : Visibility.Visible;
        _audioAlbum.Text = facts.Album ?? "";
        _audioAlbum.Visibility = string.IsNullOrWhiteSpace(facts.Album) ? Visibility.Collapsed : Visibility.Visible;
        var failed = !string.IsNullOrWhiteSpace(facts.ErrorTitle);
        _error.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        _errorText.Text = failed ? facts.ErrorTitle + "\n" + facts.ErrorSummary : "";
        _hint.Text = Environment.TickCount64 < _noticeUntil ? _notice ?? "" :
            _session.Loading ? "正在打开…" : _session.Warning ?? _session.ResumeHint ?? "";
        _hint.Visibility = string.IsNullOrWhiteSpace(_hint.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (video && _chromeShown) _session.SetSubtitleChrome(true);
        FillPlaylist();
        var speed = _session.Clock.Speed;
        if (speed <= 0) speed = 1;
        _speed.Content = speed.ToString("0.##") + "×";
    }

    internal void Tick(PlaybackClock clock)
    {
        if (_notice is not null && Environment.TickCount64 >= _noticeUntil) { _notice = null; Refresh(_session.Facts); }
        if (!_scrub)
        {
            if (clock.Duration > 0.1)
            {
                _seek.IsEnabled = true;
                _seek.Maximum = clock.Duration;
                _seek.Value = Math.Clamp(clock.Position, 0, clock.Duration);
            }
            else
            {
                _seek.IsEnabled = false;
                _seek.Maximum = 1;
                _seek.Value = 0;
            }
        }
        _time.Text = ClockText.Format(clock.Position) + " / " + ClockText.Format(clock.Duration);
        _play.IsEnabled = _session.Active && !_session.Loading;
        SetGlyph(_play, clock.Paused ? Kit.G.Play : Kit.G.Pause);
        var playLabel = clock.Paused ? "播放" : "暂停";
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(_play) != playLabel)
        {
            ToolTipService.SetToolTip(_play, playLabel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_play, playLabel);
        }
        SetGlyph(_mute, clock.Muted || clock.Volume <= 0 ? Kit.G.Mute : Kit.G.Volume);
        if (!_volumeDrag) _volume.Value = clock.Muted ? 0 : clock.Volume;
        if (clock.Paused) ShowChrome();
    }

    private (TextBlock Title, TextBlock Artist, TextBlock Album) BuildAudio()
    {
        var title = Kit.Title("未在播放", 28);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextWrapping = TextWrapping.Wrap;
        title.TextAlignment = TextAlignment.Center;
        var artist = Kit.Text("", 16, secondary: true);
        artist.HorizontalAlignment = HorizontalAlignment.Center;
        var album = Kit.Text("", 13, secondary: true);
        album.Foreground = Kit.Tertiary;
        album.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 640 };
        _artHost.HorizontalAlignment = HorizontalAlignment.Center;
        _artHost.Children.Add(new FontIcon { Glyph = Kit.G.Music, FontSize = 72, Foreground = Kit.Tertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(_artHost);
        stack.Children.Add(title);
        stack.Children.Add(artist);
        stack.Children.Add(album);
        _audioHost.Children.Add(stack);
        _audioHost.Visibility = Visibility.Collapsed;
        return (title, artist, album);
    }

    private (Grid Top, Border Drag, Button Back, TextBlock Title, TextBlock Hint) BuildTop()
    {
        var top = new Grid { VerticalAlignment = VerticalAlignment.Top };
        top.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Kit.TitleBarHeight) });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = Kit.IconButton(Kit.G.Back, "返回", () => BackRequested?.Invoke());
        back.Width = 48;
        back.Height = Kit.TitleBarHeight;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        var drag = new Border { Background = new SolidColorBrush(Colors.Transparent) };
        var title = Kit.Text("SPlayer", 13);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.IsHitTestVisible = false;
        title.Margin = new Thickness(8, 0, 160, 0);
        drag.Child = title;
        var hint = Kit.Text("", 12, secondary: true);
        hint.Margin = new Thickness(16, 4, 16, 8);
        hint.IsHitTestVisible = false;
        Grid.SetColumn(drag, 1);
        Grid.SetRow(hint, 1);
        Grid.SetColumnSpan(hint, 2);
        top.Children.Add(back);
        top.Children.Add(drag);
        top.Children.Add(hint);
        return (top, drag, back, title, hint);
    }

    private (Grid Bar, Button Play, TextBlock Time, Slider Seek, Button Mute, Slider Volume, Button Speed, Button Subs, Button Audio, Button Playlist, Button Full) BuildBottom()
    {
        var bar = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 0, 8, 8) };
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var seek = new Slider { Minimum = 0, Maximum = 1, IsThumbToolTipEnabled = false, IsTabStop = true, Margin = new Thickness(8, 4, 8, 0) };
        seek.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _scrub = true), true);
        seek.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndScrub(seek)), true);
        seek.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndScrub(seek)), true);
        seek.ValueChanged += (_, e) => { if (_scrub) _session.PreviewSeek(e.NewValue); };
        seek.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)
            { _session.SeekBy(e.Key == Windows.System.VirtualKey.Left ? -5 : 5); e.Handled = true; }
        };
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var play = DarkIcon(Kit.G.Play, "播放", () => _session.TogglePause());
        var previous = DarkIcon(Kit.G.Previous, "上一个", () => _session.Previous());
        var next = DarkIcon(Kit.G.Next, "下一个", () => _session.Next());
        var time = new TextBlock { Text = "00:00 / 00:00", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 13, FontFamily = new FontFamily("Segoe UI") };
        buttons.Children.Add(play);
        buttons.Children.Add(previous);
        buttons.Children.Add(next);
        buttons.Children.Add(time);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var mute = DarkIcon(Kit.G.Volume, "静音", () => _session.ToggleMute());
        var volume = new Slider { Width = 92, Minimum = 0, Maximum = 100, Value = 100, IsThumbToolTipEnabled = false, IsTabStop = true, VerticalAlignment = VerticalAlignment.Center };
        volume.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _volumeDrag = true), true);
        volume.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => _volumeDrag = false), true);
        volume.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => _volumeDrag = false), true);
        volume.ValueChanged += (_, e) => { if (_volumeDrag) _session.SetVolume(e.NewValue); };
        volume.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
            { _session.NudgeVolume(e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Down ? -5 : 5); e.Handled = true; }
        };
        var speed = DarkText("1×", "倍速");
        speed.Flyout = SpeedMenu();
        var subs = DarkIcon(Kit.G.Caption, "字幕", () => { });
        subs.Flyout = TrackMenu(subtitles: true);
        var audio = DarkIcon(Kit.G.Music, "音轨", () => { });
        audio.Flyout = TrackMenu(subtitles: false);
        var playlist = DarkIcon(Kit.G.List, "播放列表", TogglePlaylist);
        var full = DarkIcon(Kit.G.FullScreen, "全屏", () => FullscreenToggle?.Invoke());
        tools.Children.Add(mute);
        tools.Children.Add(volume);
        tools.Children.Add(speed);
        tools.Children.Add(subs);
        tools.Children.Add(audio);
        tools.Children.Add(playlist);
        tools.Children.Add(full);
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.SizeChanged += (_, _) =>
        {
            var narrow = row.ActualWidth < 680;
            Grid.SetRow(tools, narrow ? 1 : 0);
            tools.Margin = new Thickness(0, narrow ? 6 : 0, 0, 0);
            volume.Visibility = row.ActualWidth < 430 ? Visibility.Collapsed : Visibility.Visible;
            time.FontSize = row.ActualWidth < 430 ? 12 : 13;
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(seek, "播放进度");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(volume, "音量");
        row.Children.Add(buttons);
        row.Children.Add(tools);
        Grid.SetRow(row, 1);
        bar.Children.Add(seek);
        bar.Children.Add(row);
        return (bar, play, time, seek, mute, volume, speed, subs, audio, playlist, full);
    }

    private (Border Host, ListView List) BuildPlaylist()
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single };
        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is MediaRow row) _session.PlayIndex(row.Index, fromStart: true);
        };
        list.IsItemClickEnabled = true;
        list.ItemTemplate = ListTemplate();
        var host = new Border
        {
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(0, 48, 0, 96),
            Child = list,
        };
        return (host, list);
    }

    private (Border Box, TextBlock Text) BuildError()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, TextAlignment = TextAlignment.Center };
        var button = new Button { Content = "查看详细信息", HorizontalAlignment = HorizontalAlignment.Center };
        button.Click += async (_, _) =>
        {
            if (XamlRoot is null) return;
            await Dialogs.Alert(XamlRoot, _facts.ErrorTitle ?? "播放失败", _facts.ErrorSummary ?? "", _facts.ErrorDetail);
        };
        var stack = new StackPanel { Spacing = 12, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(text);
        stack.Children.Add(button);
        stack.Children.Add(Kit.Button("重试播放", () => _session.PlayIndex(_session.Playlist.Index)));
        stack.Children.Add(Kit.Button("打开其他文件", () => OpenFilesRequested?.Invoke()));
        var box = new Border { Child = stack, Visibility = Visibility.Collapsed, Padding = new Thickness(32) };
        return (box, text);
    }

    private void ApplyMode(bool video)
    {
        _video = video;
        _videoHost.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        _audioHost.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        Background = video ? new SolidColorBrush(Colors.Black) : Kit.White;
        var art = !video && _facts.AlbumArtOnly;
        Reparent(art);
        _artHost.Background = art ? new SolidColorBrush(Colors.Black) : Kit.Chrome;
        foreach (var child in _artHost.Children.OfType<FontIcon>()) child.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
        var ink = video ? Colors.White : Colors.Black;
        var brush = new SolidColorBrush(ink);
        _title.Foreground = brush;
        _time.Foreground = brush;
        _hint.Foreground = video ? new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)) : Kit.Secondary;
        var theme = video ? ElementTheme.Dark : ElementTheme.Default;
        _top.RequestedTheme = theme;
        _bottom.RequestedTheme = theme;
        _playlistHost.RequestedTheme = theme;
        _top.Background = video ? Shade(top: true) : new SolidColorBrush(Colors.White);
        _bottom.Background = video ? Shade(top: false) : new SolidColorBrush(Colors.White);
        _playlistHost.Background = new SolidColorBrush(video ? Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1A) : Color.FromArgb(255, 255, 255, 255));
        _playlistHost.BorderBrush = video ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : Kit.Divider;
        _playlistHost.BorderThickness = new Thickness(1, 0, 0, 0);
        _error.Background = video ? new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) : Kit.White;
        _errorText.Foreground = brush;
        StyleCommands(video);
    }

    private void StyleCommands(bool video)
    {
        foreach (var button in new Button[] { _play, _speed, _subs, _audio, _playlistButton, _full, _mute, _back })
        {
            if (video) Kit.DarkCommand(button);
            else Kit.Flat(button);
        }
        foreach (var button in Buttons(_bottom))
        {
            if (video) Kit.DarkCommand(button);
            else Kit.Flat(button);
        }
    }

    private void Reparent(bool album)
    {
        Panel target = album ? _artHost : _videoHost;
        if (target.Children.Contains(_panel)) return;
        if (_panel.Parent is Panel parent) parent.Children.Remove(_panel);
        target.Children.Add(_panel);
    }

    private void EndScrub(Slider seek)
    {
        if (!_scrub) return;
        _scrub = false;
        _session.CommitSeek(seek.Value);
        ShowChrome();
    }

    private void OnSurfacePressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_catcher);
        if (point.Properties.IsRightButtonPressed) return;
        _pressAt = point.Position;
        _clicks++;
        if (_clicks >= 2)
        {
            _clickTimer.Stop();
            _clicks = 0;
            _suppressShow = false;
            FullscreenToggle?.Invoke();
            ShowChrome(force: true);
        }
        else
        {
            _pendingDismiss = _chromeShown;
            if (!_chromeShown) ShowChrome(point.Position);
            _clickTimer.Start();
        }
        e.Handled = true;
    }

    private void ShowChrome(Windows.Foundation.Point? point = null, bool force = false)
    {
        if (!force && _suppressShow)
        {
            if (point is Windows.Foundation.Point moved && _haveDismissAt)
            {
                var dx = moved.X - _dismissAt.X;
                var dy = moved.Y - _dismissAt.Y;
                if (dx * dx + dy * dy < 144) return;
            }
            else return;
            _suppressShow = false;
        }
        if (!_chromeShown)
        {
            _top.Visibility = Visibility.Visible;
            _bottom.Visibility = Visibility.Visible;
            _chromeShown = true;
            Motion.Fade(_top, true);
            Motion.Fade(_bottom, true);
            _session.SetSubtitleChrome(true);
            ChromeChanged?.Invoke();
        }
        _hideTimer.Stop();
        if (!KeepChrome()) _hideTimer.Start();
    }

    private void HideChrome()
    {
        _suppressShow = true;
        _hideTimer.Stop();
        if (!_chromeShown) return;
        _chromeShown = false;
        Motion.Fade(_top, false);
        Motion.Fade(_bottom, false);
        _session.SetSubtitleChrome(false);
        ChromeChanged?.Invoke();
    }

    private bool KeepChrome() => !_suppressShow && (Environment.TickCount64 < _noticeUntil || _session.Clock.Paused || _overChrome || _menuOpen || _scrub || _volumeDrag || _playlistOpen);

    private static Brush Shade(bool top)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        if (top)
        {
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x99, 0, 0, 0), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x33, 0, 0, 0), Offset = 0.62 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 1 });
        }
        else
        {
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x66, 0, 0, 0), Offset = 0.35 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xC8, 0, 0, 0), Offset = 1 });
        }
        return brush;
    }

    private void TogglePlaylist()
    {
        _playlistOpen = !_playlistOpen;
        if (_playlistOpen)
        {
            _playlistHost.Visibility = Visibility.Visible;
            Motion.Fade(_playlistHost, true);
            FillPlaylist();
        }
        else Motion.Fade(_playlistHost, false, () => { if (!_playlistOpen) _playlistHost.Visibility = Visibility.Collapsed; });
        ShowChrome();
    }

    private void FillPlaylist()
    {
        if (!_playlistOpen) return;
        var key = _session.Playlist.Index + "\n" + string.Join("\n", _session.Playlist.Items);
        if (key == _playlistKey) return;
        _playlistKey = key;
        var rows = new List<MediaRow>();
        for (var i = 0; i < _session.Playlist.Items.Count; i++)
        {
            var path = _session.Playlist.Items[i];
            rows.Add(new MediaRow { Index = i, Path = path, Title = MediaPaths.DisplayName(path), Detail = i == _session.Playlist.Index ? "当前" : "" });
        }
        _playlist.ItemsSource = rows;
        if (_session.Playlist.Index >= 0 && _session.Playlist.Index < rows.Count) _playlist.SelectedIndex = _session.Playlist.Index;
    }

    private MenuFlyout SpeedMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var speed in new[] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 4.0 })
            {
                var value = speed;
                var item = new MenuFlyoutItem { Text = value.ToString("0.##") + "×" };
                item.Click += (_, _) => _session.SetSpeed(value);
                menu.Items.Add(item);
            }
        };
        Watch(menu);
        return menu;
    }

    private MenuFlyout TrackMenu(bool subtitles)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (subtitles)
            {
                var toggle = new MenuFlyoutItem { Text = _facts.SubtitlesVisible ? "关闭字幕" : "显示字幕" };
                toggle.Click += (_, _) => _session.ToggleSubtitles();
                menu.Items.Add(toggle);
            }
            var kind = subtitles ? "sub" : "audio";
            foreach (var track in _facts.Tracks.Where(t => t.Kind == kind))
            {
                var label = subtitles
                    ? TrackLabels.Subtitle(track.Title, track.Language, track.Codec, track.External)
                    : TrackLabels.Audio(track.Title, track.Language, track.Codec, track.Channels);
                if (track.Selected) label = "● " + label;
                var item = new MenuFlyoutItem { Text = label };
                var id = track.Id;
                item.Click += (_, _) =>
                {
                    if (subtitles) _session.SelectSubtitle(id);
                    else _session.SelectAudio(id);
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = subtitles ? "没有字幕" : "没有音轨", IsEnabled = false });
            menu.Items.Add(new MenuFlyoutSeparator());
            if (subtitles)
            {
                Add(menu, "加载字幕…", () => SubtitleRequested?.Invoke());
                var options = new MenuFlyoutSubItem { Text = "字幕调整" };
                menu.Items.Add(options);
                Add(options, "字幕提前 0.1 秒", () => _session.NudgeSubtitleDelay(-0.1));
                Add(options, "字幕延后 0.1 秒", () => _session.NudgeSubtitleDelay(0.1));
                Add(options, "字号减小", () => _session.SetSubtitleScale(_session.Settings.SubtitleScale - 0.1));
                Add(options, "字号增大", () => _session.SetSubtitleScale(_session.Settings.SubtitleScale + 0.1));
                Add(options, "字幕上移", () => _session.SetSubtitlePosition(Math.Max(0, _session.Player.GetDouble("sub-pos", 100) - 5)));
                Add(options, "字幕下移", () => _session.SetSubtitlePosition(_session.Player.GetDouble("sub-pos", 100) + 5));
                var encoding = new MenuFlyoutSubItem { Text = "字幕编码" };
                menu.Items.Add(encoding);
                Add(encoding, "自动", () => _session.SetSubtitleCodepage("auto"));
                Add(encoding, "UTF-8", () => _session.SetSubtitleCodepage("utf-8"));
                Add(encoding, "GB18030", () => _session.SetSubtitleCodepage("gb18030"));
                Add(encoding, "Big5", () => _session.SetSubtitleCodepage("big5"));
            }
            else
            {
                Add(menu, "音频提前 0.1 秒", () => _session.NudgeAudioDelay(-0.1));
                Add(menu, "音频延后 0.1 秒", () => _session.NudgeAudioDelay(0.1));
                Add(menu, "声道：自动", () => _session.ApplyChannels("auto-safe"));
                Add(menu, "声道：立体声", () => _session.ApplyChannels("stereo"));
                Add(menu, "声道：单声道", () => _session.ApplyChannels("mono"));
            }
        };
        Watch(menu);
        return menu;
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        Add(_menu, _session.Clock.Paused ? "播放" : "暂停", () => _session.TogglePause());
        Add(_menu, "停止", () => _session.StopPlayback());
        Add(_menu, "上一个", () => _session.Previous());
        Add(_menu, "下一个", () => _session.Next());
        Add(_menu, "从头播放", () => _session.RestartFromBeginning());
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(_menu, "画面比例：" + _session.AspectLabel, () => _session.CycleAspect());
        Add(_menu, "旋转 90°", () => _session.CycleRotate());
        Add(_menu, "截图", Shoot);
        Add(_menu, "播放信息", async () => { if (XamlRoot is not null) await Dialogs.Info(XamlRoot, _session.Facts, _session.Clock); });
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(_menu, "循环：" + RepeatText(_session.Playlist.Repeat), CycleRepeat);
        Add(_menu, _session.Playlist.Shuffle ? "关闭随机播放" : "随机播放", () => _session.SetShuffle(!_session.Playlist.Shuffle));
        Add(_menu, "逐帧前进", () => _session.FrameStep());
        _menu.Items.Add(new MenuFlyoutSeparator());
        Add(_menu, "打开文件", () => OpenFilesRequested?.Invoke());
        Add(_menu, "打开文件夹", () => OpenFolderRequested?.Invoke());
        Add(_menu, "打开网络地址", () => OpenUrlRequested?.Invoke());
        Add(_menu, "加载字幕", () => SubtitleRequested?.Invoke());
    }

    private void Shoot()
    {
        try
        {
            var path = _session.Screenshot();
            ShowNotice("截图已保存 · " + Path.GetFileName(path));
            Log.Write("截图 " + path);
        }
        catch (Exception ex)
        {
            ShowNotice("截图失败：" + ex.Message);
            Log.Write(ex.ToString());
        }
    }

    internal void ShowNotice(string message)
    {
        _notice = message;
        _noticeUntil = Environment.TickCount64 + 4500;
        Refresh(_session.Facts);
        ShowChrome(force: true);
    }

    private void CycleRepeat()
    {
        var next = _session.Playlist.Repeat switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None,
        };
        _session.SetRepeat(next);
    }

    private static string RepeatText(RepeatMode mode) => mode switch
    {
        RepeatMode.One => "单个",
        RepeatMode.All => "列表",
        _ => "关闭",
    };

    private void Watch(MenuFlyout menu)
    {
        menu.Opened += (_, _) => { _menuOpen = true; ShowChrome(); };
        menu.Closed += (_, _) => { _menuOpen = false; ShowChrome(); };
    }

    private static void Add(MenuFlyout menu, string text, Action click)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }

    private static void Add(MenuFlyoutSubItem menu, string text, Action click)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }

    private static Button DarkIcon(string glyph, string tip, Action click)
    {
        var button = Kit.IconButton(glyph, tip, click);
        Kit.DarkCommand(button);
        return button;
    }

    private static Button DarkText(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 8, 10, 8),
            IsTabStop = true,
            FontFamily = new FontFamily("Segoe UI"),
        };
        Kit.DarkCommand(button);
        ToolTipService.SetToolTip(button, tip);
        return button;
    }

    private static void SetGlyph(Button button, string glyph)
    {
        if (button.Content is FontIcon icon) { if (icon.Glyph != glyph) icon.Glyph = glyph; }
        else button.Content = new FontIcon { Glyph = glyph, FontSize = 16 };
    }

    private static DataTemplate ListTemplate()
    {
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Padding="12,8">
                <Grid.RowDefinitions>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                <TextBlock Text="{Binding Title}" FontSize="14" TextTrimming="CharacterEllipsis"/>
                <TextBlock Grid.Row="1" Text="{Binding Detail}" FontSize="12" Opacity="0.7" TextTrimming="CharacterEllipsis"/>
              </Grid>
            </DataTemplate>
            """);
    }

    private static IEnumerable<Button> Buttons(DependencyObject node)
    {
        if (node is Button button) yield return button;
        switch (node)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    if (child is DependencyObject element)
                        foreach (var nested in Buttons(element)) yield return nested;
                break;
            case ContentControl { Content: DependencyObject content }:
                foreach (var nested in Buttons(content)) yield return nested;
                break;
            case Border { Child: DependencyObject child }:
                foreach (var nested in Buttons(child)) yield return nested;
                break;
        }
    }
}

public sealed class MediaRow
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}
