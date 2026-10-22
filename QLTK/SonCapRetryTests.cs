using System;using System.Diagnostics;using TGLL;
class SonCapRetryTests {
 static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}static int R(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/soncap143-logs");using(var r=new Remote(Process.GetCurrentProcess())){
  long data=r.Allocate(4096),root=r.Allocate(512);r.Write(data+8,BitConverter.GetBytes(root));I(r,data+4,1);
  var j=new NativeJob("fixture","synthetic","",null,true);j.SonCapEnabled=true;j.SonCapMapAutoActive=true;j.SonCapStage=0;j.SonCapAutoMap=94;j.GameAutoWanted=true;j.GameAutoEnabled=true;
  DateTime t=new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
  NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t);C(R(r,data)==1&&R(r,data+32)==1&&!j.GameAutoEnabled&&j.SonCapAutoRetries==1,"live-off state not retried despite stale enabled snapshot");
  I(r,data,0);NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(2));C(R(r,data)==0,"retry too fast");
  I(r,data,3);NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(3));C(R(r,data)==3&&j.SonCapAutoRetries==1,"busy game callback overwritten");
  I(r,data,0);r.Write(data+8,BitConverter.GetBytes(root+8));NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(4));C(R(r,data)==0,"obsolete panel used");r.Write(data+8,BitConverter.GetBytes(root));
  NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(5));C(R(r,data)==1&&j.SonCapAutoRetries==2,"free channel not retried");
  I(r,data,0);r.Write(root+0x188,new byte[]{1});NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(9));C(R(r,data)==0&&j.GameAutoEnabled,"confirmed game idle resent");
  r.Write(root+0x188,new byte[]{0});C(NativeBackend.SonCapReleaseAllowed(j),"combat key release gate missing");j.SonCapMoving=true;C(!NativeBackend.SonCapReleaseAllowed(j),"portal key release allowed during movement");NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(10));C(R(r,data)==0,"portal combat forced");j.SonCapMoving=false;j.SonCapEnabled=false;NativeBackend.PumpSonCapAutoRetry(r,data,root,j,t.AddSeconds(11));C(R(r,data)==0,"stopped job retried");
 }Console.WriteLine("PASS: live-state recovery, 3-second retry spacing, busy callback and stale panel guards, confirmed idle/portal/stop; synthetic process only.");}
}
