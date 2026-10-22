using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Text;using TGLL;
class TrainTests{
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Count(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Element(IntPtr self,int i,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Getter(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Distance(IntPtr player,IntPtr mob,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Cast(IntPtr screen,IntPtr skill,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Attack(IntPtr screen,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Ctor(IntPtr self,int command,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void WriteShort(IntPtr self,int value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Send(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Barrier(IntPtr field,IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate bool Contains(IntPtr self,IntPtr item,IntPtr info);
 static void Assert(bool b,string s){if(!b)throw new Exception(s);}
 static void Put(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}
 static long Fn(Delegate d){return Marshal.GetFunctionPointerForDelegate(d).ToInt64();}
 static int Main(){try{using(Remote r=new Remote(Process.GetCurrentProcess())){
  long data=r.Allocate(4096),canvasSlot=r.Allocate(8),canvasClass=r.Allocate(256),statics=r.Allocate(16),canvas=r.Allocate(512),fcClass=r.Allocate(512),fcSlot=r.Allocate(8),fc=r.Allocate(1024),charClass=r.Allocate(256),charSlot=r.Allocate(8),charStatics=r.Allocate(16),player=r.Allocate(512),mobSlot=r.Allocate(8),mobClass=r.Allocate(512),npcClass=r.Allocate(512);
  Put(r,canvasSlot,canvasClass);Put(r,canvasClass+0xb8,statics);Put(r,statics,canvas);Put(r,canvas+0x1b8,fc);Put(r,fcSlot,fcClass);Put(r,fc,fcClass);Put(r,charSlot,charClass);Put(r,charClass+0xb8,charStatics);Put(r,charStatics+8,player);r.Write(player+0xac,BitConverter.GetBytes(100));Put(r,mobSlot,mobClass);
  long skill=r.Allocate(80),buff=r.Allocate(80),template=r.Allocate(64),buffTemplate=r.Allocate(64),name=r.Allocate(64);r.Write(skill+0x10,BitConverter.GetBytes((short)901));r.Write(skill+0x12,BitConverter.GetBytes((short)7));r.Write(skill+0x14,new byte[]{1});r.Write(skill+0x1c,BitConverter.GetBytes(450));r.Write(buff+0x10,BitConverter.GetBytes((short)902));r.Write(buffTemplate+0x2c,new byte[]{6});r.Write(template+0x2c,new byte[]{1});Put(r,template+0x18,name);r.Write(name+16,BitConverter.GetBytes(6));r.Write(name+20,Encoding.Unicode.GetBytes("Attack"));
  long near=r.Allocate(160),far=r.Allocate(160),dead=r.Allocate(160),npc=r.Allocate(160);foreach(long m in new long[]{near,far,dead}){Put(r,m,mobClass);r.Write(m+0x78,BitConverter.GetBytes(200));}Put(r,npc,npcClass);r.Write(dead+0x3d,new byte[]{6});
  long vectorClass=r.Allocate(1024),skills=r.Allocate(32),mobs=r.Allocate(32);Put(r,skills,vectorClass);Put(r,mobs,vectorClass);Put(r,fc+0x2d0,skills);Put(r,fc+0x160,mobs);
  Count count=delegate(IntPtr self,IntPtr info){return self.ToInt64()==skills?2:4;};Element element=delegate(IntPtr self,int i,IntPtr info){return new IntPtr(self.ToInt64()==skills?(i==0?skill:buff):new long[]{npc,dead,far,near}[i]);};Getter getter=delegate(IntPtr self,IntPtr info){return new IntPtr(self.ToInt64()==skill?template:buffTemplate);};Distance distance=delegate(IntPtr self,IntPtr m,IntPtr info){Assert(self.ToInt64()==player,"player identity");return m.ToInt64()==near?10:40;};int calls=0;long expected=near;
  r.Write(near+0x38,BitConverter.GetBytes(73));
  Cast cast=delegate(IntPtr self,IntPtr chosen,IntPtr info){IntPtr target=IntPtr.Zero;Assert(BitConverter.ToInt32(r.Read(data,4),0)==2,"cast holds in-flight state");Assert(self.ToInt64()==fc&&(chosen.ToInt64()==buff&&target==IntPtr.Zero),"skill-bar callback receives screen/selected skill and current UI monster target");Put(r,player+0x168,name);calls++;};
  Attack attack=delegate(IntPtr self,IntPtr info){Assert(self.ToInt64()==fc&&r.Ptr(fc+0x2b0)==expected&&r.Ptr(fc+0x2e8)==skill,"attack UI handler must receive both live target and selected skill");Assert(BitConverter.ToInt32(r.Read(data,4),0)==2,"attack holds in-flight state");Put(r,player+0x168,name);calls++;};
  Put(r,vectorClass+0x1e8,Fn(count));Put(r,vectorClass+0x258,Fn(element));
  bool overlayVisible=true;Contains contains=delegate(IntPtr self,IntPtr item,IntPtr info){return overlayVisible;};Put(r,vectorClass+0x208,Fn(contains));Put(r,fc+0x88,mobs);
  r.Write(template+0x2c,new byte[]{0});
  Barrier barrier=delegate(IntPtr field,IntPtr value){Assert((field.ToInt64()==fc+0x2b0||field.ToInt64()==fc+0x2e8)&&r.Ptr(field.ToInt64())==value.ToInt64(),"UI target reference uses GC barrier");};
  byte[] code=NativeCode.Train(data,canvasSlot,fcSlot,charSlot,mobSlot,Fn(getter),Fn(distance),Fn(cast),Fn(barrier),Fn(attack));long executable=r.Allocate(4096);r.Write(executable,code);r.Protect(executable,4096,0x20);Tick tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(executable),typeof(Tick));
  r.Write(data,BitConverter.GetBytes(1));tick();Assert(BitConverter.ToInt32(r.Read(data+8,4),0)==2&&BitConverter.ToInt32(r.Read(data+128,4),0)==901&&calls==0,"survey IDs without attacking");
  r.Write(data+12,BitConverter.GetBytes(1));r.Write(data+16,BitConverter.GetBytes(901));for(int i=0;i<3;i++){Put(r,player+0x168,0);r.Write(data,BitConverter.GetBytes(1));tick();}Assert(calls==3,"repeat requests without an external cooldown wait");tick();Assert(calls==3,"request consumed once");r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==3,"active animation must finish before next cast");Put(r,player+0x168,0);
  r.Write(data+16,BitConverter.GetBytes(902));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==3,"reject support skill");
  r.Write(data+16,BitConverter.GetBytes(901));r.Write(near+0x78,BitConverter.GetBytes(0));r.Write(far+0x78,BitConverter.GetBytes(0));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==3,"no live mob means no attack");
  Put(r,fc+0x90,name);r.Write(near+0x78,BitConverter.GetBytes(200));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==4,"visible popup cannot block direct server packet");
  r.Write(player+0xac,BitConverter.GetBytes(0));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==4,"pause dead player");GC.KeepAlive(contains);
  r.Write(player+0xac,BitConverter.GetBytes(100));Put(r,player+0x168,0);r.Write(buffTemplate+0x2c,new byte[]{1});r.Write(data+16,BitConverter.GetBytes(902));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==5,"support skill accepted and correct type retained across later records");
  Put(r,player+0x168,0);r.Write(near+0x78,BitConverter.GetBytes(0));r.Write(far+0x78,BitConverter.GetBytes(0));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==6,"support casts without living monsters");
  Put(r,player+0x168,0);Put(r,fc+0x160,0);r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==7,"support casts without a monster list");
  // Synthetic multi-mob quest: nearest mob is wrong, farther mob matches.
  Put(r,fc+0x150,mobs);Put(r,fc+0x160,0);Put(r,player+0x168,0);r.Write(data+16,BitConverter.GetBytes(901));
  r.Write(near+0x78,BitConverter.GetBytes(200));r.Write(far+0x78,BitConverter.GetBytes(200));
  r.Write(near+0x50,BitConverter.GetBytes((short)261));r.Write(far+0x50,BitConverter.GetBytes((short)180));
  long task=r.Allocate(128);Put(r,fc+0x318,task);r.Write(task+20,BitConverter.GetBytes(1));r.Write(task+24,BitConverter.GetBytes(23));r.Write(task+36,BitConverter.GetBytes(180));r.Write(task+40,BitConverter.GetBytes(74));r.Write(task+52,BitConverter.GetBytes(40));r.Write(fc+0x134,BitConverter.GetBytes((short)74));r.Write(data+80,BitConverter.GetBytes(1));
  expected=far;r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8&&r.Ptr(fc+0x2b0)==far,"quest attacks matching map mob even when builtin idle mob list is empty");
  Put(r,player+0x168,0);r.Write(far+0x78,BitConverter.GetBytes(0));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8,"missing/dead quest mob never falls back to wrong live mob");
  r.Write(far+0x78,BitConverter.GetBytes(200));r.Write(fc+0x134,BitConverter.GetBytes((short)88));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8,"live map change blocks attack before worker catches up");
  r.Write(fc+0x134,BitConverter.GetBytes((short)74));r.Write(task+24,BitConverter.GetBytes(40));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8,"completion stops native combat immediately");
  r.Write(task+24,BitConverter.GetBytes(0));r.Write(task+36,BitConverter.GetBytes(179));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8,"new quest ID invalidates previous target immediately");
  Put(r,fc+0x318,0);r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==8,"absent task blocks attack");
  r.Write(data+80,BitConverter.GetBytes(0));Put(r,fc+0x160,mobs);expected=near;r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==9,"ordinary training still chooses nearest living mob");
  // Focus mode waits for confirmed builtin stop, then strictly filters ID261.
  Assert(code.Length<4096,"train callback allocation size");
  r.Write(player+0xa8,BitConverter.GetBytes(100));Put(r,player+0x168,0);
  r.Write(near+0x50,BitConverter.GetBytes((short)180));r.Write(far+0x50,BitConverter.GetBytes((short)261));
  Put(r,data+112,far);r.Write(data+120,BitConverter.GetBytes(74));r.Write(data+124,BitConverter.GetBytes(2));
  Put(r,fc+0x2b0,near);r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==9&&r.Ptr(fc+0x2b0)==near,"waiting for builtin stop cannot attack or retarget");
  r.Write(data+124,BitConverter.GetBytes(1));expected=far;r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10&&r.Ptr(fc+0x2b0)==far,"focus targets farther Thi Quy by ID instead of nearest ordinary mob");
  r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10,"focus leaves active attack hit frames intact");Put(r,player+0x168,0);
  r.Write(data+120,BitConverter.GetBytes(75));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10,"changed map blocks stale focus");r.Write(data+120,BitConverter.GetBytes(74));
  Put(r,far,npcClass);r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10,"wrong class never falls back to ordinary mob");Put(r,far,mobClass);
  r.Write(far+0x78,BitConverter.GetBytes(0));r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10,"dead Thi Quy never falls back to ordinary mob while focus active");r.Write(far+0x78,BitConverter.GetBytes(200));
  r.Write(far+0x3d,new byte[]{4});r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==10,"disappearing Thi Quy ignored");r.Write(far+0x3d,new byte[]{0});
  Put(r,data+112,1);r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==11&&r.Ptr(fc+0x2b0)==far,"stale sampled pointer not dereferenced; live vector ID filter still finds Thi Quy");Put(r,player+0x168,0);Put(r,data+112,far);
  r.Write(data+124,BitConverter.GetBytes(0));expected=near;r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==12&&r.Ptr(fc+0x2b0)==near,"finished focus restores ordinary target selection");
  // Worker eligibility protects quest/instances/navigation even with a saved preference.
  NativeJob job=new NativeJob("synthetic.exe","test","unused",null,true);job.State="opened";job.PrioritizeThiQuy=true;job.TrainReady=true;job.GameSettingsReady=true;job.TrainEnabled=true;
  Assert(NativeBackend.ThiQuyPriorityAllowed(job),"normal training permits priority");
  foreach(string field in new string[]{"QuestEnabled","QuestPauseAuto","DailyActive","IzanamiEnabled","SonCapEnabled","DungeonHighEnabled","ReceiveFlowActive","ReceiveKeyRequested","ReplayRequested","CloseRequested","CancelRequested","ReconnectRequested","CurseActive","ZoneSyncActive","FollowActive","MailBusy"}){
   var f=typeof(NativeJob).GetField(field,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);f.SetValue(job,true);Assert(!NativeBackend.ThiQuyPriorityAllowed(job),"protected activity "+field);f.SetValue(job,false);
  }
  foreach(string state in new[]{"queued","starting","waiting_map","waiting_popup","confirming_popup","closed","failed"}){job.State=state;Assert(!NativeBackend.ThiQuyPriorityAllowed(job),"startup/closed focus guard "+state);}job.State="opened";
  job.FarmEnabled=true;Assert(NativeBackend.ThiQuyPriorityAllowed(job),"walking to training map can stop for Thi Quy");job.FarmArrived=true;Assert(NativeBackend.ThiQuyPriorityAllowed(job),"arrived farming permits priority");
  job.PrioritizeThiQuy=false;Assert(!NativeBackend.ThiQuyPriorityAllowed(job),"disabled preference inactive");
  Assert(NativeBackend.IsThiQuyName("Thi Quỷ")&&NativeBackend.IsThiQuyName("Thi Quỷ".Normalize(NormalizationForm.FormD))&&!NativeBackend.IsThiQuyName("Thi Quỷ khác"),"exact normalized monster name");
  // A quest transition wins immediately, even before the worker disarms priority.
  Put(r,fc+0x318,task);r.Write(task+36,BitConverter.GetBytes(180));r.Write(task+24,BitConverter.GetBytes(0));r.Write(data+80,BitConverter.GetBytes(1));r.Write(data+124,BitConverter.GetBytes(1));Put(r,player+0x168,0);Put(r,fc+0x2b0,near);
  r.Write(data,BitConverter.GetBytes(1));tick();Assert(calls==13&&r.Ptr(fc+0x2b0)==near,"quest target wins over Thi Quy preference");
  // Worker samples a normal managed vector and verifies the live catalogue name.
  long game=r.Allocate(0x1700000),fcStatics=r.Allocate(32),catalog=r.Allocate(32+297*8),entry=r.Allocate(96),label=r.Allocate(96),worldVector=r.Allocate(32),list=r.Allocate(32),array=r.Allocate(48);
  Put(r,game+0x16ee888,fcClass);Put(r,fcClass+0xb8,fcStatics);Put(r,fcStatics+16,fc);Put(r,game+0x16ed658,canvasClass);Put(r,game+0x16ee330,charClass);Put(r,game+0x16ee310,mobClass);Put(r,player,charClass);
  Put(r,canvas+0xd8,catalog);r.Write(catalog+24,BitConverter.GetBytes(297));Put(r,catalog+32+261*8,entry);Put(r,entry+48,label);r.Write(label+16,BitConverter.GetBytes(7));r.Write(label+20,Encoding.Unicode.GetBytes("Thi Quỷ"));
  Put(r,worldVector+16,list);Put(r,list+16,array);r.Write(list+24,BitConverter.GetBytes(2));r.Write(array+24,BitConverter.GetBytes(2));Put(r,array+32,near);Put(r,array+40,far);Put(r,fc+0x150,worldVector);
  int map;Assert(NativeBackend.ReadThiQuyTarget(r,game,out map)==far&&map==74,"worker finds ID261 with exact live catalogue name");
  r.Write(near+0x50,BitConverter.GetBytes((short)261));r.Write(far+0x58,BitConverter.GetBytes((short)100));Put(r,fc+0x2b0,0);Assert(NativeBackend.ReadThiQuyTarget(r,game,out map)==near,"nearest Thi Quy chosen among multiple");
  Put(r,fc+0x2b0,far);Assert(NativeBackend.ReadThiQuyTarget(r,game,out map)==far,"existing live Thi Quy focus retained");
  r.Write(near+0x78,BitConverter.GetBytes(0));r.Write(far+0x78,BitConverter.GetBytes(0));Assert(NativeBackend.ReadThiQuyTarget(r,game,out map)==0,"no living Thi Quy returns ordinary fallback");
  r.Write(far+0x78,BitConverter.GetBytes(200));r.Write(label+20,Encoding.Unicode.GetBytes("Sai tên"));Assert(NativeBackend.ReadThiQuyTarget(r,game,out map)==0,"different catalogue name blocks priority");
  // Worker phases preserve user Auto intent, wait for stop acknowledgement,
  // rotate the selected skills without mutating normal training and restore Auto.
  r.Write(label+20,Encoding.Unicode.GetBytes("Thi Quỷ"));r.Write(data,new byte[4]);r.Write(data+80,new byte[4]);Put(r,player+0x168,0);
  NativeJob focus=new NativeJob("synthetic.exe","focus","unused",null,true);focus.State="opened";focus.FarmEnabled=true;focus.FarmArrived=true;focus.PrioritizeThiQuy=true;focus.GameSettingsReady=true;focus.TrainReady=true;focus.GameAutoWanted=true;focus.GameAutoEnabled=true;focus.Skills=new[]{new SkillInfo{Id=901,Type=0,Level=1},new SkillInfo{Id=902,Type=1,Level=2}};focus.HangingSkillIds=new[]{901,902};
  NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(focus.ThiQuyCombatActive&&!focus.ThiQuyPriorityArmed&&focus.GameAutoWanted&&!NativeBackend.GameAutoDesired(focus)&&BitConverter.ToInt32(r.Read(data+124,4),0)==2,"detection requests temporary stop without clearing user Auto");
  var pump=typeof(NativeBackend).GetMethod("PumpTraining",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  pump.Invoke(null,new object[]{r,data,focus});Assert(BitConverter.ToInt32(r.Read(data+12,4),0)==0,"no focus cast before builtin stop confirmed");
  r.Write(data,new byte[4]);focus.GameAutoEnabled=false;NativeBackend.PumpThiQuyPriority(r,game,data,focus);pump.Invoke(null,new object[]{r,data,focus});Assert(focus.ThiQuyCombatActive&&focus.ThiQuyPriorityArmed&&BitConverter.ToInt32(r.Read(data+12,4),0)==1&&BitConverter.ToInt32(r.Read(data+80,4),0)==0&&!focus.TrainEnabled,"confirmed stop arms dedicated focus without changing normal training or quest");
  Assert(NativeBackend.TrainingPollMs(focus)==150&&NativeBackend.HangingLevelOne(focus)==null,"focus uses quest cadence without builtin supplements");
  r.Write(far+0x78,BitConverter.GetBytes(0));focus.ThiQuyPriorityAt=DateTime.MinValue;NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(!focus.ThiQuyCombatActive&&!focus.ThiQuyPriorityArmed&&focus.GameAutoWanted&&NativeBackend.GameAutoDesired(focus)&&BitConverter.ToInt32(r.Read(data+12,4),0)==0,"Thi Quy death clears focus and restores requested hanging");
  r.Write(far+0x78,BitConverter.GetBytes(200));focus.ThiQuyPriorityAt=DateTime.MinValue;NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(focus.ThiQuyCombatActive,"next Thi Quy starts another focus");focus.FarmEnabled=false;focus.FarmArrived=false;focus.GameAutoWanted=false;NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(!focus.ThiQuyCombatActive&&!NativeBackend.GameAutoDesired(focus),"manual Auto stop does not get reversed by focus cleanup");
  // Up sees Thi Quy on the way to its destination, pauses the owned route,
  // then restarts the same route without dropping Up or Auto selections.
  long farmData=r.Allocate(4096);focus.FarmEnabled=true;focus.FarmArrived=false;focus.FarmRouteSent=true;focus.GameAutoWanted=true;focus.ThiQuyPriorityAt=DateTime.MinValue;
  NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(focus.ThiQuyCombatActive,"detect Thi Quy before reaching farming spot");
  r.Write(data,new byte[4]);pump.Invoke(null,new object[]{r,data,focus});Assert(BitConverter.ToInt32(r.Read(data+12,4),0)==0,"wait until owned Up route is paused before attacking");
  Assert(NativeBackend.HandleThiQuyFarmPause(r,farmData,focus)&&BitConverter.ToInt32(r.Read(farmData,4),0)==6&&focus.FarmEnabled&&focus.GameAutoWanted,"queue owned-route cancel without disabling Up");
  NativeBackend.HandleThiQuyFarmPause(r,farmData,focus);Assert(BitConverter.ToInt32(r.Read(farmData,4),0)==6,"no repeated route command while pending");
  r.Write(farmData,new byte[4]);r.Write(data,new byte[4]);pump.Invoke(null,new object[]{r,data,focus});Assert(BitConverter.ToInt32(r.Read(data+12,4),0)==1,"focused attack allowed after Up route pause");
  r.Write(far+0x78,BitConverter.GetBytes(0));focus.ThiQuyPriorityAt=DateTime.MinValue;NativeBackend.PumpThiQuyPriority(r,game,data,focus);Assert(!NativeBackend.HandleThiQuyFarmPause(r,farmData,focus)&&focus.FarmEnabled&&!focus.FarmRouteSent&&focus.GameAutoWanted&&!focus.ThiQuyFarmPaused,"Thi Quy death resumes original Up route and retains Auto");
  Console.WriteLine("PASS: Thi Quy temporary hanging pause, confirmed-off gate, strict ID/map filter, no wrong-mob fallback, attack completion, selected skill combat, automatic restoration and manual-stop protection.");
  GC.KeepAlive(attack);GC.KeepAlive(barrier);GC.KeepAlive(count);GC.KeepAlive(element);GC.KeepAlive(getter);GC.KeepAlive(distance);GC.KeepAlive(cast);Console.WriteLine("PASS: skill ID survey, nearest live monster, repeat requests, complete animation, reject unusable skills/dead player, popup-independent attacks and continuous support without any monsters. Quest target/map/completion guards passed. Code bytes: "+code.Length);
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
