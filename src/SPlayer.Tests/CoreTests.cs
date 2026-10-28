using SPlayer;

namespace SPlayer.Tests;

public class CoreTests
{
    [Fact]
    public void OpenWithVideoQueuesSiblingVideosAndSkipsAudio()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SPlayer", "queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "b.mkv"), []);
            File.WriteAllBytes(Path.Combine(dir, "a.mp4"), []);
            File.WriteAllBytes(Path.Combine(dir, "song.mp3"), []);
            var opened = Path.Combine(dir, "b.mkv");
            Assert.True(MediaPaths.TrySiblingVideos(opened, out var files, out var index));
            Assert.Equal(2, files.Count);
            Assert.Equal(1, index);
            Assert.EndsWith("a.mp4", files[0], StringComparison.OrdinalIgnoreCase);
            Assert.False(MediaPaths.TrySiblingVideos(Path.Combine(dir, "song.mp3"), out _, out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResumeSkipsCompletedAndTinyPositions()
    {
        Assert.False(ResumePolicy.ShouldResume(new HistoryEntry { Position = 1, Duration = 100 }));
        Assert.False(ResumePolicy.ShouldResume(new HistoryEntry { Position = 40, Duration = 100, Completed = true }));
        Assert.True(ResumePolicy.ShouldResume(new HistoryEntry { Position = 40, Duration = 100 }));
        Assert.True(ResumePolicy.IsCompleted(95, 100));
        Assert.False(ResumePolicy.IsCompleted(10, 100));
        Assert.True(ResumePolicy.IsCompleted(4.6, 5));
    }

    [Fact]
    public void SeekCoalescerDropsBursts()
    {
        var seek = new SeekCoalescer(80);
        Assert.True(seek.Allow(false));
        Assert.False(seek.Allow(false));
        Assert.True(seek.Allow(true));
        Assert.True(seek.Allow(false));
    }

    [Fact]
    public void PlaylistRepeatShuffleAndReorder()
    {
        var list = new Playlist();
        list.Replace(["a", "b", "c"]);
        Assert.Equal("a", list.Current);
        Assert.Equal(AdvanceKind.Goto, list.PlanNext().Kind);
        Assert.Equal("b", list.Current);
        list.Repeat = RepeatMode.One;
        Assert.Equal(AdvanceKind.Replay, list.PlanOnEnd().Kind);
        list.Repeat = RepeatMode.None;
        list.Select(2);
        Assert.Equal(AdvanceKind.Stop, list.PlanOnEnd().Kind);
        list.Repeat = RepeatMode.All;
        Assert.Equal(0, list.PlanOnEnd().Index);
        list.SetShuffle(true);
        list.SetOrder(["c", "a", "b"]);
        Assert.Equal("a", list.Current);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void SingleItemShuffleRepeatsWhenLooping()
    {
        var list = new Playlist { Repeat = RepeatMode.All };
        list.Replace(["only"]);
        list.SetShuffle(true);
        Assert.Equal(AdvanceKind.Goto, list.PlanNext().Kind);
        Assert.Equal(0, list.Index);
    }

    [Fact]
    public void HistoryRoundTripAndCap()
    {
        var path = Path.Combine(Path.GetTempPath(), "splayer-history-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new HistoryStore(path);
        store.Upsert(new HistoryEntry { Path = "D:\\影片\\甲.mkv", Position = 12, Duration = 90, PlayedAt = DateTimeOffset.Now, Title = "甲" });
        var again = new HistoryStore(path);
        var found = again.Find("d:\\影片\\甲.mkv");
        Assert.NotNull(found);
        Assert.Equal(12, found!.Position);
        again.Clear();
        Assert.Empty(again.Entries);
    }

    [Fact]
    public void PlaylistFileKeepsUnicodeAndRelativeM3u()
    {
        var folder = Path.Combine(Path.GetTempPath(), "splayer-m3u-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var media = Path.Combine(folder, "中文.mp4");
        File.WriteAllText(media, "x");
        var list = Path.Combine(folder, "列表.m3u");
        PlaylistFile.Write(list, [media, "https://example.com/a.mp4"]);
        var read = PlaylistFile.Read(list);
        Assert.Equal(media, read[0]);
        Assert.Equal("https://example.com/a.mp4", read[1]);
    }

    [Fact]
    public void PathsClassifyHttpUncAndOneFolderLevel()
    {
        Assert.True(MediaPaths.IsHttp("https://example.com/a.mkv"));
        Assert.False(MediaPaths.IsHttp("file:///C:/a.mp4"));
        Assert.True(MediaPaths.IsUnc(@"\\nas\share\影片.mkv"));
        Assert.True(PlaybackError.IsRemote(@"\\nas\share\影片.mkv"));
        Assert.Equal("找不到文件", PlaybackError.Title(0, @"C:\no\such\splayer-missing.mp4"));
        var folder = Path.Combine(Path.GetTempPath(), "splayer-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllBytes(Path.Combine(folder, "a.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "nested", "b.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "note.txt"), [1]);
        var found = MediaPaths.MediaInFolder(folder);
        Assert.Single(found);
        Assert.EndsWith("a.mp4", found[0]);
    }

    [Fact]
    public void TrackLabelsUseChineseLanguageNames()
    {
        Assert.Contains("中文", TrackLabels.Audio("Commentary", "zh", "aac", 2));
        Assert.Contains("中文（繁体）", TrackLabels.Subtitle(null, "zh-Hant", "ass", true));
        Assert.Contains("外挂", TrackLabels.Subtitle(null, "zh-Hant", "ass", true));
    }

    [Fact]
    public void ClockTextRollsHours()
    {
        Assert.Equal("01:02", ClockText.Format(62));
        Assert.Equal("1:01:01", ClockText.Format(3661));
    }
}
