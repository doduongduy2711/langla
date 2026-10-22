using System;using System.Diagnostics;using System.Reflection;using TGLL;
class QuestTargetTests{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static void Main(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/test148logs");using(var r=new Remote(Process.GetCurrentProcess())){
  long q=r.Allocate(4096),t=r.Allocate(4096);var j=new NativeJob("synthetic","fixture","",null,true);
  j.QuestEnabled=true;j.ReceiveKeyEnabled=false;j.GameSettingsReady=true;j.QuestPhase=6;j.QuestAt=DateTime.UtcNow;
  j.Skills=new[]{new SkillInfo{Id=100,Type=1},new SkillInfo{Id=200,Type=0},new SkillInfo{Id=295,Type=0}};
  I(r,q+8,1);I(r,q+12,10);I(r,q+16,1);I(r,q+24,23);I(r,q+28,40);I(r,q+64,2);I(r,q+80,74);I(r,q+84,74);I(r,q+108,180);I(r,q+112,1);
  NativeBackend.PumpQuest(r,q,j);Check(j.TrainEnabled&&j.TrainSkillId==295&&j.QuestMobId==180&&!NativeBackend.GameAutoDesired(j),"quest owns filtered combat and prioritizes attack skill 295");
  var pump=typeof(NativeBackend).GetMethod("PumpTraining",BindingFlags.Static|BindingFlags.NonPublic);
  j.GameAutoEnabled=true;pump.Invoke(null,new object[]{r,t,j});Check(BitConverter.ToInt32(r.Read(t+12,4),0)==0,"wait for builtin auto to actually stop");
  I(r,t,0);j.GameAutoEnabled=false;pump.Invoke(null,new object[]{r,t,j});Check(BitConverter.ToInt32(r.Read(t+12,4),0)==1&&BitConverter.ToInt32(r.Read(t+80,4),0)==1,"arm strict native quest mode after confirmation");
  Check(NativeBackend.TrainingPollMs(j)==150,"quest polling bounded to 150 ms");
  I(r,q+80,88);NativeBackend.PumpQuest(r,q,j);Check(j.QuestPhase==0&&j.QuestPauseAuto&&!j.TrainEnabled,"wrong map during combat pauses and reroutes");
  NativeBackend.PumpQuest(r,q,j);Check(BitConverter.ToInt32(r.Read(q,4),0)==2,"reroute requests quest destination");
  j.QuestKind=1;j.QuestMobId=180;j.Skills=new[]{new SkillInfo{Id=200,Type=0}};NativeBackend.ArmQuestCombat(j);Check(j.TrainEnabled&&j.TrainSkillId==200,"other offensive skill works without 295");
  j.Skills=new[]{new SkillInfo{Id=100,Type=1}};NativeBackend.ArmQuestCombat(j);Check(!j.TrainEnabled&&j.SkillScanRequested,"support-only skills cannot substitute for attacking quest mobs");
  j.QuestKind=-1;NativeBackend.ArmQuestCombat(j);Check(!j.TrainEnabled,"unknown task never guesses target");
  Console.WriteLine("PASS: target snapshot, quest ownership, builtin stop confirmation, fallback attack, missing skill, unknown task, wrong-map reroute and polling interval.");
 }}
}
