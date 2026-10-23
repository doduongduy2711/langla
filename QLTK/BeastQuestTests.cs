using System;using System.Diagnostics;using System.Reflection;using System.Collections.Concurrent;using System.Linq;using TGLL;
class BeastQuestTests {
 static void Check(bool b,string s){if(!b)throw new Exception(s);}static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static void Snapshot(Remote r,long d,int map,bool present){I(r,d,0);I(r,d+8,1);I(r,d+12,3);I(r,d+16,present?1:0);I(r,d+20,0);I(r,d+32,0);I(r,d+36,1);I(r,d+64,2);I(r,d+80,map);I(r,d+84,74);I(r,d+88,68);I(r,d+108,196);I(r,d+112,1);I(r,d+120,1);}
 static void Main(){var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var peers=Enumerable.Range(0,6).Select(i=>new NativeJob("synthetic.exe","beast-"+i,"unused",null,true){CharacterName="char-"+i,State="opened",GameSettingsReady=true,TrainReady=true,ReceiveKeyEnabled=false}).ToArray();
 foreach(var j in peers)jobs[j.Id]=j;
 try{NativeBackend.ConfigureBeastTeam(peers.Select(j=>j.Id).ToArray());string[] names=peers.Select(j=>j.CharacterName).ToArray();foreach(var j in peers){j.TeamMembers=names;j.TeamReadAt=DateTime.UtcNow;j.MapId=68;j.ZoneId=17;j.BeastNearRasa=true;j.Skills=new[]{new SkillInfo{Id=295,Type=0,Level=10}};}
 var leader=peers[0];NativeBackend.ConfigureIzanamiTeam(new[]{leader.Id});Check(leader.TeamJobIds.Length==6,"cursed-quest UI sync overwrote beast team");Check(NativeBackend.IsBeastLeader(leader)&&!NativeBackend.IsBeastLeader(peers[1]),"selected order lost leader");
 Check(NativeBackend.BeastPartyReady(leader,true),"six fresh members near Rasa rejected");peers[5].BeastNearRasa=false;Check(!NativeBackend.BeastPartyReady(leader,true),"distant member permitted receipt");peers[5].BeastNearRasa=true;peers[5].TeamMembers=names.Take(5).ToArray();Check(!NativeBackend.BeastPartyReady(leader,true),"incomplete server party permitted receipt");peers[5].TeamMembers=names;
 using(var r=new Remote(Process.GetCurrentProcess())){long data=r.Allocate(4096);Snapshot(r,data,68,true);I(r,data+120,0);NativeBackend.PumpQuest(r,data,leader);Check(BitConverter.ToInt32(r.Read(data,4),0)==0&&!leader.QuestFinished,"stale weekly snapshot completed beast quest");
 Snapshot(r,data,68,true);NativeBackend.PumpQuest(r,data,leader);Check(BitConverter.ToInt32(r.Read(data,4),0)==1,"existing task bypassed initial Rasa gathering");
 Snapshot(r,data,68,true);peers[5].BeastNearRasa=false;NativeBackend.PumpQuest(r,data,leader);Check(BitConverter.ToInt32(r.Read(data,4),0)==0,"leader opened menu before sixth member reached Rasa");
 peers[5].BeastNearRasa=true;NativeBackend.PumpQuest(r,data,leader);Check(BitConverter.ToInt32(r.Read(data,4),0)==2&&leader.BeastTeamStarted,"leader did not route an existing beast task after party confirmation");
 Snapshot(r,data,74,true);leader.QuestRetryAt=DateTime.MinValue;NativeBackend.PumpQuest(r,data,leader);Check(leader.QuestPhase==6&&leader.QuestMobId==196&&leader.TrainEnabled,"leader did not arm strict task target");
 leader.MapId=74;leader.BeastX=1184;leader.BeastY=186;leader.QuestReturn=false;
 var follower=peers[1];long fd=r.Allocate(4096);Snapshot(r,fd,68,false);NativeBackend.PumpQuest(r,fd,follower);Check(BitConverter.ToInt32(r.Read(fd,4),0)==8&&BitConverter.ToInt32(r.Read(fd+96,4),0)==74&&!follower.QuestFinished,"follower accepted its own task or did not follow leader map");
 Snapshot(r,fd,74,false);follower.BeastX=1180;follower.BeastY=186;NativeBackend.PumpQuest(r,fd,follower);Check(follower.QuestPhase==6&&follower.QuestMobId==196&&follower.TrainEnabled,"follower failed to support exact leader target");
 leader.QuestFinished=true;leader.QuestEnabled=false;Snapshot(r,fd,68,false);NativeBackend.PumpQuest(r,fd,follower);Check(follower.QuestFinished&&!follower.QuestEnabled,"follower did not finish with leader");
 }
 foreach(int size in new[]{1,2,5}){var tail=peers.Take(size).ToArray();NativeBackend.ConfigureBeastTeam(tail.Select(x=>x.Id).ToArray());var tailNames=tail.Select(x=>x.CharacterName).ToArray();foreach(var member in tail){member.TeamMembers=size==1?new string[0]:tailNames;member.TeamReadAt=DateTime.UtcNow;member.MapId=68;member.ZoneId=17;member.BeastNearRasa=true;}Check(NativeBackend.IsBeastLeader(tail[0])&&NativeBackend.BeastPartyReady(tail[0],true),"final partial/solo group did not allow actual membership");tail[size-1].BeastNearRasa=false;Check(!NativeBackend.BeastPartyReady(tail[0],true),"partial group bypassed near Rasa gate");tail[size-1].BeastNearRasa=true;}
 Check(NativeCode.Quest(0x100000,0x180000000).Length<8192,"quest callback exceeds allocation");
 Console.WriteLine("PASS: six-member server party/near-Rasa gate, selected leader, profile acknowledgment, existing task gathering, leader-only menu, follower route and strict target, group completion; no real game action.");
 }finally{foreach(var j in peers){NativeJob removed;jobs.TryRemove(j.Id,out removed);}}
 }
}
