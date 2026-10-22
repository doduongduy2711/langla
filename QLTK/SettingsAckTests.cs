using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Text;using TGLL;
class SettingsAckTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Getter(IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Toggle(IntPtr self,int value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Event(IntPtr self,int code,IntPtr value,IntPtr control,IntPtr info);
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static long Fn(Delegate d){return Marshal.GetFunctionPointerForDelegate(d).ToInt64();}
 static void Put(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}
 static void Int(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static int Read(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
 static long Arr(Remote r,params long[] items){long p=r.Allocate(32+8*items.Length);Int(r,p+24,items.Length);for(int i=0;i<items.Length;i++)Put(r,p+32+i*8,items[i]);return p;}
 static long Str(Remote r,string s){long p=r.Allocate(24+2*s.Length);Int(r,p+16,s.Length);r.Write(p+20,Encoding.Unicode.GetBytes(s));return p;}
 static int Main(){try{using(Remote r=new Remote(Process.GetCurrentProcess())){
 long data=r.Allocate(4096),canvasSlot=r.Allocate(8),fcSlot=r.Allocate(8),cls=r.Allocate(256),stat=r.Allocate(32),canvas=r.Allocate(512),fc=r.Allocate(256),root=r.Allocate(512),panel=r.Allocate(256),gem=r.Allocate(256),drop=r.Allocate(256),model=r.Allocate(256);
 Put(r,canvasSlot,cls);Put(r,cls+0xb8,stat);Put(r,stat,canvas);Put(r,fcSlot,fc);Put(r,fc,fc);Put(r,root+0xe0,panel);Put(r,gem+0x48,panel);Put(r,gem+0x78,Str(r,"Tự ghép đá (trên 50%)"));Int(r,gem+0x80,309);Put(r,root+0xa8,Arr(r,Arr(r,gem),Arr(r),Arr(r),Arr(r)));
 Put(r,drop+0x48,root);Put(r,drop+0x88,model);Int(r,model+16,400);Put(r,model+24,Arr(r,Str(r,"Không giới hạn"),Str(r,"Gần")));Put(r,root+0xb0,Arr(r,drop));
 long rootSlot=r.Allocate(8),rootClass=r.Allocate(256),rootStatic=r.Allocate(32);Put(r,rootSlot,rootClass);Put(r,rootClass+0xb8,rootStatic);Put(r,rootStatic+8,root);bool ignoreToggle=false;int toggles=0,events=0;Tick tick=null;
 Getter getter=delegate{return new IntPtr(root);};Toggle toggle=delegate(IntPtr self,int v,IntPtr info){Check(self.ToInt64()==root&&Read(r,data)==3,"toggle ownership/busy");toggles++;tick();Check(Read(r,data)==3,"reentry protected");if(!ignoreToggle)r.Write(root+0x188,new[]{(byte)v});};
 Event callback=delegate(IntPtr self,int c,IntPtr value,IntPtr control,IntPtr info){Check(self.ToInt64()==root&&Read(r,data)==3,"original callback/busy");events++;if(c==309)Check(control.ToInt64()==gem&&value==IntPtr.Zero,"checkbox dispatch");if(c==400)Check(value.ToInt64()==model&&Read(r,model+20)==1,"dropdown dispatch");};
 long code=r.Allocate(4096);byte[] bytes=NativeCode.Settings(data,canvasSlot,fcSlot,Fn(getter),Fn(toggle),Fn(callback),0,rootSlot);r.Write(code,bytes);r.Protect(code,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(Tick));
 Int(r,data,1);Int(r,data+32,1);tick();Check(toggles==0,"wait for map");Put(r,canvas+0x1b8,fc);tick();Check(toggles==1&&Read(r,data)==0,"treo on");Int(r,data,1);Int(r,data+32,0);tick();Check(toggles==2&&Read(r,data+16)==0,"treo off");
 ignoreToggle=true;Int(r,data,1);Int(r,data+32,1);tick();Check(Read(r,data+48)==-2&&Read(r,data+16)==0&&Read(r,data)==0,"silently ignored auto request falsely acknowledged");ignoreToggle=false;
 NativeJob job=new NativeJob("fixture","synthetic","",null,true);NativeBackend.ReadGameSettings(r,data,job);GameOption option=Array.Find(job.GameOptions,delegate(GameOption o){return o.Code==309;});Check(option!=null&&option.Kind==0&&option.Tab==0&&option.Label.StartsWith("Tự ghép đá"),"owned-panel gem checkbox exposed");Check(Array.Find(job.GameOptions,delegate(GameOption o){return o.Code==400;}).Label=="Phạm vi đánh","dropdown label preserved");
 Put(r,data+40,gem);Int(r,data+24,309);Int(r,data+28,0);Int(r,data+32,1);Int(r,data,2);tick();Check(events==1&&r.Read(gem+0x84,1)[0]==1,"gem on");Int(r,data+32,0);Int(r,data,2);tick();Check(events==2&&r.Read(gem+0x84,1)[0]==0,"gem off");Put(r,gem+0x48,r.Allocate(256));Int(r,data,2);tick();Check(events==2&&Read(r,data+48)==-1,"unrelated panel rejected");
 Put(r,data+40,drop);Int(r,data+24,400);Int(r,data+28,1);Int(r,data+32,1);Int(r,data,2);tick();Check(events==3,"dropdown applies");Put(r,drop+0x48,0);Int(r,data,2);tick();Check(events==3,"detached dropdown rejected");// Reproduce six separate game panels after travel: an old cached enabled panel
 // must not hide the fact that the current panel has treo disabled.
 for(int i=0;i<6;i++){long previous=root;root=r.Allocate(512);Put(r,rootStatic+8,root);Int(r,data,1);Int(r,data+32,1);tick();Check(r.Ptr(data+8)==root&&Read(r,data+16)==1&&r.Read(root+0x188,1)[0]==1,"current singleton receives Auto for account "+i);Check(r.Read(previous+0x188,1)[0]==(i==0?0:1),"obsolete singleton untouched");}
 Check(toggles==9,"six replacement game panels toggled");
 GC.KeepAlive(getter);GC.KeepAlive(toggle);GC.KeepAlive(callback);
 }Console.WriteLine("PASS: real owned-panel checkbox snapshot, gem on/off callbacks, dropdown labels, auto on/off, map/reentry gates and detached-control rejection.");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
