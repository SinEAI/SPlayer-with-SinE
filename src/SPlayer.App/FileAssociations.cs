using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SPlayer;

/// <summary>
/// Registers SPlayer as a video and audio handler, then opens the Windows
/// association window. Windows 10 does not allow an app to take the default silently.
/// </summary>
internal static class FileAssociations
{
    internal const string AppId = "SinE.SPlayer";
    private const string Name = "SPlayer";
    private const string Capabilities = @"Software\SPlayer\Capabilities";

    internal static void Register(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) throw new InvalidOperationException("找不到 SPlayer.exe。");
        exe = Path.GetFullPath(exe);
        using var root = Registry.CurrentUser;
        using var classes = root.CreateSubKey(@"Software\Classes") ?? throw new InvalidOperationException("无法写入文件关联。");
        using var registered = root.CreateSubKey(@"Software\RegisteredApplications") ?? throw new InvalidOperationException("无法登记应用程序。");
        registered.SetValue(Name, Capabilities);
        using var caps = root.CreateSubKey(Capabilities) ?? throw new InvalidOperationException("无法写入应用能力。");
        caps.SetValue("ApplicationName", Name);
        caps.SetValue("ApplicationDescription", "SinE 本地影音播放器");
        caps.SetValue("ApplicationIcon", exe + ",0");
        using var map = caps.CreateSubKey("FileAssociations") ?? throw new InvalidOperationException("无法写入扩展名。");
        foreach (var ext in Extensions())
        {
            var prog = "SPlayer" + ext;
            using (var key = classes.CreateSubKey(prog))
            {
                key?.SetValue("", Kind(ext));
                key?.CreateSubKey("DefaultIcon")?.SetValue("", exe + ",0");
                key?.CreateSubKey(@"shell\open\command")?.SetValue("", "\"" + exe + "\" \"%1\"");
            }
            using var openWith = classes.CreateSubKey(ext + @"\OpenWithProgids");
            openWith?.SetValue(prog, "", RegistryValueKind.String);
            map.SetValue(ext, prog);
        }
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    internal static void Unregister()
    {
        using var root = Registry.CurrentUser;
        try { root.DeleteSubKeyTree(@"Software\SPlayer", throwOnMissingSubKey: false); }
        catch (Exception ex) { Log.Write(ex.Message); }
        try { root.OpenSubKey(@"Software\RegisteredApplications", writable: true)?.DeleteValue(Name, throwOnMissingValue: false); }
        catch (Exception ex) { Log.Write(ex.Message); }
        using var classes = root.OpenSubKey(@"Software\Classes", writable: true);
        if (classes is null) return;
        foreach (var ext in Extensions())
        {
            var prog = "SPlayer" + ext;
            try { classes.DeleteSubKeyTree(prog, throwOnMissingSubKey: false); }
            catch (Exception ex) { Log.Write(ex.Message); }
            try { classes.OpenSubKey(ext + @"\OpenWithProgids", writable: true)?.DeleteValue(prog, throwOnMissingValue: false); }
            catch (Exception ex) { Log.Write(ex.Message); }
        }
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    internal static bool ShowChooser()
    {
        var ui = (IApplicationAssociationRegistrationUI)new ApplicationAssociationRegistrationUI();
        return ui.LaunchAdvancedAssociationUI(Name) >= 0;
    }

    private static IEnumerable<string> Extensions() =>
        MediaPaths.VideoExtensions.Concat(MediaPaths.AudioExtensions).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Kind(string ext) =>
        MediaPaths.VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? "视频" : "音频";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [ComImport]
    [Guid("1968106d-f3b5-44cf-890e-116fcb9ecef1")]
    private class ApplicationAssociationRegistrationUI
    {
    }

    [ComImport]
    [Guid("1f76a169-f994-40ac-8fc8-0959e8874710")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistrationUI
    {
        [PreserveSig]
        int LaunchAdvancedAssociationUI([MarshalAs(UnmanagedType.LPWStr)] string appRegistryName);
    }
}
