using System;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class HeadlessCloakCatalogTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Request(int category,IntPtr info);
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}static void P(Remote r,long a,long v){r.Write(a,BitConverter.GetBytes(v));}static int R(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static Tick Exe(Remote r,byte[] bytes){long p=r.Allocate(4096);r.Write(p,bytes);r.Protect(p,4096,0x20);return (Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(p),typeof(Tick));}
 static int Main(){try{using(var r=new Remote(Process.GetCurrentProcess())){
 long d=r.Allocate(4096),world=r.Allocate(1024),cls=r.Allocate(512),stat=r.Allocate(32),slot=r.Allocate(8);P(r,slot,cls);P(r,cls+0xb8,stat);P(r,stat+16,world);int requests=0,normal=0,hidden=0;
 Request request=delegate(int category,IntPtr info){Check(category==18,"exact cloak category");Check(R(r,d+80)==2&&R(r,d+96)==1,"consume and arm before send");requests++;};
 Tick req=Exe(r,NativeCode.CloakCatalogRequest(d,slot,0,Marshal.GetFunctionPointerForDelegate(request).ToInt64()));I(r,d+12,1);I(r,d+80,1);req();req();Check(requests==1&&R(r,d+80)==0&&r.Ptr(d+104)==world,"one request and bound world");
 I(r,d+12,0);I(r,d+80,1);req();Check(requests==1&&R(r,d+80)==0,"readiness blocks request");
 Tick normalFn=delegate{normal++;};Tick hiddenFn=delegate{hidden++;};long titleSlot=r.Allocate(8);P(r,titleSlot,123);
 Tick reply=Exe(r,NativeCode.CloakCatalogReply(d,Marshal.GetFunctionPointerForDelegate(normalFn).ToInt64(),Marshal.GetFunctionPointerForDelegate(hiddenFn).ToInt64()));
 long inputs=r.Allocate(32);P(r,inputs,world);I(r,inputs+8,18);Code64 a=new Code64();a.Emit(0x53,0x57,0x48,0x81,0xec,0xa8,1,0,0,0x48,0xb8);a.Q(inputs);a.Emit(0x48,0x8b,0x18,0x8b,0x40,8,0x88,0x84,0x24,0x80,1,0,0);a.Call(Marshal.GetFunctionPointerForDelegate(reply).ToInt64());a.Emit(0x48,0x81,0xc4,0xa8,1,0,0,0x5f,0x5b,0xc3);Tick wrapper=Exe(r,a.Finish());
 I(r,d+96,1);wrapper();Check(hidden==1&&normal==0&&R(r,d+96)==2,"pending cloak data parsed without UI tail");wrapper();Check(hidden==1&&normal==1,"manual reply after completion normal");
 I(r,d+96,1);I(r,inputs+8,7);wrapper();Check(normal==2&&R(r,d+96)==1,"other shop untouched");I(r,inputs+8,18);P(r,inputs,world+8);wrapper();Check(normal==3&&R(r,d+96)==1,"different world untouched");
 GC.KeepAlive(request);GC.KeepAlive(normalFn);GC.KeepAlive(hiddenFn);GC.KeepAlive(reply);Console.WriteLine("PASS: category18 catalog request, once-only/readiness, response bypasses UI only for pending cloak/world; manual and other shops preserved.");
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
