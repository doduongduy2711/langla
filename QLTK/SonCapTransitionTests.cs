using System;using TGLL;
class SonCapTransitionTests {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/soncap142-logs");var t=new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
  var j=new NativeJob("synthetic.exe","fixture","unused",null,true);j.SonCapEnabled=true;j.TrainReady=true;j.GameSettingsReady=true;j.SonCapStage=0;j.SonCapMoving=true;j.SonCapStopPending=true;j.SonCapAlive=0;
  NativeBackend.ObserveSonCapMapAuto(j,95,true,t);C(j.SonCapStage==1&&!j.SonCapMoving&&!j.SonCapStopPending&&j.SonCapAlive==-1,"new map stayed in previous portal step");
  NativeBackend.ObserveSonCapMapAuto(j,95,true,t.AddMilliseconds(2999));C(!NativeBackend.GameAutoDesired(j),"enabled too soon");
  NativeBackend.ObserveSonCapMapAuto(j,95,true,t.AddSeconds(3));C(NativeBackend.GameAutoDesired(j)&&!j.SonCapAutoConfirmed,"unknown mobs blocked combat or false confirmation");
  C(NativeBackend.CanSubmitGameAuto(j,true,t.AddSeconds(3)),"initial request missing");C(!NativeBackend.CanSubmitGameAuto(j,true,t.AddSeconds(5)),"retry too fast");C(NativeBackend.CanSubmitGameAuto(j,true,t.AddSeconds(6)),"retry never allowed");
  j.GameAutoEnabled=true;NativeBackend.ObserveSonCapMapAuto(j,95,true,t.AddSeconds(7));C(j.SonCapAutoConfirmed&&!NativeBackend.CanSubmitGameAuto(j,true,t.AddSeconds(9)),"confirmed idle resent");
  j.GameAutoEnabled=false;NativeBackend.ObserveSonCapMapAuto(j,95,true,t.AddSeconds(10));C(!j.SonCapAutoConfirmed&&NativeBackend.GameAutoDesired(j),"lost idle not restored");
  NativeBackend.ObserveSonCapMapAuto(j,97,false,t.AddSeconds(11));C(!j.SonCapMapAutoActive&&j.SonCapStage==1,"loading transition accepted");NativeBackend.ObserveSonCapMapAuto(j,97,true,t.AddSeconds(12));C(j.SonCapStage==2&&!NativeBackend.GameAutoDesired(j),"new stage delay not reset");
  j.SonCapAlive=0;NativeBackend.ObserveSonCapMapAuto(j,97,true,t.AddSeconds(16));C(!NativeBackend.GameAutoDesired(j),"empty stage combat forced");
  Console.WriteLine("PASS: new map clears stale portal without mob snapshot, stable 3-second delay, confirmation/loss, bounded retries, loading/reset and empty map gates.");
 }
}
