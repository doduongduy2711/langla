using System;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class BulkUse131Tests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Getter(IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Use(IntPtr item,IntPtr info);
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static void P(Remote r,long a,long n){r.Write(a,BitConverter.GetBytes(n));}static void I(Remote r,long a,int n){r.Write(a,BitConverter.GetBytes(n));}static int R(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static int Main(){try{using(var r=new Remote(Process.GetCurrentProcess())){
 long data=r.Allocate(4096),csSlot=r.Allocate(8),fcSlot=r.Allocate(8),itemSlot=r.Allocate(8),cls=r.Allocate(256),stat=r.Allocate(32),canvas=r.Allocate(512),fc=r.Allocate(32),player=r.Allocate(512),bag=r.Allocate(128),ic=r.Allocate(32),item=r.Allocate(64);
 P(r,csSlot,cls);P(r,cls+0xb8,stat);P(r,stat,canvas);P(r,fcSlot,fc);P(r,fc,fc);P(r,canvas+0x1b8,fc);P(r,itemSlot,ic);P(r,item,ic);P(r,player+0x128,bag);P(r,bag+24,8);P(r,bag+32+2*8,item);I(r,item+20,161);I(r,item+24,100);I(r,item+32,2);
 int count=0,stopAt=-1;Tick tick=null;Getter getter=delegate{return new IntPtr(player);};Use use=delegate(IntPtr actual,IntPtr info){Check(actual.ToInt64()==item&&info==IntPtr.Zero,"correct bag item");Check(R(r,data)==2,"consumed once, held while using");tick();count++;if(count==stopAt)I(r,data+12,0);};
 byte[] bytes=NativeCode.UseItem(data,csSlot,fcSlot,Marshal.GetFunctionPointerForDelegate(getter).ToInt64(),itemSlot,Marshal.GetFunctionPointerForDelegate(use).ToInt64());long code=r.Allocate(4096);r.Write(code,bytes);r.Protect(code,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(Tick));
 I(r,data+4,161);I(r,data+12,1);I(r,data+24,100);I(r,data,1);tick();tick();Check(count==100&&R(r,data+28)==100&&R(r,data)==0,"100 chests in one invocation, no duplicate invocation");
 I(r,item+24,23);I(r,data+24,100);I(r,data,1);tick();Check(count==123&&R(r,data+28)==23,"batch capped by current stack");
 I(r,item+24,1500);I(r,data+24,1500);I(r,data,1);tick();Check(count==1123&&R(r,data+28)==1000,"large batch bounded to avoid blocking game indefinitely");
 stopAt=count+4;I(r,data+24,100);I(r,data,1);tick();Check(count==1127&&R(r,data+28)==4&&R(r,data)==0,"gate rechecked each use");
 I(r,data+12,1);I(r,data+24,1);I(r,data,1);tick();Check(count==1128,"generic single use retained");I(r,data+24,0);I(r,data,1);tick();Check(count==1129,"old clients default to one");
 I(r,data+4,277);I(r,data+24,100);I(r,data,1);tick();Check(count==1129,"wrong ID never used");
 GC.KeepAlive(getter);GC.KeepAlive(use);Console.WriteLine("PASS: 100 chests in one quantity action, once-only/reentry, live stack cap, large batch cap, mid-batch gate, single-use compatibility and wrong ID; "+bytes.Length+" bytes.");
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
