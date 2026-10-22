using System;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class DungeonClose123 {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var j=new NativeJob("synthetic.exe","fixture","unused",null,true);jobs[j.Id]=j;j.ProcessId=1;j.State="dungeon_no_keys";j.Error="dungeon_no_keys";j.DungeonStopped=true;j.DungeonFinished=true;int closes=0;Action<NativeJob> close=p=>closes++;
 NativeBackend.DungeonCloseAfterEnabled=false;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"unchecked closed");
 NativeBackend.DungeonCloseAfterEnabled=true;j.UpRequested=true;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"Up closed");j.UpRequested=false;j.DailyManaged=true;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"daily closed");j.DailyManaged=false;j.DungeonQueueOnly=true;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"queue closed twice");j.DungeonQueueOnly=false;
 j.DungeonCycleWaiting=true;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"last round closed");j.DungeonCycleWaiting=false;j.DungeonFinished=false;NativeBackend.PulseDungeonAutoClose(close);C(closes==0,"unconfirmed finish closed");j.DungeonFinished=true;
 NativeBackend.PulseDungeonAutoClose(close);NativeBackend.PulseDungeonAutoClose(close);C(closes==1,"direct account did not close exactly once");Console.WriteLine("PASS: direct dungeon close once; unchecked, Up, daily, queued and unfinished last round preserved.");}
}
