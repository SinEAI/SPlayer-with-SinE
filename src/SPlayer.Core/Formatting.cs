namespace SPlayer;

public static class ClockText
{
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var whole = (int)Math.Floor(seconds);
        var hours = whole / 3600;
        var minutes = whole % 3600 / 60;
        var secs = whole % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes:00}:{secs:00}";
    }
}

public sealed class SeekCoalescer
{
    private readonly int _intervalMs;
    private long _last;

    public SeekCoalescer(int intervalMs = 80) => _intervalMs = intervalMs;

    /// <summary>True when a seek may be sent. The final seek of a drag always passes and resets the window.</summary>
    public bool Allow(bool final)
    {
        if (final)
        {
            _last = 0;
            return true;
        }
        var now = Environment.TickCount64;
        if (now - _last < _intervalMs) return false;
        _last = now;
        return true;
    }
}

public static class TrackLabels
{
    public static string Audio(string? title, string? language, string? codec, int channels)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title.Trim());
        var lang = Language(language);
        if (lang.Length > 0) parts.Add(lang);
        if (!string.IsNullOrWhiteSpace(codec)) parts.Add(codec.Trim().ToUpperInvariant());
        if (channels > 0) parts.Add(channels == 2 ? "立体声" : channels == 1 ? "单声道" : channels + " 声道");
        return parts.Count == 0 ? "音轨" : string.Join(" · ", parts);
    }

    public static string Subtitle(string? title, string? language, string? codec, bool external)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title.Trim());
        var lang = Language(language);
        if (lang.Length > 0) parts.Add(lang);
        if (!string.IsNullOrWhiteSpace(codec)) parts.Add(codec.Trim().ToUpperInvariant());
        if (external) parts.Add("外挂");
        return parts.Count == 0 ? "字幕" : string.Join(" · ", parts);
    }

    public static string Language(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var key = code.Trim().Replace('_', '-').ToLowerInvariant();
        return key switch
        {
            "zh" or "chi" or "zho" or "zh-cn" or "zh-hans" or "chs" or "sc" or "cmn" => "中文",
            "zh-tw" or "zh-hk" or "zh-hant" or "cht" or "tc" => "中文（繁体）",
            "en" or "eng" => "英语",
            "ja" or "jpn" or "jp" => "日语",
            "ko" or "kor" => "韩语",
            "fr" or "fre" or "fra" => "法语",
            "de" or "ger" or "deu" => "德语",
            "es" or "spa" => "西班牙语",
            "ru" or "rus" => "俄语",
            "pt" or "por" => "葡萄牙语",
            "it" or "ita" => "意大利语",
            "und" or "unknown" => "",
            _ => code.Trim(),
        };
    }
}

public static class PlaybackError
{
    public static string Title(int mpvCode, string path)
    {
        if (!MediaPaths.IsHttp(path) && !MediaPaths.IsUnc(path) && LooksLocal(path) && !File.Exists(path))
            return "找不到文件";
        if (MediaPaths.IsUnc(path) || IsRemoteDrive(path)) return "无法读取网络位置";
        return mpvCode switch
        {
            -14 => "音频设备不可用",
            -15 => "视频输出初始化失败",
            -16 => "没有可播放的内容",
            -17 => "无法识别这个文件",
            -13 => "无法播放这个文件",
            _ => "播放失败",
        };
    }

    public static string Summary(int mpvCode, string path)
    {
        if (!MediaPaths.IsHttp(path) && !MediaPaths.IsUnc(path) && LooksLocal(path) && !File.Exists(path))
            return "这个路径上没有文件。它可能已被移动或删除。";
        if (MediaPaths.IsUnc(path) || IsRemoteDrive(path))
            return "网络位置没有响应，或当前账号无权读取。确认共享仍然连接后再试。";
        return mpvCode switch
        {
            -14 => "系统没有可用的音频输出。画面会继续播放，但没有声音。",
            -15 => "无法创建视频输出。可以尝试在设置中关闭硬件解码后再打开。",
            -16 => "文件能被打开，但里面没有可解码的音频或视频流。",
            -17 => "文件已损坏，或者使用了当前播放内核不能解码的格式。",
            -13 => "文件无法加载。它可能已损坏、被占用，或编码不受支持。",
            _ => "播放内核无法继续。技术细节里有内核返回的原因。",
        };
    }

    public static bool IsRemote(string path)
    {
        if (MediaPaths.IsUnc(path) || MediaPaths.IsHttp(path)) return true;
        return IsRemoteDrive(path);
    }

    private static bool LooksLocal(string path)
    {
        try { return Path.IsPathRooted(path) && !MediaPaths.IsHttp(path); }
        catch (Exception) { return false; }
    }

    private static bool IsRemoteDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !root.EndsWith('\\')) return false;
            return GetDriveType(root) == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetDriveType(string lpRootPathName);
}
