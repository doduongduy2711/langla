namespace TGLL {
 public static class SettingsHookTests {
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Hook();
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr Getter(IntPtr method);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Toggle(IntPtr root,int enabled,IntPtr method);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Callback(IntPtr root,int code,IntPtr model,IntPtr control,IntPtr method);
  [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protect);
  [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr address,UIntPtr size,uint kind);
  [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
  sealed class Fixture : IDisposable {
   readonly List<IntPtr> memory=new List<IntPtr>();readonly Getter getter;readonly Toggle toggle;readonly Callback callback;
   readonly IntPtr executable,rootStatics;readonly Hook hook;
   internal IntPtr Data,Root,LastRoot,LastControl;internal int Calls,LastCode;
   internal IntPtr Allocate(int size=768){IntPtr p=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,p,size);memory.Add(p);return p;}
   internal void P(IntPtr p,int offset,IntPtr value){Marshal.WriteInt64(p,offset,value.ToInt64());}
   internal void I(IntPtr p,int offset,int value){Marshal.WriteInt32(p,offset,value);}
   internal IntPtr ArrayOf(params IntPtr[] items){IntPtr p=Allocate(32+8*items.Length);P(p,24,new IntPtr(items.Length));for(int i=0;i<items.Length;i++)P(p,32+8*i,items[i]);return p;}
   internal void NewRoot(){Root=Allocate();P(rootStatics,8,Root);}
   internal IntPtr Check(int code,int tab=0,bool extra=false){
    IntPtr control=Allocate();P(control,0x48,Root);I(control,0x80,code);
    if(extra)P(Root,0x100,control);else{IntPtr[] tabs=new IntPtr[tab+1];tabs[tab]=ArrayOf(control);P(Root,0xa8,ArrayOf(tabs));}return control;
   }
   internal IntPtr Drop(int code,int group,int choices){
    IntPtr control=Allocate(),model=Allocate();P(control,0x48,Root);P(control,0x88,model);I(model,16,code);P(model,24,ArrayOf(new IntPtr[choices]));P(Root,0xb0+8*group,ArrayOf(control));return control;
   }
   internal void Request(int code,int kind,int value,IntPtr poison){I(Data,0,2);I(Data,24,code);I(Data,28,kind);I(Data,32,value);P(Data,40,poison);hook();}
   internal void ToggleAuto(){I(Data,0,1);I(Data,32,1);hook();}
   internal void Execute(){hook();}
   internal int Status {get{return Marshal.ReadInt32(Data,48);}}
   internal Fixture(){
    Data=Allocate();IntPtr canvas=Allocate(),world=Allocate(),canvasClass=Allocate(),canvasStatics=Allocate(),worldClass=Allocate(),rootClass=Allocate();rootStatics=Allocate();
    P(canvasClass,0xb8,canvasStatics);P(canvasStatics,0,canvas);P(canvas,0x1b8,world);P(world,0,worldClass);P(rootClass,0xb8,rootStatics);
    IntPtr canvasSlot=Allocate(8),worldSlot=Allocate(8),rootSlot=Allocate(8);P(canvasSlot,0,canvasClass);P(worldSlot,0,worldClass);P(rootSlot,0,rootClass);NewRoot();
    getter=delegate(IntPtr method){return Root;};toggle=delegate(IntPtr root,int enabled,IntPtr method){Marshal.WriteByte(root,0x188,(byte)enabled);};
    callback=delegate(IntPtr root,int code,IntPtr model,IntPtr control,IntPtr method){Calls++;LastRoot=root;LastControl=control;LastCode=code;};
    byte[] bytes=NativeCode.Settings(Data.ToInt64(),canvasSlot.ToInt64(),worldSlot.ToInt64(),Marshal.GetFunctionPointerForDelegate(getter).ToInt64(),Marshal.GetFunctionPointerForDelegate(toggle).ToInt64(),Marshal.GetFunctionPointerForDelegate(callback).ToInt64(),0,rootSlot.ToInt64());
    Assert(bytes.Length<=4096,"Settings hook exceeds allocated code page");executable=VirtualAlloc(IntPtr.Zero,(UIntPtr)4096,0x3000,0x40);Assert(executable!=IntPtr.Zero,"Executable allocation failed");Marshal.Copy(bytes,0,executable,bytes.Length);FlushInstructionCache(GetCurrentProcess(),executable,(UIntPtr)bytes.Length);hook=(Hook)Marshal.GetDelegateForFunctionPointer(executable,typeof(Hook));
   }
   public void Dispose(){VirtualFree(executable,UIntPtr.Zero,0x8000);foreach(IntPtr p in memory)Marshal.FreeHGlobal(p);GC.KeepAlive(getter);GC.KeepAlive(toggle);GC.KeepAlive(callback);}
  }
  public static string Run(){
   Assert(IntPtr.Size==8,"Tests require x64");
   using(var f=new Fixture()){
    IntPtr old=f.Check(307);f.Request(307,0,1,new IntPtr(1));Assert(f.Status==1&&f.LastControl==old&&Marshal.ReadByte(old,0x84)==1,"Poisoned cached handle was used");
    f.NewRoot();IntPtr fresh=f.Check(307,3);GC.Collect();GC.WaitForPendingFinalizers();f.Request(307,0,1,old);
    Assert(f.Status==1&&f.LastControl==fresh&&f.LastRoot==f.Root,"Old root/control used after map change");Assert(Marshal.ReadByte(old,0x84)==1,"Old object changed");
    IntPtr unmapped=VirtualAlloc(IntPtr.Zero,(UIntPtr)4096,0x3000,4);Assert(unmapped!=IntPtr.Zero,"Test allocation failed");Assert(VirtualFree(unmapped,UIntPtr.Zero,0x8000),"Test object release failed");
    f.Request(307,0,1,unmapped);Assert(f.Status==1&&f.LastControl==fresh,"Released legacy handle was used");
    var job=new NativeJob("test","test",null,null,false);job.GameSettingsReady=true;job.GameOptions=new[]{new GameOption{Code=307,Kind=0,Handle=1}};job.SettingsReadAt=DateTime.UtcNow;
    var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);jobs[job.Id]=job;
    try{
     NativeBackend.SetGameOption(job.Id,307,0);GameOption queued;Assert(job.SettingsQueue.TryPeek(out queued)&&queued.Handle==0,"Queue retained client pointer");
     f.P(f.Data,40,new IntPtr(1));
     using(var remote=new Remote(Process.GetCurrentProcess()))typeof(NativeBackend).GetMethod("PumpGameSettings",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{remote,f.Data.ToInt64(),job});
     Assert(Marshal.ReadInt64(f.Data,40)==0&&Marshal.ReadInt32(f.Data,0)==2,"Published request retained pointer");f.Execute();
     Assert(f.Status==1&&f.LastControl==fresh&&Marshal.ReadByte(fresh,0x84)==0,"Public option queue to native hook round trip failed");
    }finally{NativeJob ignored;jobs.TryRemove(job.Id,out ignored);}
    int calls=f.Calls;f.NewRoot();f.Request(307,0,1,new IntPtr(1));Assert(f.Status==-1&&f.Calls==calls,"Missing control was applied");
    IntPtr extra=f.Check(501,0,true);f.Request(501,0,1,new IntPtr(1));Assert(f.Status==1&&f.LastControl==extra,"Extra checkbox failed");
    f.Request(501,0,2,new IntPtr(1));Assert(f.Status==-1,"Invalid checkbox value accepted");
    for(int group=0;group<3;group++){
     f.NewRoot();IntPtr drop=f.Drop(210+group,group,3);f.Request(210+group,1,2,new IntPtr(1));Assert(f.Status==1&&f.LastControl==drop,"Dropdown group failed");
     f.Request(210+group,1,3,new IntPtr(1));Assert(f.Status==-1,"Out-of-range dropdown accepted");f.Request(210+group,1,-1,new IntPtr(1));Assert(f.Status==-1,"Negative dropdown accepted");
    }
    f.NewRoot();IntPtr keyboard=f.Allocate(),movement=f.Allocate();f.P(f.Root,0x140,keyboard);f.P(f.Root,0x148,movement);
    f.Request(1000,2,2,new IntPtr(1));Assert(f.Status==1&&f.LastControl==keyboard&&Marshal.ReadInt32(keyboard,20)==2,"Keyboard resolution failed");
    f.Request(1005,2,1,new IntPtr(1));Assert(f.Status==1&&f.LastControl==movement&&Marshal.ReadInt32(movement,20)==1,"Movement resolution failed");
    f.Request(1005,2,2,new IntPtr(1));Assert(f.Status==-1,"Invalid movement mode accepted");
    f.Request(5000,2,0,new IntPtr(1));Assert(f.Status==-1,"Unknown model accepted");
    f.Request(0,9,1,new IntPtr(1));Assert(f.Status==-1,"Unknown control kind accepted");
    f.ToggleAuto();Assert(f.Status==1&&Marshal.ReadByte(f.Root,0x188)==1,"Auto toggle regressed");
    f.NewRoot();IntPtr malformed=f.ArrayOf();f.P(malformed,24,new IntPtr(129));f.P(f.Root,0xb0,malformed);f.Request(210,1,0,new IntPtr(1));Assert(f.Status==-1,"Unbounded array traversal");
   }
   System.Threading.Tasks.Parallel.For(0,32,delegate(int i){using(var f=new Fixture()){IntPtr control=f.Check(307,i%4);f.Request(307,0,1,new IntPtr(1));Assert(f.LastRoot==f.Root&&f.LastControl==control&&f.Status==1,"Cross-client pointer contamination");}});
   var waiting=new NativeJob("test","test",null,null,false);waiting.SequenceManaged=true;waiting.IzanamiEnabled=true;
   Assert(NativeBackend.WatchedActivity(waiting)=="","v330 waiting stage guard lost");waiting.DailyExecuting=true;
   Assert(NativeBackend.WatchedActivity(waiting)=="izanami","Active stage watchdog disabled");
   return "PASS: executed real x64 Settings hook; poisoned/stale handles ignored; replacement roots; all tabs/dropdown groups/models; invalid/missing values rejected; auto toggle; bounded arrays; 32 parallel isolated clients.";
  }
 }
}
