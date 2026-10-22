using System;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class PortalTests126 {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){var j=new NativeJob("synthetic.exe","fixture","unused",null,true);j.SonCapEnabled=true;j.SonCapMoving=true;j.SonCapStage=0;j.SonCapAlive=0;j.MapId=94;j.TrainReady=true;j.GameSettingsReady=true;var t=DateTime.UtcNow;
 C(!NativeBackend.PlanSonCapRight(j,2284,270,false,t),"no arrival wait");C(NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(1)),"near actual gate did not nudge");
 j.GameAutoEnabled=true;C(!NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(2)),"idle combat active while key pressed");j.GameAutoEnabled=false;
 j.SonCapAlive=1;C(!NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(3)),"living monsters allowed nudge");j.SonCapAlive=0;
 C(!NativeBackend.PlanSonCapRight(j,2284,270,true,t.AddSeconds(4)),"active route allowed key");C(!NativeBackend.PlanSonCapRight(j,2000,270,false,t.AddSeconds(4)),"far from gate allowed key");
 NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(5));j.SonCapRightAt=t.AddSeconds(5);C(!NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(6)),"nudge spammed");C(NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(8)),"spaced retry rejected");
 j.SonCapRightAttempts=3;C(!NativeBackend.PlanSonCapRight(j,2284,270,false,t.AddSeconds(12)),"unbounded retries");j.SonCapRightAttempts=0;j.CloseRequested=true;C(!NativeBackend.SonCapNudgeAllowed(j),"closed game allowed key");j.CloseRequested=false;j.SonCapEnabled=false;C(!NativeBackend.SonCapNudgeAllowed(j),"stopped auto allowed key");
 Console.WriteLine("PASS: portal arrival, combat off, no living mobs, stopped route, distance, stable arrival, cooldown, 3-attempt cap and cancellation; no keys sent.");}
}
