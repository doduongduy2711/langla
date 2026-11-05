using System;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class CharacterSpeedTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void P(Remote r,long a,long v){r.Write(a,BitConverter.GetBytes(v));}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}
 static int I(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static void M(Remote r,long a,int v){r.Write(a+0x60,BitConverter.GetBytes((short)v));}
 static int M(Remote r,long a){return BitConverter.ToInt16(r.Read(a+0x60,2),0);}
 static int Main(){try{using(var r=new Remote(Process.GetCurrentProcess())){
  long data=r.Allocate(4096),cs=r.Allocate(8),fs=r.Allocate(8),ps=r.Allocate(8),cc=r.Allocate(512),fc=r.Allocate(512),pc=r.Allocate(512),cstat=r.Allocate(32),fstat=r.Allocate(32),pstat=r.Allocate(32),canvas=r.Allocate(512),world=r.Allocate(1024),player=r.Allocate(1024),other=r.Allocate(1024),route=r.Allocate(128);
  P(r,cs,cc);P(r,fs,fc);P(r,ps,pc);P(r,cc+0xb8,cstat);P(r,fc+0xb8,fstat);P(r,pc+0xb8,pstat);P(r,cstat,canvas);P(r,fstat+16,world);P(r,pstat+8,player);P(r,world,fc);P(r,canvas+0x1b8,world);P(r,player,pc);I(r,player+0xa8,100);M(r,player,7);I(r,player+0x80,0);P(r,player+0x98,route);r.Write(route+0x1c,new byte[]{7});
  byte[] bytes=NativeCode.CharacterSpeed(data,cs,fs,ps);Check(bytes.Length<4096,"routine fits allocation");long exe=r.Allocate(4096);r.Write(exe,bytes);r.Protect(exe,4096,0x20);var tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));
  tick();Check(M(r,player)==12&&I(r,player+0x80)==100&&r.Read(route+0x1c,1)[0]==12,"apply movement 7->12, attack +100, current route");
  for(int n=0;n<10000;n++)tick();Check(M(r,player)==12&&I(r,player+0x80)==100,"no compounding across frames");
  M(r,player,8);I(r,player+0x80,20);tick();Check(M(r,player)==14&&I(r,player+0x80)==120&&r.Read(route+0x1c,1)[0]==14,"server refresh uses fresh base and updates old route");
  M(r,player,5);I(r,player+0x80,-50);r.Write(route+0x1c,new byte[]{5});tick();Check(M(r,player)==9&&I(r,player+0x80)==50&&r.Read(route+0x1c,1)[0]==9,"rounding and attack debuff preserved");
  r.Write(route+0x1c,new byte[]{0});tick();Check(r.Read(route+0x1c,1)[0]==0,"explicit/scripted route unchanged");r.Write(route+0x1c,new byte[]{3});tick();Check(r.Read(route+0x1c,1)[0]==3,"unrelated route speed unchanged");
  P(r,other,pc);I(r,other+0xa8,100);M(r,other,6);I(r,other+0x80,10);P(r,pstat+8,other);tick();Check(M(r,other)==10&&I(r,other+0x80)==110,"new character gets own base");
  M(r,other,127);I(r,other+0x80,1000);tick();tick();Check(M(r,other)==127&&I(r,other+0x80)==1100,"signed route step capped and high attack stable");
  M(r,other,7);I(r,other+0x80,0);P(r,canvas+0x1b8,0);tick();Check(M(r,other)==7&&I(r,other+0x80)==0,"login/non-world screen untouched");P(r,canvas+0x1b8,world);I(r,other+0xa8,0);tick();Check(M(r,other)==7,"dead character untouched");I(r,other+0xa8,100);P(r,other,cc);tick();Check(M(r,other)==7,"wrong player class untouched");P(r,other,pc);M(r,other,0);tick();Check(I(r,other+0x80)==0,"unready movement untouched");M(r,other,7);P(r,ps,1);tick();Check(M(r,other)==7,"unresolved class untouched");
  var login=typeof(NativeCode).GetMethod("Login",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);object[] args=new object[login.GetParameters().Length];for(int n=0;n<args.Length;n++)args[n]=1L;args[args.Length-1]=0L;int oldLength=((byte[])login.Invoke(null,args)).Length;args[args.Length-1]=1L;int newLength=((byte[])login.Invoke(null,args)).Length;Check(newLength==oldLength+12&&newLength<=896,"speed included once and login fits before character code");
  Console.WriteLine("PASS: native client speed, per-character bases, 10000 frames without compounding, server stat refresh, routes, rounding, caps and world guards; bytes="+bytes.Length+" login="+newLength);
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
