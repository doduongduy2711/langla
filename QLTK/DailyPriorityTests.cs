using System;using System.Diagnostics;using System.Collections.Concurrent;using System.Reflection;using TGLL;
class DailyTests119 {
 static ConcurrentDictionary<string,NativeJob> Jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static DateTime Before=new DateTime(2026,10,11,16,59,59,DateTimeKind.Utc),After=new DateTime(2026,10,11,17,0,0,DateTimeKind.Utc);
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static NativeJob Fixture(bool key,bool dungeon,bool iza,bool quest){
  var j=new NativeJob("C:\\synthetic.exe","fixture","unused",null,true);j.State="opened";j.TrainReady=true;j.GameSettingsReady=true;j.CharacterLevel=54;j.ReceiveKeyEnabled=false;Jobs[j.Id]=j;
  NativeBackend.ConfigureDailyPriority(j.Id,true,key,dungeon,iza,quest);j.DailyLastDate=new DateTime(2026,10,11);
  var spot=new FarmSpot{MapId=74,MapName="Dòng Sông Kusagakure",MobId=-1,MobName="Dị cây",Level=54,X=1490,Y=565};
  NativeBackend.StartMapFarm(j.Id,false,54,new[]{spot},true);j.FarmTarget=spot;j.FarmArrived=true;return j;
 }
 static void Pulse(){NativeBackend.PulseDailyPriority(After);}
 static void Main(){
  NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/daily-test-logs");
  NativeJob j=Fixture(true,true,true,true);NativeBackend.ConfigureDailyTeams(new[]{j.Id});
  NativeBackend.PulseDailyPriority(Before);C(!j.DailyActive&&j.FarmArrived,"runs before local midnight");
  Pulse();C(j.DailyActive&&j.DailyStage==0&&!j.FarmEnabled&&j.FarmStopPending,"midnight did not pause farm");Pulse();C(j.DailyStage==0,"did not wait for owned route cancellation");
  j.FarmStopPending=false;Pulse();Pulse();C(j.DailyStage==1&&j.ReceiveKeyEnabled&&!j.DungeonHighEnabled&&!j.IzanamiEnabled&&!j.QuestEnabled,"key must be the only active step");
  j.State="key_submitted";Pulse();C(j.DailyStage==1,"submitted key command counted as receipt");
  j.KeyReceiptPending=true;j.KeyReceiptBefore=0;j.KeyReceiptAt=DateTime.UtcNow;bool closed=false;
  NativeBackend.PumpKeyReceipt(null,0,j,new Stopwatch(),0,delegate{return new InventorySnapshot(new[]{new InventoryItem{Name="Chìa khóa địa cung (cao cấp)",Quantity=1}},10,true,"",DateTime.UtcNow);},delegate{closed=true;},delegate{return "";});
  C(j.KeyReceiptVerified&&!closed&&!j.CloseRequested,"verified key closed game instead of advancing");
  Pulse();Pulse();C(j.DailyStage==2&&j.DungeonHighEnabled&&!j.ReceiveKeyEnabled,"dungeon not second");
  j.DungeonKeyCount=0;j.DungeonCycleWaiting=true;j.DungeonFinished=false;Pulse();C(j.DailyStage==2,"empty keys closed last active dungeon round");
  j.DungeonFinished=true;j.DungeonCycleWaiting=false;j.ReceiveFlowActive=false;Pulse();Pulse();C(j.DailyStage==3&&j.IzanamiEnabled&&j.TeamJobIds.Length==1,"Izanami not third");
  j.IzanamiRoundComplete=true;j.IzanamiRoundsCompleted=j.IzanamiRoundsTarget;Pulse();Pulse();C(j.DailyStage==4&&j.QuestEnabled&&!j.IzanamiEnabled,"quest not fourth");
  j.QuestEnabled=false;j.QuestFinished=false;Pulse();C(j.DailyStage==4&&j.DailyActive,"quest stop mistaken as finish");
  j.QuestFinished=true;Pulse();Pulse();C(!j.DailyActive&&j.FarmEnabled&&!j.FarmArrived&&j.FarmChoices[0].X==1490&&j.FarmChoices[0].Y==565&&j.FarmChoices[0].MapId==74,"did not restore exact farming destination");
  j.FarmArrived=true;for(int i=0;i<20;i++)Pulse();C(!j.DailyActive&&j.FarmEnabled,"same day restarted daily sequence");
  NativeBackend.SetReceiveKey(j.Id,true);NativeBackend.SetDungeonHigh(j.Id,true);NativeBackend.SetIzanami(j.Id,true);NativeBackend.SetAutoQuest(j.Id,true);
  C(!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled&&!j.IzanamiEnabled&&!j.QuestEnabled&&j.FarmEnabled,"ticking all auto interrupted pre-midnight farming");
  NativeBackend.StopMapFarm(j.Id);C(!j.DailyManaged&&!j.DailyActive&&!j.FarmEnabled,"manual stop did not cancel daily ownership");Jobs.Clear();
  NativeJob a=Fixture(false,false,true,false),b=Fixture(false,false,true,false);NativeBackend.ConfigureDailyTeams(new[]{a.Id,b.Id});
  Pulse();a.FarmStopPending=false;Pulse();Pulse();C(a.DailyStage==3&&!a.DailyExecuting,"leader entered before teammate reached same step");
  b.FarmStopPending=false;Pulse();Pulse();C(a.DailyExecuting&&b.DailyExecuting&&a.IzanamiEnabled&&b.IzanamiEnabled&&a.TeamJobIds[0]==a.Id,"team did not start together in table order");
  a.State="failed";NativeJob izaReconnect=new NativeJob("C:\\synthetic.exe","iza-reconnect","unused",null,true);izaReconnect.State="opened";izaReconnect.TrainReady=true;izaReconnect.GameSettingsReady=true;izaReconnect.ReceiveKeyEnabled=false;Jobs[izaReconnect.Id]=izaReconnect;C(NativeBackend.TransferDailyPriority(a.Id,izaReconnect.Id),"Izanami reconnect not transferred");Pulse();C(izaReconnect.IzanamiEnabled&&izaReconnect.DailyExecuting&&b.TeamJobIds[0]==izaReconnect.Id,"party references not restored after reconnect");a=izaReconnect;
  a.IzanamiRoundComplete=true;a.IzanamiRoundsCompleted=a.IzanamiRoundsTarget;Pulse();C(a.DailyStage==3,"team advanced before every member finished");b.IzanamiNoAttempts=true;Pulse();Pulse();Pulse();C(a.FarmEnabled&&b.FarmEnabled&&!a.DailyActive&&!b.DailyActive,"no-attempt teammate blocked completion");Jobs.Clear();
  NativeJob first=Fixture(false,true,false,false),second=Fixture(false,true,false,false);NativeBackend.DailyDungeonLimit=1;
  Pulse();first.FarmStopPending=false;second.FarmStopPending=false;Pulse();Pulse();
  C((first.DungeonHighEnabled?1:0)+(second.DungeonHighEnabled?1:0)==1,"dungeon concurrency limit exceeded");
  NativeJob running=first.DungeonHighEnabled?first:second,waiting=first.DungeonHighEnabled?second:first;running.DungeonFinished=true;Pulse();Pulse();C(waiting.DungeonHighEnabled,"capacity not released after completion");Jobs.Clear();
  NativeJob disconnect=Fixture(true,true,false,true);Pulse();disconnect.FarmStopPending=false;Pulse();Pulse();disconnect.KeyReceiptVerified=true;Pulse();Pulse();C(disconnect.DailyStage==2,"reconnect test must be in dungeon");disconnect.State="failed";
  NativeJob replacement=new NativeJob("C:\\synthetic.exe","replacement","unused",null,true);replacement.State="opened";replacement.TrainReady=true;replacement.GameSettingsReady=true;replacement.ReceiveKeyEnabled=false;Jobs[replacement.Id]=replacement;
  C(NativeBackend.TransferDailyPriority(disconnect.Id,replacement.Id),"daily stage not transferred");Pulse();C(replacement.DailyStage==2&&replacement.DungeonHighEnabled&&!replacement.ReceiveKeyEnabled,"reconnect restarted from key instead of pending dungeon");C(!disconnect.DailyManaged&&!disconnect.DailyActive,"old session retained ownership");Jobs.Clear();
  NativeJob[] six=new NativeJob[6];string[] sixIds=new string[6];for(int n=0;n<6;n++){six[n]=Fixture(false,false,true,false);sixIds[n]=six[n].Id;}NativeBackend.DailyIzanamiLimit=6;NativeBackend.ConfigureDailyTeams(sixIds);Pulse();foreach(NativeJob p in six)p.FarmStopPending=false;Pulse();Pulse();Pulse();foreach(NativeJob p in six)C(p.DailyExecuting&&p.TeamJobIds.Length==6&&p.TeamJobIds[0]==six[0].Id,"six account party must start in table order");foreach(NativeJob p in six){p.IzanamiRoundComplete=true;p.IzanamiRoundsCompleted=p.IzanamiRoundsTarget;}Pulse();Pulse();Pulse();foreach(NativeJob p in six)C(p.FarmEnabled&&!p.DailyActive,"whole completed party did not return to farm");Jobs.Clear();
  NativeJob noPlan=Fixture(false,false,false,false);Pulse();C(noPlan.FarmEnabled&&!noPlan.DailyActive,"unchecked activities paused farm");Jobs.Clear();
  Console.WriteLine("PASS: Vietnam midnight, once daily, key receipt verification without close, strict priority, last dungeon round, quest completion gate, exact return spot, six-account team barrier/order and reconnect continuation, capacity and manual cancellation.");
 }
}
