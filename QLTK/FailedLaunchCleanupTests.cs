using System;
using TGLL;
class FailedLaunchCleanupTests {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static NativeJob Fixture(){return new NativeJob("synthetic.exe","fixture","unused",null,true){State="failed",Error="native_cleanup_failed",ProcessId=999999};}
    static void Main(){
        DateTime now=DateTime.UtcNow;var j=Fixture();int scheduled=0,closed=0;bool alive=true;
        Action<NativeJob,int> schedule=delegate(NativeJob item,int pid){scheduled++;};
        Check(NativeBackend.TryBeginFailedLaunchCleanup(j,now,schedule),"cleanup not scheduled");
        Check(!NativeBackend.TryBeginFailedLaunchCleanup(j,now.AddSeconds(20),schedule)&&scheduled==1,"duplicate cleanup worker");
        NativeBackend.CleanFailedLaunch(j,j.ProcessId,pid=>alive,pid=>{closed++;return new CloseGamesResult{Failed=1};});
        Check(closed==1&&j.State=="failed"&&!j.CloseRequested&&!j.CancelRequested&&!j.FailedCleanupBusy,"failed cleanup cancelled queue member");
        Check(!NativeBackend.TryBeginFailedLaunchCleanup(j,now.AddSeconds(9),schedule),"cleanup cooldown bypassed");
        Check(NativeBackend.TryBeginFailedLaunchCleanup(j,now.AddSeconds(10),schedule),"cleanup did not retry");
        NativeBackend.CleanFailedLaunch(j,j.ProcessId,pid=>alive,pid=>{closed++;alive=false;return new CloseGamesResult{Closed=1};});
        Check(!alive&&j.State=="failed"&&!j.CloseRequested&&!j.CancelRequested,"cleanup failed to preserve scheduler ownership");
        NativeBackend.CleanFailedLaunch(j,j.ProcessId,pid=>false,pid=>{throw new Exception("already exited process closed again");});
        foreach(string state in new[]{"opened","queued","starting","closed","cancelled"}){var x=Fixture();x.State=state;Check(!NativeBackend.TryBeginFailedLaunchCleanup(x,now,schedule),"cleaned nonfailed job");}
        foreach(string error in new[]{"native_login_not_ready","game_disconnected",""}){var x=Fixture();x.Error=error;Check(!NativeBackend.TryBeginFailedLaunchCleanup(x,now,schedule),"cleaned unrelated failure");}
        var stopped=Fixture();stopped.CloseRequested=true;Check(!NativeBackend.TryBeginFailedLaunchCleanup(stopped,now,schedule),"recovered manual close");
        stopped=Fixture();stopped.CancelRequested=true;Check(!NativeBackend.TryBeginFailedLaunchCleanup(stopped,now,schedule),"recovered manual cancellation");
        stopped=Fixture();stopped.ReconnectRequested=true;Check(!NativeBackend.TryBeginFailedLaunchCleanup(stopped,now,schedule),"raced native reconnect");
        stopped=Fixture();stopped.ProcessId=0;Check(!NativeBackend.TryBeginFailedLaunchCleanup(stopped,now,schedule),"cleaned pending launch");
        var changed=Fixture();NativeBackend.CleanFailedLaunch(changed,changed.ProcessId+1,pid=>true,pid=>{throw new Exception("closed replacement PID");});
        var cancelled=Fixture();cancelled.CancelRequested=true;NativeBackend.CleanFailedLaunch(cancelled,cancelled.ProcessId,pid=>true,pid=>{throw new Exception("closed cancelled job");});
        var fault=Fixture();try{NativeBackend.TryBeginFailedLaunchCleanup(fault,now,(item,pid)=>{throw new Exception("worker unavailable");});}catch{}
        Check(!fault.FailedCleanupBusy,"failed worker start left cleanup busy");
        Console.WriteLine("PASS: failed-startup cleanup deduplicates workers, retries after cooldown, retains queue ownership, waits for exit, excludes healthy/manual/reconnecting/replacement sessions. No game action.");
    }
}
