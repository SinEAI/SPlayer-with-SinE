using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SPlayer.Ui;

internal static class Pickers
{
    public static async Task<IReadOnlyList<string>> OpenMedia(IntPtr hwnd)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        InitializeWithWindow.Initialize(picker, hwnd);
        foreach (var ext in MediaPaths.VideoExtensions.Concat(MediaPaths.AudioExtensions).Distinct(StringComparer.OrdinalIgnoreCase))
            picker.FileTypeFilter.Add(ext);
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).Where(p => p.Length > 0).ToArray() ?? [];
    }

    public static async Task<string?> OpenSubtitle(IntPtr hwnd)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        InitializeWithWindow.Initialize(picker, hwnd);
        foreach (var ext in MediaPaths.SubtitleExtensions) picker.FileTypeFilter.Add(ext);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> OpenPlaylist(IntPtr hwnd)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> SavePlaylist(IntPtr hwnd)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            SuggestedFileName = "播放列表",
            DefaultFileExtension = ".m3u",
        };
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeChoices.Add("播放列表", new List<string> { ".m3u" });
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> Folder(IntPtr hwnd)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
