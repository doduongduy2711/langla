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
  GC.KeepAlive(attack);GC.KeepAlive(barrier);GC.KeepAlive(count);GC.KeepAlive(element);GC.KeepAlive(getter);GC.KeepAlive(distance);GC.KeepAlive(cast);Console.WriteLine("PASS: skill ID survey, nearest live monster, repeat requests, complete animation, reject unusable skills/dead player, popup-independent attacks and continuous support without any monsters. Quest target/map/completion guards passed. Code bytes: "+code.Length);
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
