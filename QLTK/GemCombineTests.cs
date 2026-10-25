using System;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class GemCombineTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Alloc(IntPtr cls);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Ctor(IntPtr self,byte code,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Write(IntPtr self,int value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Flush(IntPtr self,IntPtr info);
 static void Check(bool b,string text){if(!b)throw new Exception(text);}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}static void P(Remote r,long a,long v){r.Write(a,BitConverter.GetBytes(v));}
 static int Read(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static long Static(Remote r,long instance,int off){long slot=r.Allocate(8),cls=r.Allocate(512),stat=r.Allocate(64);P(r,slot,cls);P(r,cls+0xb8,stat);P(r,stat+off,instance);return slot;}
 static InventorySnapshot Bag(int n){return new InventorySnapshot(Enumerable.Range(0,n).Select(i=>new InventoryItem{TemplateId=9,Quantity=1,Slot=i,BagIndex=i}).ToArray(),100,true,"",DateTime.UtcNow);}
 static int Main(){try{
  Check(NativeBackend.PlanGemCombine(Bag(15),262144,4194304).Length==15,"15 stones must wait");Check(NativeBackend.PlanGemCombine(Bag(20),262144,4194304).Length==16,"exact 16 stones");
  var mixed=Bag(16);mixed.Items[3].TemplateId=10;Check(NativeBackend.PlanGemCombine(mixed,262144,4194304).Length==15,"exclude level11");mixed.Items[4].Quantity=2;Check(NativeBackend.PlanGemCombine(mixed,262144,4194304).Length==14,"no unverified stacks");
  bool rejected=false;try{NativeBackend.PlanGemCombine(Bag(16),262144,8388608);}catch{rejected=true;}Check(rejected,"unverified rate rejected");
  var j=new NativeJob("fixture","synthetic","",null,true);j.GemBeforeSource=16;j.GemBeforeTarget=2;j.GemBeforeSilver=1000;j.GemFee=100;
  var after=new InventorySnapshot(new[]{new InventoryItem{TemplateId=11,Quantity=3}},100,true,"",DateTime.UtcNow);Check(NativeBackend.ConfirmGemCombine(j,after,900),"ack requires 16 consumed, one level12 gained, correct fee");Check(!NativeBackend.ConfirmGemCombine(j,after,1000),"do not ack without fee");after.Items[0].Quantity=2;Check(!NativeBackend.ConfirmGemCombine(j,after,900),"do not ack inventory loss alone");
  using(var r=new Remote(Process.GetCurrentProcess())){
   long d=r.Allocate(4096),player=r.Allocate(1024),bag=r.Allocate(512),canvas=r.Allocate(1024),root=r.Allocate(512),opts=r.Allocate(128),weights=r.Allocate(256),costs=r.Allocate(128),service=r.Allocate(32),serviceCls=r.Allocate(1024),serviceSlot=r.Allocate(8);
   P(r,player+0x128,bag);P(r,player+0xc0,1000);I(r,bag+24,16);P(r,root+0xe8,opts);I(r,opts+24,64);r.Write(root+0x188,new byte[]{1});P(r,canvas+0x1e8,weights);P(r,canvas+0x1c0,costs);I(r,weights+24,12);I(r,costs+24,12);P(r,weights+104,262144);P(r,weights+120,4194304);I(r,costs+76,100);
   long[] items=new long[16];for(int i=0;i<16;i++){items[i]=r.Allocate(128);P(r,bag+32+i*8,items[i]);I(r,items[i]+20,9);I(r,items[i]+24,1);I(r,items[i]+32,i);P(r,d+128+i*16,items[i]);I(r,d+136+i*16,i);}
   foreach(var f in new[]{new[]{24L,262144L},new[]{32L,4194304L},new[]{40L,100L},new[]{48L,player},new[]{56L,bag},new[]{64L,canvas},new[]{72L,weights},new[]{80L,costs}})P(r,d+f[0],f[1]);
   P(r,service,serviceCls);P(r,serviceSlot,serviceCls);int flushes=0;byte opcode=0;var values=new List<int>();Tick tick=null;
   Alloc alloc=delegate{return new IntPtr(service);};Ctor ctor=delegate(IntPtr self,byte op,IntPtr info){opcode=op;values.Clear();};Write write=delegate(IntPtr self,int value,IntPtr info){tick();Check(Read(r,d)==2,"reentrant dispatch held");values.Add(value);};Flush flush=delegate{flushes++;};
   foreach(int slot in new[]{0x218,0x228,0x248})P(r,serviceCls+slot,Marshal.GetFunctionPointerForDelegate(write).ToInt64());P(r,serviceCls+0x298,Marshal.GetFunctionPointerForDelegate(flush).ToInt64());
   byte[] code=NativeCode.GemCombineCore(d,Static(r,player,8),Static(r,root,8),Static(r,canvas,0),serviceSlot,Marshal.GetFunctionPointerForDelegate(alloc).ToInt64(),Marshal.GetFunctionPointerForDelegate(ctor).ToInt64(),0);Check(code.Length<4096,"hook size");long exe=r.Allocate(4096);r.Write(exe,code);r.Protect(exe,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));
   I(r,d+12,1);I(r,d,1);tick();tick();Check(flushes==1&&opcode==108&&Read(r,d+16)==2&&values.SequenceEqual(new[]{1,16}.Concat(Enumerable.Range(0,16))),"exact original stone packet, single flight");Check(r.Read(opts+39,1)[0]==255&&r.Read(root+0x188,1)[0]==1,"disable only >50%, preserve treo");
   I(r,items[5]+20,10);I(r,d,1);tick();Check(flushes==1&&Read(r,d+16)==1,"reject changed stone");I(r,items[5]+20,9);
   P(r,weights+120,8388608);I(r,d,1);tick();Check(flushes==1,"reject changed rate");P(r,weights+120,4194304);
   P(r,weights+120,2097152);P(r,d+32,2097152);I(r,d,1);tick();Check(flushes==1,"reject non-100 percent even if worker buffer matches table");P(r,weights+120,4194304);P(r,d+32,4194304);
   P(r,player+0xc0,99);I(r,d,1);tick();Check(flushes==1,"reject insufficient locked silver");P(r,player+0xc0,1000);
   I(r,items[2]+24,2);I(r,d,1);tick();Check(flushes==1,"reject changed quantity");I(r,items[2]+24,1);
   I(r,d+12,0);I(r,d,1);tick();Check(flushes==1&&Read(r,d)==0,"stop cancels unsent work");
   // Exercise the worker and native dispatcher together: gathering leaves the game
   // mode on; only the 16-stone transaction pauses it, until its acknowledgement.
   NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/gem-test-logs");
   var worker=new NativeJob("fixture","gem-worker","",null,true);worker.TrainReady=true;worker.GameSettingsReady=true;worker.GemEnabled=true;
   var current=Bag(15);var plan=new GemPlan{Player=player,Bag=bag,Canvas=canvas,Weights=weights,Costs=costs,SourceWeight=262144,TargetWeight=4194304,Fee=100,Silver=1000,Items=current.Items};
   DateTime now=DateTime.UtcNow;int restores=0;long observedSilver=1000;Action restore=delegate{Check(!worker.GemBusy&&Read(r,d+12)==0,"release native pause before enabling game mode");restores++;r.Write(opts+39,new byte[]{0});};
   r.Write(opts+39,new byte[]{0});NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now);tick();Check(Read(r,d)==0&&Read(r,d+12)==0&&r.Read(opts+39,1)[0]==0&&restores==0,"gathering does not disable >50%");
   current=Bag(16);plan.Items=current.Items;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(2));tick();Check(worker.GemBusy&&flushes==2&&r.Read(opts+39,1)[0]==255,"pause only with full inventory and enough silver");
   NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(4));Check(restores==0&&worker.GemBusy,"do not restore before server result");
   current=new InventorySnapshot(new[]{new InventoryItem{TemplateId=11,Quantity=1}},100,true,"",DateTime.UtcNow);observedSilver=900;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(6));tick();Check(restores==1&&worker.GemCompleted==1&&!worker.GemBusy&&worker.GemEnabled&&r.Read(opts+39,1)[0]==0,"confirmed stone12 restores game mode and continues watching bag");
   current=Bag(15);plan.Items=current.Items;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(8));tick();Check(restores==1&&flushes==2&&r.Read(opts+39,1)[0]==0,"next gathering cycle leaves mode on");
   current=Bag(16);plan.Items=current.Items;plan.Silver=99;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(10));tick();Check(!worker.GemEnabled&&worker.GemSkipped&&!worker.GemBusy&&flushes==2&&r.Read(opts+39,1)[0]==0,"insufficient locked silver skips and stops, keeps game mode on");
   plan.Silver=1000;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(12));tick();Check(flushes==2,"does not retry skipped transaction after silver changes");
   // Auto-item remains enabled. Drain the existing one-shot request, pause only
   // its dispatcher during crafting, and resume it after the confirmed result.
   long itemData=r.Allocate(4096);worker.GemEnabled=true;worker.GemSkipped=false;worker.AutoItemEnabled=true;worker.AutoItemIds=new[]{161};worker.AutoItemId=161;worker.AutoItemOnceIds=new int[0];worker.AutoItemNext=DateTime.UtcNow.AddHours(1);plan.Items=Bag(16).Items;current=Bag(16);plan.Silver=1000;observedSilver=1000;I(r,itemData,1);I(r,itemData+12,1);
   NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(14),itemData);NativeBackend.PumpAutoItem(r,itemData,worker);Check(worker.GemItemPause&&!worker.GemBusy&&Read(r,itemData)==1&&Read(r,itemData+12)==1&&Read(r,d)==0,"do not cancel existing once-per-session item request");
   I(r,itemData,0);NativeBackend.PumpAutoItem(r,itemData,worker);Check(Read(r,itemData+12)==0,"pause new item requests before crafting");
   NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(15),itemData);Check(!worker.GemBusy,"wait for item response to settle");
   NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(17),itemData);tick();NativeBackend.PumpAutoItem(r,itemData,worker);Check(worker.GemBusy&&flushes==3&&worker.AutoItemEnabled&&Read(r,itemData+12)==0,"auto item enabled does not block gem combine, sends paused during flight");
   current=new InventorySnapshot(new[]{new InventoryItem{TemplateId=11,Quantity=1}},100,true,"",DateTime.UtcNow);observedSilver=900;NativeBackend.PumpGemCombine(r,d,0,worker,()=>current,b=>plan,()=>observedSilver,restore,now.AddSeconds(19),itemData);NativeBackend.PumpAutoItem(r,itemData,worker);Check(!worker.GemBusy&&!worker.GemItemPause&&worker.AutoItemEnabled&&Read(r,itemData+12)==1&&restores==2,"resume item use and game >50% after confirmation");
   GC.KeepAlive(alloc);GC.KeepAlive(ctor);GC.KeepAlive(write);GC.KeepAlive(flush);
  }
  Console.WriteLine("PASS: stone10 x16 -> stone12 at100%, native packet, reentry, rate/slot/quantity/fee gates and acknowledgement.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
