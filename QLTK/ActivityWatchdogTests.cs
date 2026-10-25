using System;using TGLL;
class ActivityWatchdogTests {
 static DateTime t=new DateTime(2026,10,24,0,0,0,DateTimeKind.Utc);static int closed;
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static NativeJob J(string kind,int map){var j=new NativeJob("fixture.exe","fixture","unused",null,true);j.ProcessId=123456;j.State="opened";j.ReceiveKeyEnabled=false;j.MapId=map;j.GameSettingsReady=true;j.GameAutoEnabled=true;j.QuestSnapshotAt=t;if(kind=="quest")j.QuestEnabled=true;if(kind=="dungeon")j.DungeonHighEnabled=true;if(kind=="izanami")j.IzanamiEnabled=true;if(kind=="soncap"){j.IzanamiEnabled=true;j.SonCapEnabled=true;}return j;}
 static void Tick(NativeJob j,int sec){var now=t.AddSeconds(sec);j.ZoneReadAt=now;if(j.QuestEnabled)j.QuestSnapshotAt=now;NativeBackend.ObserveActivityWatchdog(j,now,p=>closed++);}
 static void Main(){
  foreach(string kind in new[]{"quest","dungeon","izanami","soncap"}){
   var j=J(kind,59);int before=closed;Tick(j,0);Tick(j,299);C(!j.ActivityRetryPending,"early retry "+kind);Tick(j,300);C(j.ActivityRetryPending,"retry at 5 "+kind);var start=j.ActivityVillageAt;NativeBackend.PrepareActivityRetry(j,t.AddSeconds(300));C(j.ActivityVillageAt==start,"retry extended village deadline");Tick(j,599);C(closed==before,"early close "+kind);Tick(j,600);C(closed==before+1&&j.ActivityTimeoutClosed&&j.CloseRequested,"village close "+kind);
  }
  foreach(string kind in new[]{"quest","dungeon","izanami","soncap"}){
   var j=J(kind,70);j.Error="fixture_error";Tick(j,0);Tick(j,300);C(j.ActivityRetryPending,"error retry "+kind);var start=j.ActivityFaultAt;NativeBackend.PrepareActivityRetry(j,t.AddSeconds(300));Tick(j,301);C(j.ActivityFaultAt==start,"clearing error is not recovery "+kind);Tick(j,600);C(!j.ActivityTimeoutClosed,"must retain faulted game outside village "+kind);j.MapId=59;Tick(j,601);C(!j.ActivityTimeoutClosed,"map progress should reset old fault "+kind);Tick(j,1201);C(j.ActivityTimeoutClosed,"close only after confirmed village "+kind);
  }
  var q=J("quest",70);q.QuestEnabled=false;q.QuestFaultStopped=true;Tick(q,0);Tick(q,300);C(NativeBackend.PrepareActivityRetry(q,t.AddSeconds(300))&&q.QuestEnabled&&!q.QuestFaultStopped,"restart stopped quest");q.QuestObservedCount++;Tick(q,301);C(q.ActivityFaultAt==DateTime.MinValue,"server quest progress recovers");Tick(q,1000);C(!q.ActivityTimeoutClosed,"healthy combat closed");
  var r=J("soncap",170);r.SonCapStage=0;r.SonCapAlive=50;r.SonCapFailure="fixture_portal";Tick(r,0);Tick(r,300);NativeBackend.PrepareActivityRetry(r,t.AddSeconds(300));r.SonCapAlive=49;Tick(r,301);C(r.ActivityFaultAt==DateTime.MinValue,"server monster progress recovers");
  var v=J("quest",59);Tick(v,0);Tick(v,300);v.MapId=70;v.ActivityRetryPending=false;Tick(v,400);Tick(v,1000);C(!v.ActivityTimeoutClosed&&v.ActivityVillageAt==DateTime.MinValue,"leaving village failed to reset");
  var sw=J("quest",59);Tick(sw,0);sw.QuestEnabled=false;sw.DungeonHighEnabled=true;Tick(sw,301);Tick(sw,600);C(sw.ActivityTimeoutClosed,"activity switch extended village timeout");
  var up=J("izanami",59);up.UpRequested=true;Tick(up,0);Tick(up,600);C(up.ActivityTimeoutClosed&&up.UpRequested,"fault close must preserve Up intention");
  var healthy=J("dungeon",18);healthy.DungeonCycleWaiting=true;healthy.DungeonLeftOrigin=true;Tick(healthy,0);Tick(healthy,3600);C(!healthy.ActivityRetryPending&&!healthy.ActivityTimeoutClosed,"healthy long dungeon closed");
  var idle=J("",59);Tick(idle,0);Tick(idle,900);C(!idle.ActivityTimeoutClosed,"idle village account closed");
  var done=J("quest",59);done.QuestEnabled=false;done.QuestFinished=true;Tick(done,0);Tick(done,900);C(!done.ActivityTimeoutClosed,"completed account closed without its close preference");
  var stop=J("quest",59);Tick(stop,0);stop.CancelRequested=true;Tick(stop,900);C(!stop.ActivityRetryPending&&!stop.ActivityTimeoutClosed,"manual stop retried");
  var off=J("quest",59);Tick(off,0);off.QuestEnabled=false;Tick(off,900);C(!off.ActivityTimeoutClosed,"disabled activity closed");
  var beast=J("quest",59);beast.BeastQuestEnabled=true;Tick(beast,0);Tick(beast,900);C(!beast.ActivityTimeoutClosed,"beast changed outside requested scope");
  var iz=J("izanami",59);iz.IzanamiRoundsCompleted=2;iz.IzanamiRoundsTarget=4;iz.ActivityRetryPending=true;NativeBackend.PrepareActivityRetry(iz,t);C(iz.IzanamiRoundsCompleted==2&&iz.IzanamiRoundsTarget==4,"retry reset rounds");
  var dc=J("dungeon",18);dc.DungeonEntries=3;dc.DungeonCycleWaiting=true;dc.DungeonLeftOrigin=true;dc.DungeonReturnConfirmed=true;dc.ActivityRetryPending=true;NativeBackend.PrepareActivityRetry(dc,t);C(dc.DungeonEntries==3&&dc.DungeonCycleWaiting&&dc.DungeonReturnConfirmed&&!dc.ReceiveKeyRequested,"retry lost current dungeon");
  foreach(int map in new[]{18,89,170,70,-1}){var j=J("dungeon",map);j.Error="fixture_error";Tick(j,0);Tick(j,900);C(!j.ActivityTimeoutClosed,"non-village timeout killed map "+map);}
  foreach(int map in new[]{59,60,68,69,75,85,102}){var j=J("dungeon",map);Tick(j,0);Tick(j,600);C(j.ActivityTimeoutClosed,"village close missing "+map);}
  var stale=J("dungeon",59);stale.Error="fixture_error";Tick(stale,0);stale.ZoneReadAt=t;NativeBackend.ObserveActivityWatchdog(stale,t.AddSeconds(900),p=>closed++);C(!stale.ActivityTimeoutClosed,"stale village snapshot closed game");
  var drift=J("dungeon",59);C(!NativeBackend.ConfirmActivityCloseMap(drift,()=>18),"map changed to dungeon before close");C(!NativeBackend.ConfirmActivityCloseMap(drift,()=>{throw new Exception();}),"read failure authorized close");C(NativeBackend.ConfirmActivityCloseMap(drift,()=>59),"fresh village blocked");drift.CancelRequested=true;C(!NativeBackend.ConfirmActivityCloseMap(drift,()=>59),"manual stop raced with deferred close");
  var recovered=J("dungeon",18);recovered.DungeonCycleWaiting=true;recovered.DungeonLeftOrigin=true;recovered.GameSettingsReady=false;Tick(recovered,0);recovered.GameSettingsReady=true;recovered.SettingsReadAt=t.AddSeconds(1);Tick(recovered,1);recovered.SettingsReadAt=t.AddSeconds(31);Tick(recovered,31);C(recovered.ActivityFaultAt==DateTime.MinValue,"transient game_not_ready latched despite stable in-dungeon recovery");Tick(recovered,901);C(!recovered.ActivityTimeoutClosed,"recovered dungeon closed");
  var snap=J("soncap",94);snap.ZoneReadAt=t;snap.SonCapReadError="team_snapshot_changed";C(NativeBackend.ActivityFaultReason(snap,"soncap",t)=="team_snapshot_changed","missing initial snapshot fault");
  NativeBackend.ObserveSonCap(snap,94,10,false,t);C(snap.SonCapReadError.Length>0,"unloaded snapshot cleared read error");
  NativeBackend.ObserveSonCap(snap,94,10,true,t);C(snap.SonCapReadError.Length==0&&NativeBackend.ActivityFaultReason(snap,"soncap",t)=="","valid snapshot retained stale read fault");
  Console.WriteLine("PASS: four activity watchdogs; exact 5/10-minute boundaries; retry preserves deadline/rounds/current dungeon; actual recovery, village departure/switch, manual stop, idle/completed/healthy combat and Up selection. Village-only close, fresh native map check and transient settings recovery. No real game closed.");
 }
}
