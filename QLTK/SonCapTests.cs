using System;using System.Linq;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class SonCapTests124 {
 static ConcurrentDictionary<string,NativeJob> Jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static DateTime T=new DateTime(2026,10,12,14,0,0,DateTimeKind.Utc);
 static void C(bool b,string s){if(!b)throw new Exception(s);}
 static NativeJob J(){var j=new NativeJob("synthetic.exe","fixture","unused",null,true);Jobs[j.Id]=j;j.State="opened";j.TrainReady=true;j.GameSettingsReady=true;j.ReceiveKeyEnabled=false;NativeBackend.SetSonCap(j.Id,true);return j;}
 static int O(NativeJob j,int map,int mobs,int sec,bool loaded=true){j.SonCapPollAt=T.AddSeconds(sec);return NativeBackend.ObserveSonCap(j,map,mobs,loaded,T.AddSeconds(sec));}
 static void Main(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/soncap-test-logs");
  var j=J();NativeBackend.SonCapTeamSize=1;NativeBackend.ConfigureSonCapTeams(new[]{j.Id});C(j.IzanamiEnabled&&!j.QuestEnabled&&!j.DungeonHighEnabled&&!j.ReceiveKeyEnabled,"exclusive flow");
  C(O(j,94,0,0,false)==0&&j.SonCapStage==-1,"loading map incorrectly completed");
  C(O(j,94,10,1)==0&&j.GameAutoWanted&&j.SonCapStage==0,"entry did not enable combat");
  C(O(j,94,0,2)==0,"initial spawn grace missing");C(O(j,94,0,6)==0,"empty snapshot instantly completed");
  O(j,94,1,7);C(j.SonCapEmptyAt==DateTime.MinValue&&j.GameAutoWanted,"revived/spawned mob did not reset completion");
  O(j,94,0,8);j.GameAutoEnabled=true;C(O(j,94,0,11)==0,"portal moved while game combat enabled");j.GameAutoEnabled=false;C(O(j,94,0,12)==1,"empty map did not request portal");
  j.SonCapMoving=true;j.SonCapRouteAt=T.AddSeconds(12);C(O(j,94,0,13)==0&&!j.GameAutoWanted,"moving portal repeats or enables combat");
  int[] maps={95,97,93,96};int second=20;
  foreach(int map in maps){C(O(j,map,3,second)==0&&j.GameAutoWanted&&!j.SonCapMoving,"next map combat");O(j,map,0,second+6);int action=O(j,map,0,second+10);C(action==(map==96?2:1),"stage action wrong");if(map!=96){j.SonCapMoving=true;j.SonCapRouteAt=T.AddSeconds(second+10);}second+=20;}
  C(j.SonCapFinished&&!j.GameAutoWanted&&O(j,96,0,second)==0,"final stage repeated");
  var resumed=J();C(O(resumed,97,2,0)==0&&resumed.SonCapStage==2&&resumed.IzanamiRoundEntered&&resumed.IzanamiTravelReady,"resume in third hang returns to village");
  NativeBackend.SetIzanami(resumed.Id,false);NativeBackend.SetDungeonHigh(resumed.Id,true);NativeBackend.SetAutoQuest(resumed.Id,true);C(resumed.IzanamiEnabled&&!resumed.DungeonHighEnabled&&!resumed.QuestEnabled,"other preferences overwrite SonCap");
  var a=J();var b=J();NativeBackend.SonCapTeamSize=2;NativeBackend.ConfigureSonCapTeams(new[]{a.Id,b.Id});O(a,94,1,0);O(b,94,1,0);O(a,94,0,6);O(b,94,1,6);C(O(a,94,0,10)==1,"cleared account must cross even while teammate still has mobs");O(b,94,0,10);O(a,94,0,13);C(O(b,94,0,13)==1,"cleared team held forever");
  O(a,95,4,14);C(O(b,94,0,14)==1,"first teammate crossing blocked remaining members");
  a.SonCapMoving=true;a.SonCapRouteAt=T;O(a,95,0,181);C(a.SonCapFailure.Length>0&&a.SonCapStopPending&&!a.GameAutoWanted,"portal timeout retries forever");
  NativeBackend.SetSonCap(b.Id,false);C(!b.SonCapEnabled&&!b.IzanamiEnabled&&!b.GameAutoWanted,"stop failed");
  C(NativeBackend.IsSonCapMonsterType("iu")&&!NativeBackend.IsSonCapMonsterType("gd"),"wrong monster type accepted");
  var living=J();O(living,94,60,0);NativeBackend.ObserveSonCapMapAuto(living,94,true,T);C(!NativeBackend.GameAutoDesired(living),"must wait three seconds");NativeBackend.ObserveSonCapMapAuto(living,94,true,T.AddSeconds(3));C(living.GameAutoWanted&&NativeBackend.GameAutoDesired(living),"60 living monsters did not enable idle combat");
  living.SonCapMoving=true;living.SonCapClearedStage=0;O(living,94,60,1);C(!living.SonCapMoving&&living.SonCapStopPending&&!living.GameAutoWanted,"living monsters failed to cancel portal");living.SonCapStopPending=false;O(living,94,60,2);C(living.GameAutoWanted&&NativeBackend.GameAutoDesired(living),"combat failed to resume after route cancellation");
  C(NativeBackend.SonCapEntityType("iu")==1&&NativeBackend.SonCapEntityType("Char")==0&&NativeBackend.SonCapEntityType("unknown")==-1,"mixed entity classification");
  var early=J();var late=J();NativeBackend.SonCapTeamSize=2;NativeBackend.ConfigureSonCapTeams(new[]{early.Id,""});O(early,94,0,0);O(early,94,0,6);C(O(early,94,0,10)==1,"pending login slot must not hold cleared account");
  NativeBackend.ConfigureSonCapTeams(new[]{early.Id,late.Id});C(early.TeamJobIds[1]==late.Id&&late.TeamJobIds[0]==early.Id,"late login slot not filled in existing team");
  O(late,94,0,10);O(late,94,0,16);O(early,94,0,20);C(O(late,94,0,20)==1&&O(early,94,0,20)==1,"updated team still stuck at cleared map");
  O(early,95,2,21);NativeBackend.ConfigureSonCapTeams(new[]{early.Id,""});C(early.TeamJobIds[1]==late.Id,"transient missing job dropped teammate");
  NativeBackend.ConfigureSonCapTeams(new[]{early.Id,living.Id});C(early.TeamJobIds[1]==late.Id,"active team repartitioned");
  var independent=J();var offline=J();NativeBackend.ConfigureSonCapTeams(new[]{independent.Id,offline.Id});O(independent,94,1,0);offline.CloseRequested=true;offline.CancelRequested=true;offline.State="closed";
  O(independent,94,0,6);C(O(independent,94,0,9)==1,"disconnected teammate blocks cleared account");C(offline.SonCapClearedStage==-1,"one account falsely marks disconnected teammate cleared");
  O(independent,95,1,10);C(O(independent,95,1,13)==0&&independent.GameAutoWanted,"independent new map does not resume combat");
  byte[] code=NativeCode.Npc(0x10000,0x20000,0x21000,0x22000,0x23000,0x24000,0x25000,0x26000,0x27000,0x28000,0x29000);C(code.Length<8192,"NPC code size overflow");
  Console.WriteLine("PASS: 5-stage route, loading/empty gates, alive-mob reset, combat off before portal, no repeated moves, final completion, resume, preference isolation, independent portal progression, timeout, stop; NPC code bytes="+code.Length);
 }
}
