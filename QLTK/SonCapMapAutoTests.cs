using System;using TGLL;
class SonCapMapAutoTests {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){
  NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/soncap138-map-logs");
  var j=new NativeJob("synthetic.exe","fixture","unused",null,true);j.SonCapEnabled=true;j.TrainReady=true;j.GameSettingsReady=true;
  DateTime t=new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
  foreach(int map in new[]{94,95,97,93,96}){
   j.ReceiveFlowActive=true;j.State="waiting_popup";j.GameAutoWanted=false;
   NativeBackend.ObserveSonCapMapAuto(j,map,true,t);C(!j.SonCapMapAutoActive,"enabled before delay");
   NativeBackend.ObserveSonCapMapAuto(j,map,true,t.AddMilliseconds(2999));C(!j.SonCapMapAutoActive,"delay too short");
   NativeBackend.ObserveSonCapMapAuto(j,map,true,t.AddSeconds(3));C(j.SonCapMapAutoActive&&NativeBackend.GameAutoDesired(j)&&!j.ReceiveFlowActive,"stale NPC state blocked map combat");
   j.GameAutoEnabled=false;NativeBackend.ObserveSonCapMapAuto(j,map,true,t.AddSeconds(4));C(NativeBackend.GameAutoDesired(j),"disabled game idle not restored");t=t.AddSeconds(10);
  }
  j.SonCapStage=4;j.SonCapAlive=0;NativeBackend.ObserveSonCapMapAuto(j,96,true,t);C(!j.SonCapMapAutoActive,"empty map guard forced combat");
  j.SonCapAlive=2;j.SonCapMoving=true;NativeBackend.ObserveSonCapMapAuto(j,96,true,t);C(!j.SonCapMapAutoActive&&!NativeBackend.GameAutoDesired(j),"portal interrupted");
  j.SonCapMoving=false;j.SonCapStopPending=true;NativeBackend.ObserveSonCapMapAuto(j,96,true,t);C(!j.SonCapMapAutoActive,"pending route cancellation ignored");
  j.SonCapStopPending=false;NativeBackend.ObserveSonCapMapAuto(j,96,false,t);C(!j.SonCapMapAutoActive,"loading screen accepted");
  NativeBackend.ObserveSonCapMapAuto(j,59,true,t);C(!j.SonCapMapAutoActive,"village enabled");
  NativeBackend.ObserveSonCapMapAuto(j,96,true,t);NativeBackend.ObserveSonCapMapAuto(j,96,true,t.AddSeconds(3));C(j.SonCapMapAutoActive,"resume failed");
  j.SonCapFinished=true;NativeBackend.ObserveSonCapMapAuto(j,96,true,t.AddSeconds(4));C(!j.SonCapMapAutoActive,"finished job restarted");
  j.SonCapFinished=false;j.SonCapEnabled=false;NativeBackend.ObserveSonCapMapAuto(j,96,true,t.AddSeconds(4));C(!j.SonCapMapAutoActive,"manual stop ignored");
  Console.WriteLine("PASS: five map IDs, 3-second delay, stale NPC recovery, idle restoration, empty/portal/loading/finish/stop gates.");
 }
}
