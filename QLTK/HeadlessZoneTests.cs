using System;using System.Collections.Generic;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class HeadlessZoneTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Reply(IntPtr world,IntPtr message,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate short ReadShort(IntPtr reader,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Alloc(IntPtr cls,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Ctor(IntPtr self,byte code,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Send(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void WriteByte(IntPtr self,int value,IntPtr info);
 static void C(bool ok,string text){if(!ok)throw new Exception(text);}
 static void P(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static int Int(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
 static void Stub(Remote r,long p,Delegate d){Code64 c=new Code64();c.Rax(Marshal.GetFunctionPointerForDelegate(d).ToInt64());c.Emit(0xff,0xe0);r.Write(p,c.Finish());}
 static void Main(){using(var r=new Remote(Process.GetCurrentProcess())){
  long g=r.Allocate(0x1800000),d=r.Allocate(4096),pending=r.Allocate(4096),fcClass=r.Allocate(2048),fcStatic=r.Allocate(64),fc=r.Allocate(2048),canvasClass=r.Allocate(256),canvasStatic=r.Allocate(64),canvas=r.Allocate(512),messageClass=r.Allocate(1024),message=r.Allocate(128),readerClass=r.Allocate(512),reader=r.Allocate(64),response=r.Allocate(64),untouchedPopup=r.Allocate(256);
  r.Protect(g,0x1800000,0x40);P(r,g+0x16ee888,fcClass);P(r,fcClass+0xb8,fcStatic);P(r,fcStatic+16,fc);P(r,fc,fcClass);P(r,g+0x16ed658,canvasClass);P(r,canvasClass+0xb8,canvasStatic);P(r,canvasStatic,canvas);P(r,canvas+0x1b8,fc);P(r,g+0x16ee820,messageClass);P(r,message,messageClass);P(r,reader,readerClass);P(r,response+24,reader);P(r,fc+0x90,untouchedPopup);r.Write(fc+0x134,BitConverter.GetBytes((short)59));r.Write(fc+0x13a,BitConverter.GetBytes((short)1));I(r,d+8,59);I(r,d+12,1);I(r,d+16,2);
  int sends=0,originalCalls=0,reads=0,uiCalls=0;byte opcode=0;var bytes=new List<int>();var payload=new Queue<short>();Tick zoneTick=null;
  Alloc alloc=delegate{return new IntPtr(message);};Ctor ctor=delegate(IntPtr self,byte op,IntPtr info){opcode=op;zoneTick();};Send send=delegate{sends++;};WriteByte write=delegate(IntPtr self,int value,IntPtr info){bytes.Add(value);};Tick ui=delegate{uiCalls++;};
  Reply original=delegate{originalCalls++;};ReadShort read=delegate{reads++;return payload.Dequeue();};
  Stub(r,g+0x16f760,alloc);Stub(r,g+0x2e2aa0,ctor);Stub(r,g+0x304ac0,ui);P(r,messageClass+0x298,Marshal.GetFunctionPointerForDelegate(send).ToInt64());P(r,messageClass+0x228,Marshal.GetFunctionPointerForDelegate(write).ToInt64());P(r,fcClass+0x4d8,Marshal.GetFunctionPointerForDelegate(ui).ToInt64());P(r,readerClass+0x178,Marshal.GetFunctionPointerForDelegate(read).ToInt64());
  byte[] commandCode=NativeCode.Zone(d,g,0,pending),replyCode=NativeCode.ZoneReply(pending,Marshal.GetFunctionPointerForDelegate(original).ToInt64());C(commandCode.Length<4096&&replyCode.Length<2048,"native code exceeds allocation");long exe=r.Allocate(4096),capture=r.Allocate(4096);r.Write(exe,commandCode);r.Write(capture,replyCode);r.Protect(exe,4096,0x20);r.Protect(capture,4096,0x20);zoneTick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));var reply=(Reply)Marshal.GetDelegateForFunctionPointer(new IntPtr(capture),typeof(Reply));
  Action request=delegate{I(r,d,1);zoneTick();zoneTick();C(Int(r,d)==4&&opcode==0xfa,"FA population request not sent");};
  request();C(sends==1&&bytes.Count==0&&uiCalls==0&&r.Ptr(fc+0x90)==untouchedPopup,"query opens UI/writes unexpected payload/repeats send");
  int[] ids,counts;C(!NativeBackend.TryReadZoneList(r,d,out ids,out counts),"old/unreceived populations accepted");
  foreach(short v in new short[]{3,120,80,3,0,9,1,0,2,3})payload.Enqueue(v);reply(new IntPtr(fc),new IntPtr(response),IntPtr.Zero);
  C(NativeBackend.TryReadZoneList(r,d,out ids,out counts)&&counts.Length==3&&counts[0]==9&&counts[1]==0&&counts[2]==3,"server populations parsed incorrectly");C(r.Ptr(pending)==0&&originalCalls==0&&uiCalls==0&&r.Ptr(fc+0x90)==untouchedPopup,"reply disturbed visible UI");
  reply(new IntPtr(fc),new IntPtr(response),IntPtr.Zero);C(originalCalls==1&&reads==10,"unsolicited/manual response was consumed");
  request();C(!NativeBackend.TryReadZoneList(r,d,out ids,out counts),"previous response reused for next request");foreach(short v in new short[]{3,120,80,1,3,1})payload.Enqueue(v);reply(new IntPtr(fc),new IntPtr(response),IntPtr.Zero);payload.Clear();C(!NativeBackend.TryReadZoneList(r,d,out ids,out counts),"invalid zone index committed");
  request();r.Write(fc+0x134,BitConverter.GetBytes((short)69));int before=reads;reply(new IntPtr(fc),new IntPtr(response),IntPtr.Zero);C(originalCalls==2&&reads==before&&!NativeBackend.TryReadZoneList(r,d,out ids,out counts),"changed-map reply accepted");r.Write(fc+0x134,BitConverter.GetBytes((short)59));
  I(r,d+16,2);I(r,d,2);bytes.Clear();zoneTick();C(opcode==0xf9&&bytes.Count==1&&bytes[0]==2&&uiCalls==0&&r.Ptr(fc+0x90)==untouchedPopup,"zone change did not send direct F9 byte or touched another popup");
  GC.KeepAlive(alloc);GC.KeepAlive(ctor);GC.KeepAlive(send);GC.KeepAlive(write);GC.KeepAlive(read);GC.KeepAlive(original);GC.KeepAlive(ui);
  Console.WriteLine("PASS: native FA request and reply counts, no UI construction/attachment/closure, fresh-response gate, original manual response preservation, malformed/changed-map rejection, native F9 change and no duplicate send. No real server/game input.");
 }}
}
