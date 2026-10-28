using System.Diagnostics;
using Xunit;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace SPlayer.Tests;

/// <summary>
/// Desktop pass. It is skipped unless SPLAYER_UI=1, because it needs a real display.
/// It opens the shell and several aspect ratios, then checks the window and the picture edges.
/// </summary>
public class DetailPass
{
    [DesktopFact]
    public void ShellOmitsNowPlaying_AndEachVideoFillsItsOwnFrame()
    {
        if (Environment.GetEnvironmentVariable("SPLAYER_UI") != "1") return;
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var root = RepoRoot();
        var exe = FindExe(root);
        var shots = Path.Combine(root, "artifacts", "verify", "detail");
        Directory.CreateDirectory(shots);
        PrepareRatios(root);
        var report = new StringBuilder();
        var failures = new List<string>();

        using (var shell = Launch(exe, root, []))
        {
            var hwnd = WaitWindow(shell);
            Raise(hwnd);
            Thread.Sleep(800);
            SaveShot(hwnd, Path.Combine(shots, "shell.png"));
            var names = Names(hwnd);
            report.AppendLine("shell: " + string.Join(" | ", names));
            if (names.Any(n => n.Contains("正在播放", StringComparison.Ordinal)))
                failures.Add("主界面仍有「正在播放」");
            if (!names.Any(n => n.Contains("最近播放", StringComparison.Ordinal)))
                failures.Add("主界面没有「最近播放」");
            if (!names.Any(n => n.Contains("播放列表", StringComparison.Ordinal)))
                failures.Add("主界面没有「播放列表」");
            if (!names.Any(n => n.Contains("设置", StringComparison.Ordinal)))
                failures.Add("主界面没有「设置」");
        }

        var cases = new (string File, double Aspect)[]
        {
            ("h264.mp4", 16.0 / 9.0),
            ("ratio-43.mp4", 4.0 / 3.0),
            ("ratio-239.mp4", 1434.0 / 600.0),
        };
        foreach (var item in cases)
        {
            var path = Path.Combine(root, "testdata", "samples", item.File);
            Assert.True(File.Exists(path), "缺少 " + path);
            using var player = Launch(exe, root, [path]);
            var hwnd = WaitWindow(player);
            var matched = false;
            var client = new Native.RECT();
            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(200);
                if (!Native.GetClientRect(hwnd, out client)) continue;
                var width = client.Right - client.Left;
                var height = client.Bottom - client.Top;
                if (height < 200) continue;
                var aspect = width / (double)height;
                if (Math.Abs(aspect - item.Aspect) / item.Aspect < 0.03)
                {
                    matched = true;
                    break;
                }
            }
            Native.GetClientRect(hwnd, out client);
            var cw = client.Right - client.Left;
            var ch = client.Bottom - client.Top;
            var got = ch == 0 ? 0 : cw / (double)ch;
            report.AppendLine($"{item.File}: client {cw}x{ch} aspect {got:0.000} want {item.Aspect:0.000} matched {matched}");
            if (!matched) failures.Add(item.File + " 窗口比例不是画面比例");

            Raise(hwnd);
            Native.GetWindowRect(hwnd, out var window);
            Click((window.Left + window.Right) / 2, (window.Top + window.Bottom) / 2);
            Thread.Sleep(500);
            var clean = Path.Combine(shots, Path.GetFileNameWithoutExtension(item.File) + "-clean.png");
            SaveClient(hwnd, clean);
            var bars = BlackBars(clean);
            report.AppendLine($"{item.File}: black-edge L{bars.Left} T{bars.Top} R{bars.Right} B{bars.Bottom}");
            if (bars.Left > 6 || bars.Top > 6 || bars.Right > 6 || bars.Bottom > 6)
                failures.Add(item.File + " 画面边缘有黑边");
            var names = Names(hwnd);
            if (names.Any(n => n.Contains("正在播放", StringComparison.Ordinal)))
                failures.Add(item.File + " 播放窗口仍有「正在播放」");
        }

