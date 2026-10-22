using System;using System.Diagnostics;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class LeaderZoneTests {
 static ConcurrentDictionary<string,NativeJob> Jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static void C(bool b,string m){if(!b)throw new Exception(m);}static void I(Remote r,long p,int n){r.Write(p,BitConverter.GetBytes(n));}static int R(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
 static NativeJob J(string name,int zone){var j=new NativeJob("synthetic.exe",name,"unused",null,true);j.IzanamiEnabled=true;j.TeamManaged=true;j.CharacterName=name;j.MapId=59;j.ZoneId=zone;j.TrainReady=true;j.GameSettingsReady=true;j.TeamReadAt=DateTime.UtcNow;Jobs[j.Id]=j;return j;}
 static void Pump(Remote r,long data,NativeJob member,NativeJob leader,DateTime time){member.TeamReadAt=time;leader.TeamReadAt=time;NativeBackend.PumpLeaderZoneSync(r,data,member,time);}
 static void Main(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/leader-zone146-logs");using(var r=new Remote(Process.GetCurrentProcess())){
  long data=r.Allocate(4096);DateTime t=DateTime.UtcNow;var leader=J("FastPine",12);var member=J("FastRaven",11);var late=J("FastReef",0);string[] ids={leader.Id,member.Id,late.Id};NativeBackend.ConfigureIzanamiTeam(ids);
  late.TravelData=123;late.IzanamiTravelReady=false;member.PlannedZone=11;member.ZoneAttempts=3;I(r,data,4);
  Pump(r,data,member,leader,t);C(R(r,data)==2&&R(r,data+16)==12&&member.PlannedZone==12,"old zone11 target not replaced by leader12 / late peer blocked sync");
  member.ZoneActionAt=t;I(r,data,4);Pump(r,data,member,leader,t.AddSeconds(2));C(R(r,data)==4,"retry before8 seconds");
  member.ZoneId=12;Pump(r,data,member,leader,t.AddSeconds(3));C(!member.ZoneSyncActive&&member.LeaderZoneConfirmed&&R(r,data)==4,"confirmation resent zone change");
  leader.ZoneId=13;I(r,data,3);Pump(r,data,member,leader,t.AddSeconds(9));C(R(r,data)==3&&member.PlannedZone==13,"busy command overwritten / new target ignored");
  I(r,data,4);Pump(r,data,member,leader,t.AddSeconds(10));C(R(r,data)==2&&R(r,data+16)==13,"new leader zone not sent");
  member.ZoneAttempts=3;member.ZoneActionAt=t.AddSeconds(10);I(r,data,4);Pump(r,data,member,leader,t.AddSeconds(20));C(R(r,data)==4,"backoff missing");Pump(r,data,member,leader,t.AddSeconds(41));C(R(r,data)==2&&R(r,data+16)==13,"retry permanently stopped after3");
  var reopened=J("FastRaven",11);NativeBackend.RebindIzanamiJob(member.Id,reopened.Id);C(leader.TeamJobIds[1]==reopened.Id&&reopened.TeamJobIds[0]==leader.Id&&late.TeamJobIds[1]==reopened.Id,"reopened member lost team slot");
  I(r,data,4);Pump(r,data,reopened,leader,t.AddSeconds(42));C(R(r,data)==2&&R(r,data+16)==13,"reopened member not synced");
  var newLeader=J("FastPine",15);NativeBackend.RebindIzanamiJob(leader.Id,newLeader.Id);C(reopened.TeamJobIds[0]==newLeader.Id,"leader reconnect retained stale job");reopened.ZoneActionAt=t.AddSeconds(42);I(r,data,4);Pump(r,data,reopened,newLeader,t.AddSeconds(51));C(R(r,data)==2&&R(r,data+16)==15,"new leader job zone not used");
  reopened.MapId=60;I(r,data,4);Pump(r,data,reopened,newLeader,t.AddSeconds(60));C(R(r,data)==4,"cross-map zone request sent");
  reopened.MapId=59;reopened.EntryCombatMap=59;Pump(r,data,reopened,newLeader,t.AddSeconds(61));C(R(r,data)==4&&!reopened.ZoneSyncActive,"instance combat moved zones");
 }Console.WriteLine("PASS: leader12 replaces old11 target, late peer independence, spacing/backoff, busy command, confirmation, member/leader reconnect, map and combat gates; no server requests.");}
}
