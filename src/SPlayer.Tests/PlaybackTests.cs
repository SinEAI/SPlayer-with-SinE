using SPlayer;

namespace SPlayer.Tests;

public class PlaybackTests
{
    private static string Samples
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "testdata", "samples");
                if (File.Exists(Path.Combine(candidate, "h264.mp4"))) return candidate;
                dir = dir.Parent;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "testdata", "samples"));
        }
    }

    private static string Sample(string name)
    {
        var path = Path.Combine(Samples, name);
        Assert.True(File.Exists(path), "缺少测试样本 " + path);
        return path;
    }

    public static IEnumerable<object[]> MediaFiles()
    {
        if (!Directory.Exists(Samples)) yield break;
        foreach (var file in Directory.EnumerateFiles(Samples, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "manifest.json" or "broken.mp4") continue;
            if (MediaPaths.IsMedia(file)) yield return [file];
        }
    }

    [Fact]
    public void SampleSetWasGenerated()
    {
        Assert.True(Directory.Exists(Samples), Samples);
        Assert.True(File.Exists(Path.Combine(Samples, "h264.mp4")));
        Assert.True(File.Exists(Path.Combine(Samples, "hevc.mp4")));
        Assert.True(File.Exists(Path.Combine(Samples, "tone.flac")));
    }

    [Theory]
    [MemberData(nameof(MediaFiles))]
    public void FileOpensAndAdvances(string path)
    {
        using var player = MpvPlayer.Create(render: false);
        player.SetOption("untimed", "yes");
        player.Start();
        player.Load(path);
        player.SetFlag("pause", false);
        var ready = player.WaitUntil(() =>
        {
            var facts = player.ReadFacts();
            var clock = player.ReadClock();
            return !string.IsNullOrEmpty(facts.ErrorTitle) || clock.Position > 0.05 || clock.Eof;
        }, TimeSpan.FromSeconds(20));
        var facts = player.ReadFacts();
        Assert.True(ready, Path.GetFileName(path));
        Assert.True(string.IsNullOrEmpty(facts.ErrorTitle), facts.ErrorTitle + " " + facts.ErrorDetail);
        Assert.True(facts.FileLoaded);
    }

    [Fact]
    public void BrokenFileDoesNotCrash()
    {
        using var player = MpvPlayer.Create(render: false);
        player.Start();
        player.Load(Sample("broken.mp4"));
        player.SetFlag("pause", false);
        var done = player.WaitUntil(() => !string.IsNullOrEmpty(player.ReadFacts().ErrorTitle) || player.ReadClock().Eof, TimeSpan.FromSeconds(10));
        Assert.True(done);
        Assert.False(string.IsNullOrEmpty(player.ReadFacts().ErrorTitle));
    }

    [Fact]
    public void MissingFileIsExplained()
    {
        using var player = MpvPlayer.Create(render: false);
        player.Start();
        var path = Path.Combine(Samples, "不存在的文件.mp4");
        player.Load(path);
        player.SetFlag("pause", false);
        Assert.True(player.WaitUntil(() => !string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), TimeSpan.FromSeconds(10)));
        Assert.Contains("找不到", player.ReadFacts().ErrorTitle);
    }

    [Fact]
    public void SeekLandsNearTheRequestedTime()
    {
        using var player = MpvPlayer.Create(render: false);
        player.Start();
        player.Load(Sample("h264.mp4"));
        player.SetFlag("pause", true);
        Assert.True(player.WaitUntil(() => player.ReadFacts().FileLoaded, TimeSpan.FromSeconds(10)));
        player.Command("seek", "3", "absolute+exact");
        Assert.True(player.WaitUntil(() => Math.Abs(player.ReadClock().Position - 3) < 0.6, TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public void ExternalUtf8SubtitleIsRead()
    {
        using var player = Open(Sample("h264.mp4"));
        AssertSubtitle(player, "中文测试字幕");
    }

    [Fact]
    public void Gb18030SubtitleIsRead()
    {
        using var player = MpvPlayer.Create(render: false);
        player.SetOption("sub-codepage", "gb18030");
        player.Start();
        player.Load(Sample("gb18030.mp4"));
        player.SetFlag("pause", false);
        Assert.True(player.WaitUntil(() => player.ReadFacts().FileLoaded || !string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), TimeSpan.FromSeconds(12)));
        Assert.True(string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), player.ReadFacts().ErrorDetail);
        AssertSubtitle(player, "简体中文编码");
    }

    [Fact]
    public void EmbeddedSubtitleTrackIsListed()
    {
        using var player = Open(Sample("embedded-srt.mkv"));
        Assert.Contains(player.ReadFacts().Tracks, t => t.Kind == "sub");
        AssertSubtitle(player, "中文测试字幕");
    }

    [Fact]
    public void SecondAudioTrackCanBeSelected()
    {
        using var player = Open(Sample("dual-audio.mkv"));
        var audio = player.ReadFacts().Tracks.Where(t => t.Kind == "audio").ToArray();
        Assert.True(audio.Length >= 2);
        player.SetString("aid", audio[1].Id.ToString());
        Assert.True(player.WaitUntil(() => player.ReadFacts().AudioId == audio[1].Id, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ChinesePathPlays()
    {
        var path = Path.Combine(Samples, "中文目录", "影片 测试.mp4");
        Assert.True(File.Exists(path), "缺少测试样本 " + path);
        using var player = Open(path);
        Assert.True(player.ReadFacts().FileLoaded);
        Assert.Contains("影片", player.ReadFacts().Path);
    }

    [Fact]
    public void RapidLoadsSettleOnTheLastFile()
    {
        using var player = MpvPlayer.Create(render: false);
        player.Start();
        var files = new[] { "tone.mp3", "tone.flac", "h264.mp4", "hevc.mp4", "tone.wav" }.Select(Sample).ToArray();
        foreach (var file in files) player.Load(file);
        player.SetFlag("pause", false);
        Assert.True(player.WaitUntil(() => player.ReadFacts().FileLoaded && player.ReadFacts().Path == files[^1] && string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void RenderedFrameIsNotBlank()
    {
        var path = Sample("h264.mp4");
        using var video = D3D11Video.Create();
        using var player = MpvPlayer.Create(render: true);
        if (!video.Hardware) player.SetOption("hwdec", "no");
        player.Start();
        using var render = player.AttachRender(video.Device.NativePointer);
        using var target = video.CreateTarget(320, 180);
        player.Load(path);
        player.SetFlag("pause", false);
        double best = 0;
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until && best < 20)
        {
            render.Update();
            render.Render(target.NativePointer, false);
            var pixels = video.ReadBgra(target);
            best = Math.Max(best, D3D11Video.Variance(pixels));
            if (best >= 20) break;
            Thread.Sleep(40);
        }
        var hwdec = player.GetString("hwdec-current") ?? "";
        File.WriteAllText(Path.Combine(Samples, "render-result.txt"), "variance=" + best.ToString("0.##") + Environment.NewLine + "hwdec=" + hwdec + Environment.NewLine + "hardwareDevice=" + video.Hardware);
        Assert.True(best >= 20, "画面方差 " + best + " hwdec=" + hwdec);
    }

    private static void AssertSubtitle(MpvPlayer player, string expected)
    {
        string seen = "";
        var ok = player.WaitUntil(() =>
        {
            seen = player.GetString("sub-text") ?? "";
            return seen.Contains(expected);
        }, TimeSpan.FromSeconds(8));
        Assert.True(ok, "sub-text=[" + seen + "] sid=" + player.GetDouble("sid", -1) + " pos=" + player.ReadClock().Position.ToString("0.00") + " tracks=" + string.Join(",", player.ReadFacts().Tracks.Select(t => t.Kind + ":" + t.Id + ":" + t.Title)));
    }

    private static MpvPlayer Open(string path)
    {
        var player = MpvPlayer.Create(render: false);
        player.Start();
        player.Load(path);
        player.SetFlag("pause", false);
        Assert.True(player.WaitUntil(() => player.ReadFacts().FileLoaded || !string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), TimeSpan.FromSeconds(12)));
        Assert.True(string.IsNullOrEmpty(player.ReadFacts().ErrorTitle), player.ReadFacts().ErrorDetail);
        return player;
    }
}
