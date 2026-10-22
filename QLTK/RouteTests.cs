using System;using TGLL;
class RouteTests127 {
 static void C(bool b,string s){if(!b)throw new Exception(s);}
 static void Main(){var j=new NativeJob("synthetic.exe","fixture","unused",null,true);j.SonCapEnabled=true;j.SonCapMoving=true;j.SonCapStage=2;j.MapId=97;j.SonCapAlive=0;j.TrainReady=true;j.GameSettingsReady=true;var t=DateTime.UtcNow;j.SonCapRouteAt=t.AddSeconds(-10);
 C(!NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t),"retry before stable position");C(NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(3)),"stuck map 3 did not retry");
 j.SonCapRouteRetryAt=t.AddSeconds(3);j.SonCapRouteRetries=1;C(!NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(4)),"spam retry");C(NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(11)),"spaced retry missing");
 C(!NativeBackend.PlanSonCapRouteRetry(j,2860,475,false,t.AddSeconds(12)),"moving player interrupted");C(!NativeBackend.PlanSonCapRouteRetry(j,2860,475,true,t.AddSeconds(20)),"active route overwritten");
 C(!NativeBackend.PlanSonCapRouteRetry(j,3234,436,false,t.AddSeconds(21)),"at gate retried navigation");
 j.SonCapRouteRetries=3;NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(22));C(!NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(30)),"unbounded retry");j.SonCapRouteRetries=0;j.SonCapAlive=1;C(!NativeBackend.PlanSonCapRouteRetry(j,2844,475,false,t.AddSeconds(31)),"living monster navigation");
 Console.WriteLine("PASS: map 3 stopped early, 3-second stable gate, 8-second retry spacing, moving-player/active-route protection, no retry at portal, 3-attempt bound, living-monster guard.");}
}
