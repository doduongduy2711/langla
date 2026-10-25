using System;using System.Runtime.InteropServices;using System.Diagnostics;using System.Reflection;using System.Collections.Concurrent;using System.Linq;using TGLL;
class BeastQuestTests {
 static void Check(bool b,string s){if(!b)throw new Exception(s);}static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static void Snapshot(Remote r,long d,int map,bool present){I(r,d,0);I(r,d+8,1);I(r,d+12,3);I(r,d+16,present?1:0);I(r,d+20,0);I(r,d+32,0);I(r,d+36,1);I(r,d+64,2);I(r,d+80,map);I(r,d+84,74);I(r,d+88,68);I(r,d+108,196);I(r,d+112,1);I(r,d+120,1);}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Player(IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate bool Contains(IntPtr self,IntPtr value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Close(IntPtr self,IntPtr info);
 static void P(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}
 static void Replace(byte[] b,long from,long to){byte[] f=BitConverter.GetBytes(from),t=BitConverter.GetBytes(to);for(int i=0;i<=b.Length-8;i++){bool match=true;for(int k=0;k<8;k++)if(b[i+k]!=f[k])match=false;if(match){Array.Copy(t,0,b,i,8);i+=7;}}}
 static void MenuClose(){using(var r=new Remote(Process.GetCurrentProcess())){
  long game=r.Allocate(0x1700000),d=r.Allocate(4096),worldClass=r.Allocate(512),stat=r.Allocate(128),world=r.Allocate(1024),player=r.Allocate(256),menuClass=r.Allocate(2048),menu=r.Allocate(512),listClass=r.Allocate(1024),list=r.Allocate(32);
  P(r,game+0x16ee888,worldClass);P(r,worldClass+0xb8,stat);P(r,stat+16,world);P(r,world+0x90,menu);P(r,world+0x88,list);P(r,game+0x16efe08,menuClass);P(r,menu,menuClass);P(r,list,listClass);I(r,player+0x8c,3);I(r,menu+0xdc,11);I(r,d+56,11);P(r,d+128,menu);
  bool visible=true;int closed=0;Player getter=delegate(IntPtr info){return new IntPtr(player);};Contains contains=delegate(IntPtr self,IntPtr value,IntPtr info){Check(self.ToInt64()==list&&value.ToInt64()==menu,"wrong overlay containment check");return visible;};Close close=delegate(IntPtr self,IntPtr info){Check(self.ToInt64()==menu,"closed another popup");closed++;};P(r,listClass+0x208,Marshal.GetFunctionPointerForDelegate(contains).ToInt64());P(r,menuClass+0x578,Marshal.GetFunctionPointerForDelegate(close).ToInt64());
  byte[] code=NativeCode.Quest(d,game);Replace(code,game+0x3672c0,Marshal.GetFunctionPointerForDelegate(getter).ToInt64());long exe=r.Allocate(8192);r.Write(exe,code);r.Protect(exe,8192,0x20);Tick tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));I(r,d+116,1);I(r,d+4,1);I(r,d,9);tick();Check(closed==1&&BitConverter.ToInt32(r.Read(d+36,4),0)==1,"exact Rasa menu did not close");
  P(r,d+128,menu+8);I(r,d,9);tick();Check(closed==1,"replaced popup was closed");P(r,d+128,menu);I(r,menu+0xdc,12);I(r,d,9);tick();Check(closed==1,"another NPC menu closed");I(r,menu+0xdc,11);visible=false;I(r,d,9);tick();Check(closed==1,"dismissed popup closed again");visible=true;I(r,d+116,0);I(r,d,9);tick();Check(closed==1,"weekly quest closed beast menu");
  GC.KeepAlive(getter);GC.KeepAlive(contains);GC.KeepAlive(close);
 }}
 static void Snap(Remote r,long d,NativeJob j,int map,bool present,bool done=false,int remain=3){j.BeastStartupSandReady=true;j.BeastStartupSandPid=j.ProcessId;Snapshot(r,d,map,present);I(r,d+20,done?1:0);I(r,d+12,remain);j.QuestRetryAt=DateTime.MinValue;j.TeamReadAt=DateTime.UtcNow;j.MapId=map;if(j.ZoneId<0)j.ZoneId=10;j.ZoneReadAt=DateTime.UtcNow;}
 static NativeJob StartupJob(Remote r,int pid){return new NativeJob("synthetic.exe","beast-startup","unused",null,true){ProcessId=pid,State="opened",GameSettingsReady=true,TrainReady=true,BeastQuestEnabled=true,QuestEnabled=true,QuestQueueOnly=true,BeastSoloReady=true,TeamReadAt=DateTime.UtcNow,TravelData=r.Allocate(4096),QuestAt=DateTime.UtcNow};}
 static void StartupSand(){using(var r=new Remote(Process.GetCurrentProcess())){
  long data=r.Allocate(4096);var timer=new Stopwatch();var j=StartupJob(r,11);
  Snapshot(r,data,74,false);NativeBackend.PumpQuest(r,data,j);Check(BitConverter.ToInt32(r.Read(data,4),0)==0&&j.QuestPhase==0,"beast routed before first Sand teleport");
  NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.TravelActive&&j.NpcAction==3&&j.ReceiveFlowActive&&BitConverter.ToInt32(r.Read(j.TravelData+4,4),0)==168,"beast did not start item168 Sand teleport");
  DateTime first=j.TravelAt;NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.TravelAt==first,"startup reissued item before acknowledgement");
  I(r,j.TravelData,0);I(r,j.TravelData+8,2);NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.TravelPhase==2,"item receipt not awaited");
  I(r,j.TravelData+64,3);NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.TravelPhase==3,"village submenu not awaited");
  I(r,j.TravelData+64,4);NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.TravelPhase==5&&!j.BeastStartupSandReady,"menu selection falsely counted as arrival");
  I(r,data,16);NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(!j.BeastStartupSandReady,"unknown/wrong map started beast quest");
  NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>true);Check(j.BeastStartupSandReady&&!j.TravelActive&&!j.ReceiveFlowActive&&!j.IzanamiRequested&&j.Stage=="beast_startup_sand_ready"&&BitConverter.ToInt32(r.Read(data,4),0)==12,"Sand arrival started key/dungeon/Onoki instead of beast quest");
  NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(j.BeastStartupSandReady&&!j.TravelActive,"startup teleport repeated during same session");
  Snapshot(r,data,59,false);j.TeamReadAt=DateTime.UtcNow;NativeBackend.PumpQuest(r,data,j);Check(BitConverter.ToInt32(r.Read(data,4),0)==1,"confirmed Sand arrival did not release Rasa navigation");
  j.ProcessId=12;NativeBackend.PumpBeastStartupSand(r,data,j,timer,0,()=>false);Check(!j.BeastStartupSandReady&&j.TravelActive&&j.BeastStartupSandPid==12,"replacement game skipped first teleport");
  j.BeastQuestEnabled=false;NativeBackend.PumpTravel(r,data,j,timer,0);Check(!j.TravelActive&&!j.ReceiveFlowActive,"untick left teleport active");
  var missing=StartupJob(r,22);long d2=r.Allocate(4096);NativeBackend.PumpBeastStartupSand(r,d2,missing,timer,0,()=>false);I(r,missing.TravelData,0);I(r,missing.TravelData+8,3);NativeBackend.PumpBeastStartupSand(r,d2,missing,timer,0,()=>false);
  Check(missing.BeastStartupSandStopped&&!missing.BeastStartupSandReady&&!missing.TravelActive&&missing.QuestStatus.Contains("travel_item_missing"),"missing item was not reported or allowed quest through");
  NativeBackend.PumpBeastStartupSand(r,d2,missing,timer,0,()=>false);Check(!missing.TravelActive,"missing-item startup spams travel");
  var near=StartupJob(r,33);long d3=r.Allocate(4096);NativeBackend.PumpBeastStartupSand(r,d3,near,timer,0,()=>true);Check(near.BeastStartupSandReady&&!near.TravelActive&&BitConverter.ToInt32(r.Read(near.TravelData,4),0)==0,"already confirmed Sand consumed a travel item");
  var unready=StartupJob(r,44);unready.GameSettingsReady=false;NativeBackend.PumpBeastStartupSand(r,d3,unready,timer,0,()=>false);Check(!unready.TravelActive,"startup ran before game readiness");
  Console.WriteLine("PASS: first-session beast Sand teleport item168, menu/real-arrival gates, quest pause then Rasa navigation, once per PID, no key/dungeon/Onoki entry, cancellation, missing-item stop, already-Sand skip and unready gate; no real game action.");
 }}
 static void Zones(ConcurrentDictionary<string,NativeJob> jobs){
 var peers=Enumerable.Range(0,10).Select(i=>new NativeJob("synthetic.exe","zone-"+i,"unused",null,true){CharacterName="zone-"+i,State="opened",GameSettingsReady=true,BeastQuestEnabled=true,QuestEnabled=true,MapId=74,ZoneId=10,ZoneReadAt=DateTime.UtcNow}).ToArray();foreach(var j in peers)jobs[j.Id]=j;
 try{
  var a=peers[0];var b=peers[1];var c=peers[2];int[] ids={10,11,12},counts={0,100,5};Check(NativeBackend.ReserveBeastCombatZone(a,74,10,null,null)==10,"first account did not keep zone 10");Check(NativeBackend.ReserveBeastCombatZone(b,74,10,ids,counts)==12,"next account did not avoid occupied zone 10 and choose quiet unused zone");Check(NativeBackend.ReserveBeastCombatZone(c,74,10,ids,counts)==11,"third account duplicated a pending reservation");Check(NativeBackend.ReserveBeastCombatZone(peers[3],74,10,ids,counts)==-1,"full zones fell back to a duplicate");
  a.MapId=75;Check(NativeBackend.ReserveBeastCombatZone(peers[3],74,10,ids,counts)==10,"account leaving map did not release its zone");Check(NativeBackend.ReserveBeastCombatZone(a,75,10,ids,counts)==10,"different maps unnecessarily shared zone exclusions");NativeBackend.ResetBeastCombatZone(c);Check(NativeBackend.ReserveBeastCombatZone(peers[4],74,10,ids,counts)==11,"completed task did not release its reservation");b.CloseRequested=true;Check(NativeBackend.ReserveBeastCombatZone(peers[5],74,10,ids,counts)==12,"closing account retained a zone");
  foreach(var j in peers){NativeBackend.ResetBeastCombatZone(j);j.MapId=74;j.ZoneId=10;j.CloseRequested=false;}
  int[] many=Enumerable.Range(0,10).ToArray(),pop=Enumerable.Range(0,10).Select(i=>i+1).ToArray();int[] chosen=new int[peers.Length];System.Threading.Tasks.Parallel.For(0,peers.Length,n=>{chosen[n]=NativeBackend.ReserveBeastCombatZone(peers[n],74,10,many,pop);});Check(chosen.All(n=>n>=0)&&chosen.Distinct().Count()==peers.Length,"simultaneous arrivals selected the same zone");
  foreach(var j in peers)NativeBackend.ResetBeastCombatZone(j);NativeBackend.ReserveBeastCombatZone(a,74,10,null,null);NativeBackend.PrepareBeastCombatZone(b,74);Check(NativeBackend.ReserveBeastCombatZone(b,74,10,new[]{10,11},new[]{0,1})==11,"confirmation fixture target wrong");
  using(var r=new Remote(Process.GetCurrentProcess())){long d=r.Allocate(4096);NativeBackend.PumpBeastCombatZones(r,0,d,b);Check(BitConverter.ToInt32(r.Read(d,4),0)==2&&!b.BeastZoneReady,"zone change did not wait for confirmation");I(r,d,4);b.ZonePollAt=DateTime.MinValue;NativeBackend.PumpBeastCombatZones(r,0,d,b);Check(!b.BeastZoneReady,"packet ack accepted while actual zone stayed 10");b.ZoneId=11;b.ZoneReadAt=DateTime.UtcNow;b.ZonePollAt=DateTime.MinValue;NativeBackend.PumpBeastCombatZones(r,0,d,b);Check(b.BeastZoneReady&&!b.ZoneSyncActive&&NativeBackend.PrepareBeastCombatZone(b,74),"actual zone 11 confirmation did not release combat");b.ZoneId=10;Check(!NativeBackend.PrepareBeastCombatZone(b,74),"manual zone change bypassed recheck");}
 }finally{foreach(var j in peers){NativeJob removed;jobs.TryRemove(j.Id,out removed);}}
 }
 static void Main(){var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var peers=Enumerable.Range(0,2).Select(i=>new NativeJob("synthetic.exe","beast-"+i,"unused",null,true){CharacterName="char-"+i,State="opened",GameSettingsReady=true,TrainReady=true,ReceiveKeyEnabled=false}).ToArray();foreach(var j in peers)jobs[j.Id]=j;
 try{
 foreach(var j in peers){j.TeamManaged=true;j.TeamJobIds=peers.Select(x=>x.Id).ToArray();j.ZoneSyncActive=true;j.FollowActive=true;NativeBackend.ConfigureBeastSolo(j.Id);Check(j.BeastQuestEnabled&&j.QuestEnabled&&j.TeamJobIds.Length==0&&!j.TeamManaged&&!j.ZoneSyncActive&&!j.FollowActive&&j.FollowStopPending,"solo mode retained party/follow/zone gates");j.Skills=new[]{new SkillInfo{Id=295,Type=0,Level=10}};}
 var a=peers[0];var other=peers[1];a.QuestPhase=6;a.QuestRemaining=2;NativeBackend.ConfigureBeastSolo(a.Id);Check(a.QuestPhase==6&&a.QuestRemaining==2,"periodic solo configuration reset progress");a.QuestPhase=0;a.QuestRemaining=-1;NativeBackend.ConfigureIzanamiTeam(new[]{a.Id});Check(a.TeamJobIds.Length==0&&!a.TeamManaged,"other activity created a beast team");other.CloseRequested=true;other.TeamReadAt=DateTime.MinValue;
 using(var r=new Remote(Process.GetCurrentProcess())){long d=r.Allocate(4096),o=r.Allocate(4096),team=r.Allocate(4096);a.BeastNearRasa=true;
 a.TeamMembers=new[]{"old-owner",a.CharacterName};a.TeamActionAt=DateTime.MinValue;NativeBackend.PumpBeastSoloParty(r,team,a,0x123400,DateTime.UtcNow);Check(!a.BeastSoloReady&&BitConverter.ToInt32(r.Read(team,4),0)==8&&r.Ptr(team+32)==0x123400,"solo mode did not leave exact existing party");
 Snap(r,d,a,68,false);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==0&&BitConverter.ToInt32(r.Read(d+4,4),0)==0,"receipt started before leave confirmation");
 I(r,team,6);NativeBackend.PumpBeastSoloParty(r,team,a,0x123400,DateTime.UtcNow);Check(!a.BeastSoloReady&&BitConverter.ToInt32(r.Read(team,4),0)==6,"leave ack incorrectly counted as no party or repeated too soon");
 a.TeamMembers=new string[0];NativeBackend.PumpBeastSoloParty(r,team,a,0,DateTime.UtcNow);other.BeastSoloReady=true;Check(a.BeastSoloReady,"server-confirmed empty party did not release solo quest");

 Snap(r,d,a,68,false);I(r,d+120,0);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==0&&!a.QuestFinished,"stale weekly profile consumed as beast data");
 Snap(r,d,a,68,false);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==1&&BitConverter.ToInt32(r.Read(d,4),0)==0,"near Rasa reopened navigation unnecessarily");
 Snap(r,d,a,68,false);I(r,d+88,0);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==3,"solo receipt waited for another account or party");
 Snap(r,d,a,68,false);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==4,"solo root menu missing");Check(!NativeBackend.ShouldCloseBeastRasaMenu(a),"owned receipt menu closed");
 Snap(r,d,a,68,false);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==5,"solo account did not Receive");
 Snap(r,d,a,68,false);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==5,"receive acknowledgement not awaited");
 Snap(r,d,a,68,true);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==0,"server receipt did not advance");
 Snap(r,d,a,68,true);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==2,"existing task did not route directly to target");
 Snap(r,d,a,74,true);a.BeastNearRasa=false;NativeBackend.PumpQuest(r,d,a);Check(!a.TrainEnabled&&a.BeastZoneWaiting,"combat started before checking other account zones");NativeBackend.PumpBeastCombatZones(r,0,r.Allocate(4096),a);Snap(r,d,a,74,true);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==6&&a.TrainEnabled&&a.QuestMobId==196,"solo strict target combat not armed");
 other.CloseRequested=false;Snap(r,o,other,59,false,false,0);NativeBackend.PumpQuest(r,o,other);Check(other.QuestFinished&&!other.QuestEnabled&&!a.QuestFinished&&a.QuestRemaining==3,"one account completion changed another account");
 Snap(r,d,a,74,true,true);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==0&&!a.TrainEnabled,"completion did not stop combat");
 Snap(r,d,a,74,true,true);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==1,"solo task did not return to Rasa");
 a.BeastNearRasa=true;Snap(r,d,a,68,true,true);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==3,"solo return waited for completed/offline peer");
 Snap(r,d,a,68,true,true);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==4,"turn-in root menu missing");
 Snap(r,d,a,68,true,true);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==7,"solo account did not Complete");
 Snap(r,d,a,68,true,true);NativeBackend.PumpQuest(r,d,a);Check(a.QuestPhase==7&&!a.QuestFinished,"menu ack counted as completion");
 Snap(r,d,a,68,false,false,2);NativeBackend.PumpQuest(r,d,a);Check(a.QuestRemaining==2&&!a.QuestFinished&&BitConverter.ToInt32(r.Read(d,4),0)==3,"remaining solo round did not reopen receipt");
 Snap(r,d,a,68,false,false,2);NativeBackend.PumpQuest(r,d,a);Snap(r,d,a,68,false,false,2);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==5,"second solo receipt blocked by finished account");
 a.QuestPhase=3;Snap(r,d,a,68,true,false,2);NativeBackend.PumpQuest(r,d,a);Check(BitConverter.ToInt32(r.Read(d,4),0)==0&&a.QuestPhase==0,"existing active task received again");
 a.QuestPhase=3;Snap(r,d,a,68,true,true,0);NativeBackend.PumpQuest(r,d,a);Check(!a.QuestFinished&&BitConverter.ToInt32(r.Read(d,4),0)==7,"zero turns skipped final pending reward");
 Snap(r,d,a,68,false,false,0);NativeBackend.PumpQuest(r,d,a);Check(a.QuestFinished&&!a.QuestEnabled,"own final server confirmation not required");
 }
 StartupSand();Zones(jobs);MenuClose();Check(NativeCode.Quest(0x100000,0x180000000).Length<8192,"quest callback exceeds allocation");Console.WriteLine("PASS: solo mode clears group/follow/zone gates, idempotent sync, exact-party leave and server-confirmed solo gate, independent Receive/strict combat/Complete/remaining rounds, per-account finish, distinct map/zone reservations, concurrent arrivals, departure/close reuse, actual zone confirmation, server confirmation and exact Rasa menu close; no real game action.");
 }finally{foreach(var j in peers){NativeJob removed;jobs.TryRemove(j.Id,out removed);}}
 }
}
