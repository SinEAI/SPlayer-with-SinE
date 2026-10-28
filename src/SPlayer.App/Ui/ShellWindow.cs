using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;

namespace SPlayer.Ui;

internal sealed class ShellWindow : Window
{
    private readonly LaunchRequest _request;
    private readonly Session _session;
    private readonly VideoPresenter _presenter;
    private readonly PlayerSurface _player;
    private readonly NavigationView _nav = new()
    {
        PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
        ExpandedModeThresholdWidth = 800,
        CompactModeThresholdWidth = 640,
        CompactPaneLength = 48,
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        IsSettingsVisible = false,
        IsPaneToggleButtonVisible = true,
        OpenPaneLength = 208,
    };
    private readonly NavigationViewItem _recentItem;
    private readonly NavigationViewItem _playlistItem;
    private readonly NavigationViewItem _settingsItem;
    private readonly Grid _root = new();
    private readonly Grid _pages = new() { Background = Kit.White };
    private readonly Grid _recentPage;
    private readonly Grid _playlistPage;
    private readonly Grid _settingsPage;
    private readonly ListView _recentList = new() { SelectionMode = ListViewSelectionMode.None };
    private readonly ListView _playlistList = new();
    private readonly Border _shellDrag = new()
    {
        Height = Kit.TitleBarHeight,
        Margin = new Thickness(48, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Colors.Transparent),
    };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WindowChrome? _chrome;
    private bool _playerVisible;
    private bool _fullscreen;
    private bool _wasMaximized;
    private bool _closing;
    private bool _armed;
    private bool _pausedForSleep;
    private bool _navGuard;
    private bool _settingsGuard = true;
    private RectInt32 _restore;
    private string _page = "recent";
    private string _playlistKey = "";
    private RectInt32 _shellBounds;
    private bool _keepShellBounds;
    private string _fitKey = "";
    private string _recentKey = "";
    private (bool Video, bool Open)? _captionState;
    private readonly TextBlock _continueTitle = Kit.Text("随时继续", 14);
    private readonly TextBlock _continueDetail = Kit.Text("打开文件，开始播放", 12, secondary: true);
    private readonly Button _continueButton = new();
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Warning };

    internal ShellWindow(LaunchRequest request)
    {
        _request = request;
        Title = "SPlayer";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SPlayer.ico"));
        _session = new Session();
        _presenter = new VideoPresenter(_session.Player);
        _player = new PlayerSurface(_session, _presenter) { Visibility = Visibility.Collapsed };
        _presenter.Failed += message => { _session.Player.SetFlag("pause", true); _notice.Message = message; _notice.IsOpen = true; };
        _presenter.Recovered += () => _player.ShowNotice("画面输出已恢复");
        _recentItem = Item("最近播放", "recent");
        _playlistItem = Item("播放列表", "playlist");
        _settingsItem = Item("设置", "settings");
        _recentPage = BuildRecent();
        _playlistPage = BuildPlaylist();
        _settingsPage = BuildSettings();
        _settingsGuard = false;
        Compose();
        _session.Changed += OnSession;
        _session.Failed += OnFailed;
        _session.Notice += ShowError;
        App.UnexpectedError += ShowError;
        _player.BackRequested += BackToShell;
        _player.FullscreenToggle += ToggleFullscreen;
        _player.OpenFilesRequested += async () => await OpenFiles();
        _player.OpenFolderRequested += async () => await OpenFolder();
        _player.OpenUrlRequested += async () => await OpenUrl();
        _player.SubtitleRequested += async () => await LoadSubtitle();
        _player.ChromeChanged += () =>
        {
            UpdateCursor();
            PaintCaption();
        };
        _clock.Tick += (_, _) =>
        {
            _session.Pump();
            if (_playerVisible) _player.Tick(_session.Clock);
            UpdateCursor();
        };
        _clock.Start();
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;
        Place();
        if (request.PlayerOnly) ShowPlayer();
        else ShowPage("recent");
    }

    private IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    private void Compose()
    {
        var clear = new SolidColorBrush(Colors.Transparent);
        _nav.Resources["NavigationViewDefaultPaneBackground"] = clear;
        _nav.Resources["NavigationViewExpandedPaneBackground"] = clear;
        _nav.Resources["NavigationViewContentBackground"] = Kit.White;
        _nav.Resources["NavigationViewContentGridBorderBrush"] = clear;
        _nav.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(0);
        _nav.Resources["NavigationViewContentGridCornerRadius"] = new CornerRadius(0);
        var header = new Grid { Height = 44 };
        header.Children.Add(new TextBlock
        {
            Text = "SPlayer",
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Kit.Secondary,
        });
        _nav.PaneHeader = header;
        _nav.MenuItems.Add(_recentItem);
        _nav.MenuItems.Add(_playlistItem);
        _nav.FooterMenuItems.Add(_settingsItem);
        var continueContent = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
        continueContent.Children.Add(Kit.Text("继续播放", 11, secondary: true));
        continueContent.Children.Add(_continueTitle);
        continueContent.Children.Add(_continueDetail);
        _continueTitle.MaxWidth = 148;
        _continueDetail.MaxWidth = 148;
        _continueButton.Content = continueContent;
        _continueButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _continueButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _continueButton.Margin = new Thickness(8, 12, 8, 12);
        _continueButton.Padding = new Thickness(0);
        _continueButton.BorderThickness = new Thickness(1);
        _continueButton.BorderBrush = Kit.Divider;
        _continueButton.Background = Kit.Clear;
        _continueButton.Click += async (_, _) =>
        {
            if (_session.Active && _session.Facts.FileLoaded) ShowPlayer();
            else if (_session.History.Entries.FirstOrDefault(ResumePolicy.ShouldResume) is { } entry)
            { _session.PlayPath(entry.Path); ShowPlayer(); }
            else await OpenFiles();
        };
        _nav.PaneFooter = _continueButton;
        _nav.PaneOpening += (_, _) => _continueButton.Visibility = Visibility.Visible;
        _nav.PaneClosing += (_, _) => _continueButton.Visibility = Visibility.Collapsed;
        _nav.Content = _pages;
        _pages.Children.Add(_recentPage);
        _pages.Children.Add(_playlistPage);
        _pages.Children.Add(_settingsPage);
        _nav.SelectionChanged += OnNav;
        _root.Children.Add(_nav);
        _root.Children.Add(_player);
        _root.Children.Add(_shellDrag);
        _notice.VerticalAlignment = VerticalAlignment.Top;
        _notice.Margin = new Thickness(56, 40, 16, 0);
        _root.Children.Add(_notice);
        _root.AllowDrop = true;
        _root.DragOver += (_, e) =>
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        };
        _root.Drop += async (_, e) => await DropFiles(e);
        _root.PreviewKeyDown += OnKey;
        _root.PointerWheelChanged += OnWheel;
        Content = _root;
        _navGuard = true;
        _nav.SelectedItem = _recentItem;
        _navGuard = false;
    }

    private static NavigationViewItem Item(string text, string tag) => new()
    {
        Content = text, Tag = tag,
        Icon = new SymbolIcon(tag switch { "recent" => Symbol.Clock, "playlist" => Symbol.List, _ => Symbol.Setting }),
    };

    private async void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        if (_chrome is null)
        {
            _chrome = new WindowChrome(Hwnd);
            _chrome.Suspending += OnSuspend;
            _chrome.Resuming += OnResume;
            _chrome.Media += OnMedia;
        }
        if (_armed || e.WindowActivationState == WindowActivationState.Deactivated) return;
        _armed = true;
        if (_request.Media.Length > 0)
        {
            await _session.PlayFromOpenWith(_request.Media);
            if (_closing) return;
            ShowPlayer();
        }
        if (_request.Smoke) StartSmoke();
        else if (_request.Audit) StartAudit();
    }

    private void OnSession()
    {
        if (_playerVisible)
        {
            _player.Refresh(_session.Facts);
            var name = MediaPaths.DisplayName(_session.Facts.Path);
            Title = string.IsNullOrWhiteSpace(name) ? "SPlayer" : name + " — SPlayer";
            PaintCaption();
            FitCurrentVideo();
        }
        if (!_playerVisible)
        {
            UpdateContinue();
            if (_recentPage.Visibility == Visibility.Visible) FillRecent();
            if (_playlistPage.Visibility == Visibility.Visible) FillPlaylist();
        }
    }

    private void ShowError(string message)
    {
        if (_closing) return;
        _notice.Message = message; _notice.IsOpen = true;
    }

    private void OnFailed(string title, string summary, string detail)
    {
        _player.Refresh(_session.Facts);
    }

    private void UpdateContinue()
    {
        var active = _session.Active && _session.Facts.FileLoaded;
        var entry = _session.History.Entries.FirstOrDefault(ResumePolicy.ShouldResume);
        _continueTitle.Text = active ? MediaPaths.DisplayName(_session.Facts.Path) : entry?.Title ?? "随时继续";
        _continueDetail.Text = active ? "返回播放 · " + ClockText.Format(_session.Clock.Position) :
            entry is null ? "打开文件，开始播放" : "继续观看 · " + ClockText.Format(entry.Position);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_continueButton, _continueTitle.Text + "，" + _continueDetail.Text);
        ToolTipService.SetToolTip(_continueButton, _continueTitle.Text);
        _continueButton.Visibility = _nav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPage(string tag)
    {
        var changed = _page != tag || _playerVisible;
        _page = tag;
        _playerVisible = false;
        _clock.Interval = TimeSpan.FromMilliseconds(500);
        _player.Visibility = Visibility.Collapsed;
        _nav.Visibility = Visibility.Visible;
        _shellDrag.Visibility = Visibility.Visible;
        _recentPage.Visibility = tag == "recent" ? Visibility.Visible : Visibility.Collapsed;
        _playlistPage.Visibility = tag == "playlist" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new SidebarBackdrop();
            _root.Background = null;
        }
        else _root.Background = Kit.Chrome;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_shellDrag);
        PaintCaption();
        Title = "SPlayer";
        FillRecent();
        FillPlaylist();
        _navGuard = true;
        _nav.SelectedItem = tag switch
        {
            "playlist" => _playlistItem,
            "settings" => _settingsItem,
            _ => _recentItem,
        };
        _navGuard = false;
        UpdateContinue();
        if (changed)
        {
            var page = tag == "recent" ? _recentPage : tag == "playlist" ? _playlistPage : _settingsPage;
            page.Opacity = 0;
            Motion.Fade(page, true);
        }
    }

    private void ShowPlayer()
    {
        var returning = !_playerVisible;
        _playerVisible = true;
        _clock.Interval = TimeSpan.FromMilliseconds(100);
        _nav.Visibility = Visibility.Collapsed;
        _shellDrag.Visibility = Visibility.Collapsed;
        _player.Visibility = Visibility.Visible;
        _player.AllowBack = !_request.PlayerOnly;
        SystemBackdrop = null;
        _root.Background = _player.ShowingVideo ? new SolidColorBrush(Colors.Black) : Kit.White;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_player.DragRegion);
        PaintCaption();
        if (returning) _session.ReturnToPlayer();
        if (returning && !_fullscreen && !_request.PlayerOnly && !_keepShellBounds)
        {
            _shellBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            _keepShellBounds = true;
        }
        _player.Refresh(_session.Facts);
        _player.Tick(_session.Clock);
    }

    private void PaintCaption()
    {
        var video = _playerVisible && _player.ShowingVideo;
        var state = (video, _player.ChromeOpen);
        if (_captionState == state) return;
        _captionState = state;
        var ink = !video ? Colors.Black : _player.ChromeOpen ? Colors.White : Colors.Transparent;
        var bar = AppWindow.TitleBar;
        // The light theme otherwise leaves the system border as a one-pixel white line.
        var frame = video ? Colors.Black : Color.FromArgb(255, 0xF2, 0xF2, 0xF2);
        var caption = new SolidColorBrush(frame);
        Application.Current.Resources["WindowCaptionBackground"] = caption;
        Application.Current.Resources["WindowCaptionBackgroundDisabled"] = caption;
        bar.BackgroundColor = frame;
        bar.InactiveBackgroundColor = frame;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ink;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedForegroundColor = ink;
        bar.ButtonInactiveForegroundColor = video && !_player.ChromeOpen ? Colors.Transparent : Color.FromArgb(255, 0x8A, 0x8A, 0x8A);
        bar.ButtonHoverBackgroundColor = Color.FromArgb(video ? (byte)0x28 : (byte)0x19, 0, 0, 0);
        if (video) bar.ButtonHoverBackgroundColor = Color.FromArgb(0x28, 255, 255, 255);
        bar.ButtonPressedBackgroundColor = video ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x33, 0, 0, 0);
    }

    private void BackToShell()
    {
        if (_request.PlayerOnly) return;
        if (_fullscreen) ExitFullscreen();
        _session.LeavePlayer();
        if (_keepShellBounds) AppWindow.MoveAndResize(_shellBounds);
        ShowPage(_page);
    }

    private void OnNav(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_navGuard) return;
        var tag = (e.SelectedItem as NavigationViewItem)?.Tag as string ?? "recent";
        if (_playerVisible) _session.LeavePlayer();
        ShowPage(tag);
    }

    private async Task OpenFiles()
    {
        var files = await Pickers.OpenMedia(Hwnd);
        if (files.Count == 0) return;
        _session.PlayPaths(files);
        ShowPlayer();
    }

    private async Task OpenFolder()
    {
        var folder = await Pickers.Folder(Hwnd);
        if (string.IsNullOrWhiteSpace(folder)) return;
        IReadOnlyList<string> files;
        try { files = await Task.Run(() => Directory.EnumerateFiles(folder).Where(MediaPaths.IsMedia).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()); }
        catch (Exception ex) { await Dialogs.Alert(_root.XamlRoot, "无法读取文件夹", "请检查路径、网络连接和访问权限。", ex.Message); return; }
        if (files.Count == 0)
        {
            if (_root.XamlRoot is not null) await Dialogs.Alert(_root.XamlRoot, "没有可播放的文件", "这个文件夹的第一层里没有支持的媒体文件。", folder);
            return;
        }
        _session.PlayPaths(files);
        ShowPlayer();
    }

    private async Task OpenUrl()
    {
        if (_root.XamlRoot is null) return;
        var url = await Dialogs.AskUrl(_root.XamlRoot);
        if (url is null)
        {
            return;
        }
        _session.PlayPaths([url]);
        ShowPlayer();
    }

    private async Task LoadSubtitle()
    {
        var path = await Pickers.OpenSubtitle(Hwnd);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { _session.LoadSubtitle(path); }
        catch (Exception ex)
        {
            Log.Write(ex.ToString());
            if (_root.XamlRoot is not null) await Dialogs.Alert(_root.XamlRoot, "字幕没有加载", "这个字幕文件无法加入当前影片。", ex.Message);
        }
    }

    private async Task DropFiles(DragEventArgs e)
    {
        var paths = new List<string>();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file) paths.Add(file.Path);
                    else if (item is StorageFolder folder) paths.AddRange(await Task.Run(() => MediaPaths.MediaInFolder(folder.Path)));
                }
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = (await e.DataView.GetTextAsync()).Trim();
                if (text.Length > 0) paths.Add(text);
            }
        }
        catch (Exception ex) { Log.Write(ex.Message); }
        var media = paths.Where(p => MediaPaths.IsMedia(p) || MediaPaths.IsHttp(p) || MediaPaths.IsUnc(p)).ToArray();
        var subs = paths.Where(MediaPaths.IsSubtitle).ToArray();
        if (media.Length == 0 && subs.Length > 0 && _session.Active)
        {
            foreach (var sub in subs) _session.LoadSubtitle(sub);
            return;
        }
        if (media.Length == 0) return;
        _session.PlayPaths(media.Concat(subs).ToArray());
        ShowPlayer();
    }

    private Grid BuildRecent()
    {
        var page = Page("最近播放", "打开过的文件会留在这台电脑上。");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(Kit.Inset, 0, Kit.Inset, 12) };
        bar.Children.Add(Kit.Button("打开文件", async () => await OpenFiles(), Kit.G.Open));
        bar.Children.Add(Kit.Button("打开文件夹", async () => await OpenFolder(), Kit.G.Folder));
        bar.Children.Add(Kit.Button("打开地址", async () => await OpenUrl(), Kit.G.Link));
        bar.Children.Add(Kit.Button("清空记录", ClearHistory, Kit.G.Delete));
        var commands = new ScrollViewer
        {
            Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(commands, 1);
        page.Children.Add(commands);
        _recentList.IsItemClickEnabled = true;
        _recentList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is MediaRow row && !string.IsNullOrWhiteSpace(row.Path))
            {
                _session.PlayPath(row.Path);
                ShowPlayer();
            }
        };
        _recentList.ItemTemplate = RowTemplate();
        _recentList.Margin = new Thickness(Kit.Inset - 12, 0, Kit.Inset, 24);
        Grid.SetRow(_recentList, 2);
        page.Children.Add(_recentList);
        return page;
    }

    private Grid BuildPlaylist()
    {
        var page = Page("播放列表", "列表保存在本机，不会上传。");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(Kit.Inset, 0, Kit.Inset, 12) };
        bar.Children.Add(Kit.Button("添加文件", async () => await AddFiles(), Kit.G.Add));
        bar.Children.Add(Kit.Button("添加文件夹", async () => await AddFolder(), Kit.G.Folder));
        bar.Children.Add(Kit.Button("打开列表", async () => await ImportList(), Kit.G.Open));
        bar.Children.Add(Kit.Button("保存列表", async () => await ExportList(), Kit.G.Save));
        bar.Children.Add(Kit.Button("删除", RemoveSelected, Kit.G.Delete));
        bar.Children.Add(Kit.Button("播放", PlaySelected, Kit.G.Play));
        var commands = new ScrollViewer
        {
            Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(commands, 1);
        page.Children.Add(commands);
        _playlistList.CanDragItems = true;
        _playlistList.CanReorderItems = true;
        _playlistList.AllowDrop = true;
        _playlistList.SelectionMode = ListViewSelectionMode.Single;
        _playlistList.ItemTemplate = RowTemplate();
        _playlistList.DoubleTapped += (_, e) =>
        {
            if (ItemFrom(e.OriginalSource as DependencyObject) is MediaRow row)
            {
                _session.PlayIndex(row.Index);
                ShowPlayer();
            }
        };
        _playlistList.DragItemsCompleted += (_, _) =>
        {
            if (_playlistList.ItemsSource is IEnumerable<MediaRow> rows)
            { _session.Reorder(rows.Select(r => r.Path).ToArray()); _playlistKey = ""; FillPlaylist(); }
        };
        _playlistList.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Delete) RemoveSelected();
        };
        _playlistList.Margin = new Thickness(Kit.Inset - 12, 0, Kit.Inset, 24);
        Grid.SetRow(_playlistList, 2);
        page.Children.Add(_playlistList);
        return page;
    }

    private Grid BuildSettings()
    {
        var page = Page("设置", "播放、字幕和这台电脑上的记录。");
        var stack = new StackPanel { Margin = new Thickness(Kit.Inset, 0, Kit.Inset, 32), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 0 };
        stack.Children.Add(Toggle("硬件解码", "优先使用显卡。失败时自动改用软件解码。", _session.Settings.HardwareDecoding, v => _session.ApplyHardware(v)));
        stack.Children.Add(Toggle("记住播放位置", "再次打开未看完的文件时从上次位置继续。", _session.Settings.RememberPosition, v => _session.Settings.RememberPosition = v));
        stack.Children.Add(Toggle("记录播放历史", "关闭后不再写入历史，也不会恢复进度。", _session.Settings.RecordHistory, v => _session.Settings.RecordHistory = v));
        stack.Children.Add(Toggle("滚轮调节音量", "指针在画面上时，滚轮改变音量。", _session.Settings.WheelAdjustsVolume, v => _session.Settings.WheelAdjustsVolume = v));
        stack.Children.Add(Toggle("跟随视频比例", "从「打开方式」打开时，自动调整窗口以铺满完整画面。关闭后保留上次窗口比例。", _session.Settings.FitWindowToVideo, v => _session.Settings.FitWindowToVideo = v));
        var seek = new NumberBox { Value = _session.Settings.SeekSeconds, Minimum = 1, Maximum = 60, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };
        seek.ValueChanged += (_, e) =>
        {
            if (_settingsGuard || double.IsNaN(e.NewValue)) return;
            _session.Settings.SeekSeconds = (int)Math.Clamp(e.NewValue, 1, 60);
            _session.SaveSoon();
        };
        stack.Children.Add(Row("方向键快进", "左右方向键跳过的秒数。", seek));
        var pages = new ComboBox { Width = 160 };
        foreach (var name in new[] { "自动", "UTF-8", "GB18030", "Big5" }) pages.Items.Add(name);
        pages.SelectedIndex = CodeIndex(_session.Settings.SubtitleCodepage);
        pages.SelectionChanged += (_, _) =>
        {
            if (_settingsGuard) return;
            _session.SetSubtitleCodepage(pages.SelectedIndex switch { 1 => "utf-8", 2 => "gb18030", 3 => "big5", _ => "auto" });
        };
        stack.Children.Add(Row("字幕编码", "外挂字幕乱码时可以指定编码。", pages));
        var scale = new Slider { Minimum = 0.5, Maximum = 2.5, Value = _session.Settings.SubtitleScale, Width = 180, StepFrequency = 0.1 };
        scale.ValueChanged += (_, e) => { if (!_settingsGuard) _session.SetSubtitleScale(e.NewValue); };
        stack.Children.Add(Row("字幕大小", "相对默认字号的比例。", scale));
        var channels = new ComboBox { Width = 160 };
        foreach (var name in new[] { "自动", "立体声", "单声道" }) channels.Items.Add(name);
        channels.SelectedIndex = _session.Settings.AudioChannels switch { "stereo" => 1, "mono" => 2, _ => 0 };
        channels.SelectionChanged += (_, _) =>
        {
            if (_settingsGuard) return;
            _session.ApplyChannels(channels.SelectedIndex switch { 1 => "stereo", 2 => "mono", _ => "auto-safe" });
        };
        stack.Children.Add(Row("声道", "多声道会按声卡能力输出。", channels));
        stack.Children.Add(Row("截图文件夹", "截图保存为 PNG。", Kit.Button("选择文件夹", async () => await ChooseShots(), Kit.G.Folder)));
        stack.Children.Add(Row("播放历史", "只保存在本机。", Kit.Button("清空历史", ClearHistory, Kit.G.Delete)));
        stack.Children.Add(Row("默认应用", "登记 SPlayer，并打开 Windows「默认应用」。请在「视频播放器」里选择它。", Kit.Button("打开默认应用设置", OfferDefault, Kit.G.Open)));
        var about = Kit.Text("SPlayer 0.1.2.1    SinE\n播放内核为 mpv 与 FFmpeg，许可证为 GPL-2.0-or-later。没有账号、广告或遥测。", 13, secondary: true);
        about.TextWrapping = TextWrapping.Wrap;
        about.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(about);
        var scroll = new ScrollViewer { Content = stack };
        Grid.SetRow(scroll, 1);
        Grid.SetRowSpan(scroll, 2);
        page.Children.Add(scroll);
        return page;
    }

    private static Grid Page(string title, string summary)
    {
        var page = new Grid { Background = Kit.White };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = Kit.Title(title);
        var text = Kit.Text(summary, 13, secondary: true);
        text.Margin = new Thickness(0, 6, 0, 0);
        var block = new StackPanel { Margin = new Thickness(Kit.Inset, Kit.TitleBarHeight + 16, Kit.Inset, 16) };
        block.Children.Add(heading);
        block.Children.Add(text);
        page.Children.Add(block);
        return page;
    }

    private Border Toggle(string title, string detail, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "", OffContent = "" };
        toggle.Toggled += (_, _) => { changed(toggle.IsOn); _session.SaveSoon(); };
        return Row(title, detail, toggle);
    }

    private static Border Row(string title, string detail, FrameworkElement control)
    {
        var grid = new Grid { Padding = new Thickness(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Kit.Text(title, 15));
        var sub = Kit.Text(detail, 12, secondary: true);
        sub.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(sub);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
        control.Margin = new Thickness(16, 0, 0, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return new Border { Child = grid, BorderBrush = Kit.Divider, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private void FillRecent()
    {
        var entries = _session.History.Entries;
        var key = string.Join("\n", entries.Select(e => e.Path + "|" + e.Position + "|" + e.PlayedAt));
        if (key == _recentKey && _recentList.Items.Count > 0) return;
        _recentKey = key;
        var rows = entries.Select(entry => new MediaRow
        {
            Path = entry.Path,
            Title = string.IsNullOrWhiteSpace(entry.Title) ? MediaPaths.DisplayName(entry.Path) : entry.Title,
            Detail = entry.Completed
                ? "已看完  ·  " + entry.PlayedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : ClockText.Format(entry.Position) + " / " + ClockText.Format(entry.Duration) + "  ·  " + entry.PlayedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
        }).ToList();
        _recentList.ItemsSource = rows.Count == 0
            ? new[] { new MediaRow { Title = "还没有播放记录", Detail = "打开一个文件后，它会出现在这里。" } }
            : rows;
    }

    private void FillPlaylist()
    {
        var items = _session.Playlist.Items;
        var key = _session.Playlist.Index + "\n" + string.Join("\n", items);
        if (key == _playlistKey && _playlistList.Items.Count == items.Count) return;
        _playlistKey = key;
        var rows = new List<MediaRow>();
        for (var i = 0; i < items.Count; i++)
        {
            rows.Add(new MediaRow
            {
                Index = i,
                Path = items[i],
                Title = MediaPaths.DisplayName(items[i]),
                Detail = i == _session.Playlist.Index ? "当前" : "",
            });
        }
        _playlistList.ItemsSource = rows;
    }

    private async Task AddFiles()
    {
        var files = await Pickers.OpenMedia(Hwnd);
        if (files.Count == 0) return;
        _session.AddToPlaylist(files);
        _playlistKey = "";
        FillPlaylist();
    }

    private async Task AddFolder()
    {
        var folder = await Pickers.Folder(Hwnd);
        if (string.IsNullOrWhiteSpace(folder)) return;
        _session.AddToPlaylist(await Task.Run(() => MediaPaths.MediaInFolder(folder)));
        _playlistKey = "";
        FillPlaylist();
    }

    private async Task ImportList()
    {
        var path = await Pickers.OpenPlaylist(Hwnd);
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            _session.Playlist.Replace(await Task.Run(() => PlaylistFile.Read(path)));
            _session.Save();
            _playlistKey = "";
            FillPlaylist();
        }
        catch (Exception ex)
        {
            if (_root.XamlRoot is not null) await Dialogs.Alert(_root.XamlRoot, "无法打开播放列表", "这个列表文件无法读取。", ex.Message);
        }
    }

    private async Task ExportList()
    {
        var path = await Pickers.SavePlaylist(Hwnd);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { PlaylistFile.Write(path, _session.Playlist.Items); }
        catch (Exception ex)
        {
            if (_root.XamlRoot is not null) await Dialogs.Alert(_root.XamlRoot, "无法保存播放列表", "没有写入这个文件。", ex.Message);
        }
    }

    private void RemoveSelected()
    {
        if (_playlistList.SelectedItem is not MediaRow row) return;
        _session.RemoveFromPlaylist(row.Index);
        _playlistKey = "";
        FillPlaylist();
    }

    private void PlaySelected()
    {
        var index = _playlistList.SelectedIndex;
        if (index < 0) index = _session.Playlist.Index;
        if (index < 0) return;
        _session.PlayIndex(index);
        ShowPlayer();
    }

    private void OfferDefault()
    {
        try { FileAssociations.Register(Environment.ProcessPath ?? ""); }
        catch (Exception ex) { Log.Write(ex.ToString()); }
        OpenDefaultApps();
    }

    private static void OpenDefaultApps()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write(ex.Message); }
    }

    private async void ClearHistory()
    {
        try { await _session.ClearHistory(); FillRecent(); UpdateContinue(); }
        catch (Exception ex) { await Dialogs.Alert(_root.XamlRoot, "清空失败", "记录未能清空，请检查磁盘和权限。", ex.Message); }
    }

    private async Task ChooseShots()
    {
        var folder = await Pickers.Folder(Hwnd);
        if (string.IsNullOrWhiteSpace(folder)) return;
        _session.Settings.ScreenshotDirectory = folder;
        _session.Save();
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (Typing(e.OriginalSource as DependencyObject)) return;
        if (Interactive(e.OriginalSource as DependencyObject) &&
            e.Key is VirtualKey.Space or VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down) return;
        var ctrl = Down(VirtualKey.Control);
        var step = Math.Max(1, _session.Settings.SeekSeconds);
        switch (e.Key)
        {
            case VirtualKey.Space when _playerVisible:
                _session.TogglePause();
                e.Handled = true;
                break;
            case VirtualKey.Left when _playerVisible:
                _session.SeekBy(ctrl ? -1 : -step);
                e.Handled = true;
                break;
            case VirtualKey.Right when _playerVisible:
                _session.SeekBy(ctrl ? 1 : step);
                e.Handled = true;
                break;
            case VirtualKey.Up when _playerVisible:
                _session.NudgeVolume(5);
                e.Handled = true;
                break;
            case VirtualKey.Down when _playerVisible:
                _session.NudgeVolume(-5);
                e.Handled = true;
                break;
            case VirtualKey.F when _playerVisible:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case VirtualKey.Escape when _fullscreen:
                ExitFullscreen();
                e.Handled = true;
                break;
            case VirtualKey.Escape when _playerVisible && !_request.PlayerOnly:
                BackToShell();
                e.Handled = true;
                break;
            case VirtualKey.M when _playerVisible:
                _session.ToggleMute();
                e.Handled = true;
                break;
            case VirtualKey.O when ctrl:
                _ = OpenFiles();
                e.Handled = true;
                break;
        }
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_playerVisible || !_session.Settings.WheelAdjustsVolume) return;
        if (Interactive(e.OriginalSource as DependencyObject) || Typing(e.OriginalSource as DependencyObject)) return;
        var delta = e.GetCurrentPoint(_root).Properties.MouseWheelDelta;
        if (delta == 0) return;
        _session.NudgeVolume(delta > 0 ? 4 : -4);
        e.Handled = true;
    }

    private void ToggleFullscreen()
    {
        if (_fullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_fullscreen || !_playerVisible) return;
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        _wasMaximized = presenter?.State == OverlappedPresenterState.Maximized;
        _restore = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        _fullscreen = true;
        _player.SetFullscreen(true);
        UpdateCursor();
    }

    private void ExitFullscreen()
    {
        if (!_fullscreen) return;
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_wasMaximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        else AppWindow.MoveAndResize(_restore);
        _fullscreen = false;
        _player.SetFullscreen(false);
        if (_chrome is not null) _chrome.HideCursor = false;
        PaintCaption();
    }

    private void UpdateCursor()
    {
        if (_chrome is null) return;
        _chrome.HideCursor = _fullscreen && _playerVisible && !_player.ChromeOpen;
    }

    private void OnSuspend()
    {
        _pausedForSleep = _playerVisible && !_session.Clock.Paused;
        if (_pausedForSleep) _session.TogglePause();
        _session.Checkpoint();
    }

    private void OnResume()
    {
        if (_pausedForSleep)
        {
            _pausedForSleep = false;
            if (_session.Clock.Paused) _session.TogglePause();
        }
    }

    private void OnMedia(MediaCommand command)
    {
        if (!_session.Active && command != MediaCommand.PlayPause) return;
        switch (command)
        {
            case MediaCommand.PlayPause: _session.TogglePause(); break;
            case MediaCommand.Stop: _session.StopPlayback(); break;
            case MediaCommand.Next: _session.Next(); break;
            case MediaCommand.Previous: _session.Previous(); break;
        }
    }

    private void Place()
    {
        double scale = Math.Max(1, GetDpiForWindow(Hwnd) / 96.0);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(480 * scale);
            presenter.PreferredMinimumHeight = (int)(270 * scale);
        }
        var saved = _request.PlayerOnly ? _session.Settings.Player : _session.Settings.Shell;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (saved.Width >= 480 && saved.Height >= 270)
        {
            var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(rect);
                if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter overlapped) overlapped.Maximize();
                return;
            }
        }
        int width = Math.Max((int)(area.Width * 0.55), (int)(800 * scale));
        int height = Math.Max((int)(area.Height * 0.78), (int)(560 * scale));
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    /// <summary>Size the client area to this video so the picture fills the window.</summary>
    private void FitCurrentVideo()
    {
        if (!_request.PlayerOnly || !_session.Settings.FitWindowToVideo || _fullscreen) return;
        var facts = _session.Facts;
        if (!facts.HasVideo || facts.Width < 2 || facts.Height < 2) return;
        var key = facts.Path + "\n" + facts.Width + "x" + facts.Height;
        if (key == _fitKey) return;
        if (FitToVideo(facts.Width, facts.Height)) _fitKey = key;
    }

    private bool FitToVideo(int videoWidth, int videoHeight)
    {
        if (!GetWindowRect(Hwnd, out var window) || !GetClientRect(Hwnd, out var client)) return false;
        var clientWidth = client.Right - client.Left;
        var clientHeight = client.Bottom - client.Top;
        if (clientWidth < 200 || clientHeight < 200) return false;
        var padX = (window.Right - window.Left) - clientWidth;
        var padY = (window.Bottom - window.Top) - clientHeight;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double aspect = videoWidth / (double)videoHeight;
        int maxClientW = Math.Max(480, (int)(area.Width * 0.86) - Math.Max(0, padX));
        int maxClientH = Math.Max(270, (int)(area.Height * 0.86) - Math.Max(0, padY));
        int targetH = maxClientH;
        int targetW = (int)Math.Round(targetH * aspect);
        if (targetW > maxClientW)
        {
            targetW = maxClientW;
            targetH = (int)Math.Round(targetW / aspect);
        }
        int outerW = targetW + Math.Max(0, padX);
        int outerH = targetH + Math.Max(0, padY);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(outerW, 320);
            presenter.PreferredMinimumHeight = Math.Min(outerH, 180);
            if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        }
        int x = area.X + Math.Max(0, (area.Width - outerW) / 2);
        int y = area.Y + Math.Max(0, (area.Height - outerH) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, outerW, outerH));
        return true;
    }

    private void Remember()
    {
        if (_fullscreen) ExitFullscreen();
        if (_playerVisible)
        {
            WritePlacement(_session.Settings.Player);
            if (!_request.PlayerOnly && _keepShellBounds)
            {
                var shell = _session.Settings.Shell;
                shell.Maximized = false;
                shell.X = _shellBounds.X;
                shell.Y = _shellBounds.Y;
                shell.Width = _shellBounds.Width;
                shell.Height = _shellBounds.Height;
            }
            return;
        }
        WritePlacement(_session.Settings.Shell);
    }

    private void WritePlacement(WindowPlacement place)
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var minimized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        place.Maximized = maximized;
        if (maximized || minimized) return;
        place.X = AppWindow.Position.X;
        place.Y = AppWindow.Position.Y;
        place.Width = AppWindow.Size.Width;
        place.Height = AppWindow.Size.Height;
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) return;
        args.Cancel = true;
        _closing = true;
        try
        {
            _clock.Stop();
            App.UnexpectedError -= ShowError;
            Remember();
            _chrome?.Dispose();
            _root.IsHitTestVisible = false;
            _session.PrepareClose();
            await Task.Run(() => { _presenter.Dispose(); _session.Dispose(); });
        }
        catch (Exception ex) { Log.Write(ex.ToString()); }
        Close();
    }

    private void StartSmoke()
    {
        var started = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += async (_, _) =>
        {
            var facts = _session.Facts;
            var clock = _session.Clock;
            var presented = _presenter.Presented;
            var ready = facts.FileLoaded && string.IsNullOrEmpty(facts.ErrorTitle) && (presented > 0 || !facts.HasVideo) && (clock.Position > 0.05 || !clock.Paused);
            var failed = !string.IsNullOrEmpty(facts.ErrorTitle);
            if (!ready && !failed && Environment.TickCount64 - started < 20000) return;
            timer.Stop();
            try
            {
                if (!string.IsNullOrWhiteSpace(_request.SmokePng)) _session.ScreenshotTo(_request.SmokePng);
            }
            catch (Exception ex) { Log.Write(ex.ToString()); }
            await Task.Delay(400);
            var lines = new[]
            {
                "file=" + _request.SmokeFile,
                "loaded=" + facts.FileLoaded,
                "presented=" + _presenter.Presented,
                "position=" + clock.Position.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "paused=" + clock.Paused,
                "hwdec=" + facts.Hwdec,
                "warning=" + (_session.Warning ?? ""),
                "codec=" + facts.VideoCodec,
                "audio=" + facts.AudioCodec,
                "size=" + facts.Width + "x" + facts.Height,
                "error=" + (facts.ErrorTitle ?? ""),
            };
            try { File.WriteAllText(_request.SmokeLog!, string.Join(Environment.NewLine, lines)); }
            catch (Exception ex) { Log.Write(ex.ToString()); }
            App.ExitCode = ready && !failed ? 0 : 1;
            Close();
        };
        timer.Start();
    }

    private async void StartAudit()
    {
        var started = Environment.TickCount64;
        while (_presenter.Presented < 1 && string.IsNullOrEmpty(_session.Facts.ErrorTitle) && Environment.TickCount64 - started < 15000)
            await Task.Delay(100);
        var before = WindowChrome.WindowRect(Hwnd);
        EnterFullscreen();
        await Task.Delay(800);
        var during = WindowChrome.WindowRect(Hwnd);
        var monitor = WindowChrome.MonitorRect(Hwnd);
        ExitFullscreen();
        await Task.Delay(500);
        var after = WindowChrome.WindowRect(Hwnd);
        var full = Near(during, monitor, 2);
        var restored = Near(after, before, 8);
        var lines = new[]
        {
            "fullscreen=" + full,
            "window=" + Rect(during),
            "monitor=" + Rect(monitor),
            "restore=" + restored,
            "before=" + Rect(before),
            "after=" + Rect(after),
            "presented=" + _presenter.Presented,
            "error=" + (_session.Facts.ErrorTitle ?? ""),
        };
        try { File.WriteAllText(_request.AuditLog!, string.Join(Environment.NewLine, lines)); }
        catch (Exception ex) { Log.Write(ex.ToString()); }
        App.ExitCode = full && restored && _presenter.Presented > 0 ? 0 : 1;
        Close();
    }

    private static bool Near(RectInt32 a, RectInt32 b, int slack) =>
        Math.Abs(a.X - b.X) <= slack && Math.Abs(a.Y - b.Y) <= slack &&
        Math.Abs(a.Width - b.Width) <= slack && Math.Abs(a.Height - b.Height) <= slack;

    private static string Rect(RectInt32 rect) => rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height;

    private static MediaRow? ItemFrom(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: MediaRow row }) return row;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static bool Interactive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Slider or ListViewItem or Button or ComboBox or ScrollViewer) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static bool Typing(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or NumberBox or AutoSuggestBox or PasswordBox or RichEditBox) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static bool Down(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private static int CodeIndex(string code) => code switch
    {
        "utf-8" => 1,
        "gb18030" => 2,
        "big5" => 3,
        _ => 0,
    };

    private static DataTemplate RowTemplate() => (DataTemplate)XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid Padding="8,10">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <TextBlock Text="{Binding Title}" FontSize="15" TextTrimming="CharacterEllipsis"/>
            <TextBlock Grid.Row="1" Text="{Binding Detail}" FontSize="12" Foreground="#FF606060" Margin="0,2,0,0" TextTrimming="CharacterEllipsis"/>
          </Grid>
        </DataTemplate>
        """);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
