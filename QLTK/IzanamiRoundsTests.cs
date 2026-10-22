using System;using System.Linq;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class IzanamiRoundsTests{
 static void C(bool ok,string message){if(!ok)throw new Exception(message);}
 static int Main(){try{Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Run(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/izanami157-logs");var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);var team=new NativeJob[3];DateTime t=DateTime.UtcNow;
  for(int i=0;i<team.Length;i++){var j=new NativeJob("synthetic.exe","fixture","",null,true);jobs[j.Id]=j;team[i]=j;j.IzanamiEnabled=true;j.IzanamiRoundsTarget=4;j.CharacterName="member"+i;j.State="opened";j.EntryAutoKind=2;j.EntryAutoOrigin=59;j.MapId=59;j.ZoneId=1;j.ZoneReadAt=t;j.TeamReadAt=t;}
  NativeBackend.ConfigureIzanamiTeam(team.Select(j=>j.Id).ToArray());string[] names=team.Select(j=>j.CharacterName).ToArray();
  for(int round=1;round<=4;round++){
   foreach(var j in team){j.ZonePrepared=true;j.TeamMembers=names;j.TeamReadAt=t;j.ZoneReadAt=t;j.TeamConfirmed=true;}
   C(NativeBackend.IzanamiEntryTeamReady(team[0]),"valid group rejected before round "+round);
   team[1].TeamMembers=new[]{"outsider"};C(!NativeBackend.IzanamiEntryTeamReady(team[0]),"stale confirmed flag bypasses missing roster before round "+round);team[1].TeamMembers=names;
   team[1].ZoneId=2;C(!NativeBackend.IzanamiEntryTeamReady(team[0]),"different zone bypasses round gate");team[1].ZoneId=1;
   foreach(var j in team){j.IzanamiRoundEntered=true;j.IzanamiRoundComplete=false;j.MapId=59;NativeBackend.ObserveIzanamiReturn(j,59,t);NativeBackend.ObserveIzanamiReturn(j,59,t.AddSeconds(3));NativeBackend.ObserveIzanamiReturn(j,59,t.AddSeconds(4));C(j.IzanamiRoundsCompleted==round,"return counted more than once");j.TeamReadAt=t.AddSeconds(4);j.ZoneAttempts=3;j.PlannedZone=14;j.ZonePlanMap=59;}
   C(team[0].IzanamiRunFinished==(round==4),"intermediate round was treated as all rounds complete");
   if(round<4){team[2].CloseRequested=true;NativeBackend.PulseIzanamiRounds(t.AddSeconds(4));C(team[0].IzanamiRoundComplete,"offline member did not hold next-round group check");team[2].CloseRequested=false;NativeBackend.PulseIzanamiRounds(t.AddSeconds(4));C(team.All(j=>!j.IzanamiRoundComplete&&!j.TeamConfirmed&&j.TeamReadAt==DateTime.MinValue),"next round did not invalidate old group confirmation");C(team.All(j=>j.ZoneAttempts==0&&j.PlannedZone==-1&&j.ZonePlanMap==-1&&!j.ZonePrepared),"new round retained previous zone attempts/target");C(team[0].IzanamiRequested&&!team[1].IzanamiRequested,"next entry leader incorrect");C(!NativeBackend.IzanamiEntryTeamReady(team[0]),"reentry skipped group reread");}
   t=t.AddSeconds(10);
  }
  NativeBackend.PulseIzanamiRounds(t);C(team.All(j=>j.IzanamiRunFinished&&j.IzanamiRoundsCompleted==4),"last round restarted instead of finishing");
  Console.WriteLine("PASS: four confirmed rounds, one count per return, group/zone checks before every entry, offline member hold, reset/reconfirm roster, leader-only reentry and final-only completion.");
 }
}
