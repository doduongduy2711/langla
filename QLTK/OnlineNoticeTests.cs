using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Text;using TGLL;
class OnlineNoticeTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate bool Contains(IntPtr self,IntPtr value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Confirm(IntPtr self,int command,IntPtr popup,IntPtr button,IntPtr info);
 static void C(bool ok,string text){if(!ok)throw new Exception(text);}
 static void P(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static string Message(int count){return "Đang có "+count+" player online. Tích cực tham gia tương tác ở Fanpage để nhận quà Fan cứng hàng tuần nhé!";}
 static long S(Remote r,string text){long p=r.Allocate(32+text.Length*2);I(r,p+16,text.Length);r.Write(p+20,Encoding.Unicode.GetBytes(text));return p;}
 static void Main(){
 NativeBackend.LogDirectory=Path.Combine(Path.GetTempPath(),"QLTK-online-notice-tests-"+Guid.NewGuid().ToString("N"));
 C(NativeBackend.IsOnlineFanpageNotice(Message(3860)),"exact pictured notice missed");C(NativeBackend.IsOnlineFanpageNotice(Message(12).Replace(" ","\n")),"wrapped notice missed");
 foreach(string text in new[]{"Bạn có muốn mua vật phẩm này không?","Người chơi A không đủ lượt tham gia Sơn cáp",Message(1)+" Xác nhận mua vật phẩm","Đang có player online", ""})C(!NativeBackend.IsOnlineFanpageNotice(text),"unrelated popup matched");
 using(var r=new Remote(Process.GetCurrentProcess())){
  long game=r.Allocate(0x1700000),data=r.Allocate(4096),canvasClass=r.Allocate(512),canvasStatic=r.Allocate(64),canvas=r.Allocate(512),worldClass=r.Allocate(2048),worldStatic=r.Allocate(64),world=r.Allocate(1024),dialogClass=r.Allocate(512),popup=r.Allocate(512),button=r.Allocate(256),vectorClass=r.Allocate(1024),vector=r.Allocate(64),list=r.Allocate(64),array=r.Allocate(64);
  P(r,game+0x16ed658,canvasClass);P(r,canvasClass+0xb8,canvasStatic);P(r,canvasStatic,canvas);P(r,canvas+0x1b8,world);P(r,game+0x16ee888,worldClass);P(r,worldClass+0xb8,worldStatic);P(r,worldStatic+16,world);P(r,world,worldClass);
  P(r,game+0x16eeba8,dialogClass);P(r,popup,dialogClass);P(r,popup+0xe8,button);I(r,button+0x80,-7);P(r,button+0x78,S(r,"Xác nhận"));long body=S(r,Message(3860));P(r,popup+0xb0,body);P(r,world+0x90,popup);P(r,world+0x88,vector);
  P(r,vector,vectorClass);P(r,vector+16,list);P(r,list+16,array);I(r,list+24,1);I(r,array+24,1);P(r,array+32,popup);
  bool visible=true;int clicked=0;
  Contains contains=delegate(IntPtr self,IntPtr value,IntPtr info){C(self.ToInt64()==vector,"wrong live overlay check");return visible&&value.ToInt64()==popup;};
  Confirm confirm=delegate(IntPtr self,int command,IntPtr target,IntPtr targetButton,IntPtr info){C(self.ToInt64()==world&&command==-7&&target.ToInt64()==popup&&targetButton.ToInt64()==button,"wrong confirmation dispatch");clicked++;};
  P(r,vectorClass+0x208,Marshal.GetFunctionPointerForDelegate(contains).ToInt64());P(r,worldClass+0x488,Marshal.GetFunctionPointerForDelegate(confirm).ToInt64());
  byte[] bytes=NativeCode.PrepareMap(data,game+0x16ed658,game+0x16ee888,game+0x16eeba8,game+0x1dd900,0,true);C(bytes.Length<4096,"notice callback allocation exceeded");long exe=r.Allocate(4096);r.Write(exe,bytes);r.Protect(exe,4096,0x20);Tick tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));var j=new NativeJob("synthetic.exe","notice-fixture","unused",null,true);
  NativeBackend.PumpOnlineNotice(r,data,j,game);C(BitConverter.ToInt32(r.Read(data,4),0)==15,"worker did not arm pictured popup");tick();tick();C(clicked==1,"notice did not close exactly once");visible=false;tick();C(BitConverter.ToInt32(r.Read(data,4),0)==16,"dismissal was not confirmed");
  visible=true;NativeBackend.PumpOnlineNotice(r,data,j,game);P(r,popup+0xb0,S(r,"Bạn có muốn mua vật phẩm?"));tick();C(clicked==1,"changed message in same dialog was confirmed");
  P(r,popup+0xb0,body);NativeBackend.PumpOnlineNotice(r,data,j,game);P(r,world+0x90,r.Allocate(512));tick();C(clicked==1,"replaced dialog was confirmed");P(r,world+0x90,popup);
  NativeBackend.PumpOnlineNotice(r,data,j,game);P(r,button+0x78,S(r,"Đồng ý"));tick();C(clicked==1,"unrecognized button was clicked");P(r,button+0x78,S(r,"Xác nhận"));
  NativeBackend.PumpOnlineNotice(r,data,j,game);visible=false;tick();C(clicked==1,"stale inactive dialog was clicked");visible=true;
  P(r,popup+0xb0,S(r,Message(4001)));NativeBackend.PumpOnlineNotice(r,data,j,game);tick();C(clicked==2,"recurring online notice was not dismissed");
  GC.KeepAlive(contains);GC.KeepAlive(confirm);
 }
 Console.WriteLine("PASS: exact online/Fanpage text, dynamic count/wrapped text, live overlay and button action, one confirmation until closure, recurring notices, changed immutable body, replacement popup, wrong button and stale overlay exclusions; no real game/UI action.");
 }
}
