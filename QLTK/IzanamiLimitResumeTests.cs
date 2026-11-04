using System;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class IzanamiLimitResumeTests {
 static void C(bool ok,string text){if(!ok)throw new Exception(text);}
 static ConcurrentDictionary<string,NativeJob> jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
 const string Notice="Người chơi Edolria không đủ lượt tham gia cấm thuật";
 static NativeJob New(bool up){var j=new NativeJob("fixture.exe","fixture","",null,true){TrainReady=true,GameSettingsReady=true,State="opened",IzanamiEnabled=true,DailyManaged=true,DailyActive=true,DailyStage=3,DailyExecuting=true,DailyStatus="Đang đi cấm thuật",MapId=59,PersistentUp=up,UpRequested=up,ReceiveFlowActive=true};j.DailyPlan=j.DailyRunPlan=up?new[]{false,false,true,true,false}:new[]{false,false,true,true};jobs[j.Id]=j;return j;}
 static void Main(){
  NativeBackend.LogDirectory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"QLTK-izanami-resume-"+Guid.NewGuid().ToString("N"));
  foreach(bool up in new[]{false,true}){
   jobs.Clear();var old=New(up);old.State="closed";old.CloseRequested=true;var current=New(up);current.TeamJobIds=new[]{old.Id};current.DailyTeamIds=new[]{old.Id};
   C(NativeBackend.ObserveBlockingIzanamiLimit(current,Notice,59),"live refusal ignored after reconnect");
   C(current.IzanamiNoAttempts&&current.IzanamiRunFinished&&!current.IzanamiRequested&&!current.ReceiveFlowActive,"current session not stopped when team retains old session ID");
   C(old.State=="closed"&&!old.IzanamiNoAttempts,"old closed session was changed/revived");
   NativeBackend.PulseDailyPriority(DateTime.UtcNow);
   C(current.DailyStage==4&&!current.IzanamiEnabled,"exhausted account still waiting for stale team instead of next configured activity");
   if(up)C(current.UpCompleted[2],"Up did not remember exhausted activity completion");
  }
  jobs.Clear();var stale=New(true);stale.MapId=89;stale.IzanamiRoundEntered=true;
  C(NativeBackend.ObserveBlockingIzanamiLimit(stale,Notice,59)&&!stale.IzanamiRoundEntered&&stale.IzanamiNoAttempts,"fresh village map did not override stale entered/map flags");
  var inside=New(true);C(!NativeBackend.ObserveBlockingIzanamiLimit(inside,Notice,89)&&!inside.IzanamiNoAttempts,"fresh in-instance map was interrupted");
  jobs.Clear();var inactive=New(true);inactive.IzanamiEnabled=false;
  C(!NativeBackend.ObserveBlockingIzanamiLimit(inactive,Notice,59),"old modal completed a different activity");
  jobs.Clear();Console.WriteLine("PASS: stale team ID after reconnect stops the actual reporting session, preserves closed sessions, advances Daily and Up to next activity, remembers completion, trusts fresh village map and preserves active instance/inactive activity. No real game action.");
 }
}
