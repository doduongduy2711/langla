using System;using System.Diagnostics;using System.IO;using System.Reflection;using System.Text;using TGLL;
class StartupTrainingRetryTests {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}
 static int I(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static void Pump(Remote r,long data,NativeJob j){typeof(NativeBackend).GetMethod("PumpTraining",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{r,data,j});}
 static int Main(){try{
  NativeBackend.LogDirectory=Path.Combine(Path.GetTempPath(),"QLTK-startup-training-"+Guid.NewGuid().ToString("N"));
  using(var r=new Remote(Process.GetCurrentProcess())){
   long data=r.Allocate(4096);int attempts=0,waits=0;
   var j=new NativeJob("fixture","synthetic","",null,true){SonCapEnabled=true,IzanamiEnabled=true,TeamManaged=true,TeamJobIds=new[]{"leader","member"},TrainSkillIds=new[]{1}};
   NativeBackend.ExecuteLaunch(j,delegate(NativeJob n){
    attempts++;
    if(attempts==1){n.Stage="popup";n.TrainReady=true;n.TrainIdleApplied=true;n.SkillScanRequested=false;n.SkillGeneration=2;n.Skills=new[]{new SkillInfo{Id=999}};n.LastSkills="old-process";n.HangingAwaiting=true;n.LevelOneNextAt=DateTime.UtcNow.AddMinutes(5);throw new Exception("game_exited");}
    Check(!n.TrainReady&&!n.TrainIdleApplied&&n.SkillScanRequested&&n.SkillGeneration==0&&n.Skills.Length==0,"fresh process cannot inherit idle/skill snapshot");
    Check(!n.HangingAwaiting&&n.LevelOneNextAt==DateTime.MinValue,"old outstanding cast discarded");
    Check(n.SonCapEnabled&&n.IzanamiEnabled&&n.TeamManaged&&n.TeamJobIds.Length==2&&n.TrainSkillIds[0]==1,"activity, team and selected skills retained");
    Pump(r,data,n);Check(n.TrainReady&&I(r,data)==1&&I(r,data+12)==0,"new channel scans without starting combat");
    long name=r.Allocate(64);I(r,name+16,5);r.Write(name+20,Encoding.Unicode.GetBytes("Sword"));I(r,data,0);I(r,data+4,2);I(r,data+8,1);I(r,data+128,1);I(r,data+132,30);r.Write(data+144,BitConverter.GetBytes(name));I(r,data+152,1);I(r,data+156,0);
    Pump(r,data,n);Check(n.Skills.Length==1&&n.Skills[0].Id==1&&n.SkillGeneration==2,"same native generation in a new process is accepted");n.State="opened";
   },delegate(NativeJob n,int delay){waits++;});
   Check(attempts==2&&waits==1&&j.State=="opened"&&j.TrainReady,"retry reaches ready state");
   // Also repair a lost readiness flag on an already running channel.
   I(r,data,0);j.TrainReady=false;j.TrainIdleApplied=true;j.SkillScanRequested=false;Pump(r,data,j);Check(j.TrainReady&&I(r,data)==1,"idle fast path cannot permanently hide an unready session");
   I(r,data,0);j.SkillScanRequested=false;j.TrainIdleApplied=true;Pump(r,data,j);Check(I(r,data)==0,"normal ready idle session keeps cheap fast path");
   Console.WriteLine("PASS: popup game-exit retry discards stale training state, accepts repeated generation, preserves team/activity/preferences, restores readiness without combat, self-heals lost readiness and keeps idle fast path.");
  }return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
