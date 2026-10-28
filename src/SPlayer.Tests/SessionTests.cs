using System.Collections.Concurrent;
using SPlayer;
namespace SPlayer.Tests;

public class SessionTests
{
    [Fact]
    public void SwitchingCheckpointsOldMediaAndMuteAndScreenshotReportActualState()
    {
        var previous = SynchronizationContext.Current;
        var ui = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"SPlayer.slnx"))) root=root.Parent;
        Assert.NotNull(root);
        var samples=Path.Combine(root!.FullName,"testdata","samples");
        Session? session = null;
        try
        {
            session=new Session(); session.Settings.RecordHistory=true;
            using var video=D3D11Video.Create();
            using var render=session.Player.AttachRender(video.Device.NativePointer);
            using var target=video.CreateTarget(160,90);
            var running=true;
            var thread=new Thread(()=>
            {
                while(Volatile.Read(ref running)) { render.Update();render.Render(target.NativePointer,false);Thread.Sleep(10); }
            });
            thread.Start();
            try
            {
                session.PlayPaths([Path.Combine(samples,"h264.mp4"),Path.Combine(samples,"hevc.mp4")],fromStart:true);
                Wait(()=>session.Clock.Position>.6 && session.Facts.FileLoaded,ui);
                session.Player.SetFlag("mute",true);
                Wait(()=>session.Clock.Muted,ui);
                session.Settings.Muted=true;
                session.SetVolume(25);
                Wait(()=>!session.Clock.Muted,ui);
                Assert.False(session.Settings.Muted);
                session.Next();
                Wait(()=>session.Facts.FileLoaded && session.Facts.Path.EndsWith("hevc.mp4"),ui);
                Wait(()=>session.History.Find(Path.Combine(samples,"h264.mp4"))?.Position>.5,ui);
                session.Settings.ScreenshotDirectory=Path.Combine(AppPaths.DataDirectory,"shots");
                var shot=session.Screenshot(); Assert.True(File.Exists(shot));
                var next=session.Screenshot(); Assert.NotEqual(shot,next);
                session.Settings.ScreenshotDirectory=shot;
                Assert.ThrowsAny<Exception>(()=>session.Screenshot());
                session.PrepareClose();
            }
            finally { Volatile.Write(ref running,false); thread.Join(); }
        }
        finally { session?.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void Wait(Func<bool> condition, PumpContext ui)
    {
        var until=Environment.TickCount64+10000;
        while(Environment.TickCount64<until) { ui.Pump(); if(condition())return; Thread.Sleep(10); }
        Assert.True(condition(),"Session condition timed out");
    }
    private sealed class PumpContext:SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback,object? State)> _queue=new();
        public override void Post(SendOrPostCallback callback,object? state)=>_queue.Enqueue((callback,state));
        internal void Pump() { while(_queue.TryDequeue(out var item))item.Callback(item.State); }
    }
}
