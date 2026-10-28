using SPlayer;
namespace SPlayer.Tests;
public class ReliabilityTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "SPlayer", "regression-" + Guid.NewGuid().ToString("N"), "data.json");
    [Fact] public void IndependentHistoryWritersMergeAndClearDoesNotResurrect()
    {
        var path = NewPath(); var a = new HistoryStore(path); var b = new HistoryStore(path);
        a.Upsert(new HistoryEntry { Path = "a.mp4" });
        b.Upsert(new HistoryEntry { Path = "b.mp4" });
        Assert.Equal(2, new HistoryStore(path).Entries.Count);
        a.Clear(); b.Upsert(new HistoryEntry { Path = "c.mp4" });
        Assert.Single(new HistoryStore(path).Entries);
        Assert.Equal("c.mp4", new HistoryStore(path).Entries[0].Path);
    }
    [Fact] public void UrlPathCaseIsPreservedButLocalPathsIgnoreCase()
    {
        var path = NewPath(); var h = new HistoryStore(path);
        h.Upsert(new HistoryEntry { Path = "https://example.invalid/A.mp4" });
        h.Upsert(new HistoryEntry { Path = "https://example.invalid/a.mp4" });
        Assert.Equal(2,h.Entries.Count);
        Assert.True(MediaPaths.Same(@"C:\Video\A.mp4", @"c:\video\a.mp4"));
    }
    [Fact] public void SettingsSaveMergesOnlyPropertiesChangedByEachInstance()
    {
        var path = NewPath(); var a=AppSettings.Load(path); var b=AppSettings.Load(path);
        a.Volume=42; a.Save(path); b.SeekSeconds=12; b.Save(path);
        var final=AppSettings.Load(path); Assert.Equal(42,final.Volume); Assert.Equal(12,final.SeekSeconds);
        a.Save(path); Assert.Equal(12,AppSettings.Load(path).SeekSeconds);
    }
    [Fact] public void ExistingSettingsDefaultToVideoFitAndFixedModePersists()
    {
        var path = NewPath();
        AtomicFile.Write(path, "{\"volume\":42}");
        var settings = AppSettings.Load(path);
        Assert.True(settings.FitWindowToVideo);
        settings.FitWindowToVideo = false;
        settings.Save(path);
        var reloaded = AppSettings.Load(path);
        Assert.False(reloaded.FitWindowToVideo);
        Assert.Equal(42, reloaded.Volume);
        reloaded.FitWindowToVideo = true;
        reloaded.Save(path);
        Assert.True(AppSettings.Load(path).FitWindowToVideo);
    }
    [Fact] public void ShuffleManualSelectionDoesNotImmediatelyRepeatCurrent()
    {
        var p=new Playlist();p.Replace(["a","b"]);p.SetShuffle(true);p.Select(1);
        Assert.Equal(0,p.PlanNext().Index);
    }
    [Fact] public void SensitiveUrlCannotBePersistedAndLogsRedactIt()
    {
        const string url="https://test:synthetic@example.invalid/video.mp4?token=synthetic";
        Assert.False(MediaPaths.CanPersist(url)); Assert.True(MediaPaths.CanPersist("https://example.invalid/a.mp4"));
        var redacted=Log.Redact("failed "+url); Assert.DoesNotContain("synthetic",redacted);Assert.DoesNotContain("test:",redacted);
    }
    [Fact] public void ConcurrentHistoryWritersKeepAllEntries()
    {
        var path=NewPath();
        Parallel.For(0,12,i=> new HistoryStore(path).Upsert(new HistoryEntry { Path=$"test-{i}.mp4" }));
        Assert.Equal(12,new HistoryStore(path).Entries.Count);
    }
    [Fact] public void OpenSnapshotAllowsAtomicReplacement()
    {
        var path = NewPath();
        AtomicFile.Write(path, "old");
        using var old = new StreamReader(AtomicFile.OpenRead(path));
        AtomicFile.Write(path, "new");
        Assert.Equal("old", old.ReadToEnd());
        Assert.Equal("new", AtomicFile.Read(path));
    }
    [Fact] public void SubtitleVisibilitySnapshotTracksBothDirections()
    {
        using var p=MpvPlayer.Create(false);p.Start();
        p.SetFlag("sub-visibility",false);
        Assert.True(p.WaitUntil(()=>!p.ReadFacts().SubtitlesVisible,TimeSpan.FromSeconds(3)));
        p.SetFlag("sub-visibility",true);
        Assert.True(p.WaitUntil(()=>p.ReadFacts().SubtitlesVisible,TimeSpan.FromSeconds(3)));
    }
}