        File.WriteAllText(Path.Combine(shots, "report.txt"), report.ToString(), Encoding.UTF8);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures) + Environment.NewLine + report);
    }

    private static LaunchScope Launch(string exe, string root, IReadOnlyList<string> args)
    {
        var data = Path.Combine(root, "artifacts", "verify", "detail-profile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
        };
        start.Environment["SPLAYER_DATA"] = data;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return new LaunchScope(Process.Start(start) ?? throw new InvalidOperationException("无法启动"));
    }

    private static void PrepareRatios(string root)
    {
        var ffmpeg = Path.Combine(root, "third_party", "ffmpeg", "ffmpeg.exe");
        var dir = Path.Combine(root, "testdata", "samples");
        Encode(ffmpeg, dir, "ratio-43.mp4", 960, 720);
        Encode(ffmpeg, dir, "ratio-239.mp4", 1434, 600);
    }

    private static void Encode(string ffmpeg, string dir, string name, int width, int height)
    {
        var dest = Path.Combine(dir, name);
        if (File.Exists(dest) && new FileInfo(dest).Length > 1000) return;
        if (!File.Exists(ffmpeg)) throw new InvalidOperationException("缺少 ffmpeg.exe，无法生成 " + name);
        var source = $"testsrc2=size={width}x{height}:rate=25:duration=4";
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-y", "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", source, "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4", "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", dest })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg");
        process.WaitForExit(60000);
        if (process.ExitCode != 0 || !File.Exists(dest)) throw new InvalidOperationException("生成失败 " + name);
    }

    private static IntPtr WaitWindow(LaunchScope process)
    {
        for (var i = 0; i < 50; i++)
        {
            process.Process.Refresh();
            var hwnd = Find(process.Process.Id);
            if (hwnd != IntPtr.Zero) return hwnd;
            Thread.Sleep(200);
        }
        throw new InvalidOperationException("窗口没有出现");
    }

    private static IntPtr Find(int processId)
    {
        var found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != (uint)processId || !Native.IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(256);
            Native.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) found = hwnd;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void Raise(IntPtr hwnd)
    {
        Native.ShowWindow(hwnd, 9);
        Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
        Native.SetForegroundWindow(hwnd);
    }

    private static List<string> Names(IntPtr hwnd)
    {
        var script = "$e = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]" + hwnd.ToInt64() + "); $e.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }";
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Add-Type -AssemblyName UIAutomationClient; " + script);
        using var process = Process.Start(info);
        if (process is null) return ["uia-failed: no powershell"];
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit(15000);
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    private static void Click(int x, int y)
    {
        Native.SetCursorPos(x, y);
        Thread.Sleep(40);
        Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SaveShot(IntPtr hwnd, string path)
    {
        Native.GetWindowRect(hwnd, out var rect);
        Save(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, path);
    }

    private static void SaveClient(IntPtr hwnd, string path)
    {
        Native.GetClientRect(hwnd, out var client);
        var origin = new Native.POINT();
        Native.ClientToScreen(hwnd, ref origin);
        Save(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top, path);
    }

    private static void Save(int x, int y, int width, int height, string path)
    {
        if (width < 50 || height < 50) return;
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static (int Left, int Top, int Right, int Bottom) BlackBars(string path)
    {
        using var bitmap = new Bitmap(path);
        return (
            Run(bitmap, horizontal: false, fromStart: true),
            Run(bitmap, horizontal: true, fromStart: true),
            Run(bitmap, horizontal: false, fromStart: false),
            Run(bitmap, horizontal: true, fromStart: false));
    }

    private static int Run(Bitmap bitmap, bool horizontal, bool fromStart)
    {
        var limit = horizontal ? bitmap.Height / 3 : bitmap.Width / 3;
        var count = 0;
        for (var i = 0; i < limit; i++)
        {
            var index = fromStart ? i : (horizontal ? bitmap.Height - 1 - i : bitmap.Width - 1 - i);
            if (!Dark(bitmap, horizontal, index)) break;
            count++;
        }
        return count;
    }

    private static bool Dark(Bitmap bitmap, bool row, int index)
    {
        var length = row ? bitmap.Width : bitmap.Height;
        long luma = 0;
        var samples = 0;
        for (var i = 0; i < length; i += 8)
        {
            var pixel = row ? bitmap.GetPixel(i, index) : bitmap.GetPixel(index, i);
            luma += pixel.R + pixel.G + pixel.B;
            samples++;
        }
        return samples > 0 && luma / samples < 24;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SPlayer.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到仓库根目录");
    }

    private static string FindExe(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "src", "SPlayer.App", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", "SPlayer.exe"),
            Path.Combine(root, "src", "SPlayer.App", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "win-x64", "SPlayer.exe"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("先生成 SPlayer.exe");
    }

    private sealed class LaunchScope : IDisposable
    {
        private readonly Process _process;
        public LaunchScope(Process process) => _process = process;
        public Process Process => _process;
        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }
            _process.Dispose();
        }
    }

    private static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    }
}

public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SPLAYER_UI") != "1")
            Skip = "需要显式启用 SPLAYER_UI=1 和可交互桌面；不是播放核心测试。";
    }
}
