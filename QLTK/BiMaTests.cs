using System;using System.IO;using System.Linq;using System.Reflection;using System.Diagnostics;using System.Collections.Concurrent;using TGLL;
class BiMaTests {
 static void C(bool b,string m){if(!b)throw new Exception(m);}static void P(Remote r,long p,long v){r.Write(p,BitConverter.GetBytes(v));}static void I(Remote r,long p,int v){r.Write(p,BitConverter.GetBytes(v));}
 static int Main(){try{
  string directory=Path.GetFullPath("work/bima-test-"+Guid.NewGuid().ToString("N"));NativeBackend.BiMaDataDirectory=Path.Combine(directory,"counts");NativeBackend.LogDirectory=Path.Combine(directory,"logs");
  var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  string key=Guid.NewGuid().ToString("N");var j=new NativeJob("synthetic.exe","one","unused",null,true);jobs[j.Id]=j;NativeBackend.BindBiMaAccount(j.Id,key);
  C(NativeBackend.GetBiMaSnapshot(key).Count==-1,"new account unknown is not zero");
  using(Remote r=new Remote(Process.GetCurrentProcess())){
   long game=r.Allocate(0x1700000),cc=r.Allocate(256),pc=r.Allocate(256),ic=r.Allocate(256),fcClass=r.Allocate(256),cs=r.Allocate(16),ps=r.Allocate(16),canvas=r.Allocate(512),player=r.Allocate(512),world=r.Allocate(512),bag=r.Allocate(64);
   P(r,game+0x16ed658,cc);P(r,game+0x16ee330,pc);P(r,game+0x16ee578,ic);P(r,game+0x16ee888,fcClass);P(r,cc+0xb8,cs);P(r,pc+0xb8,ps);P(r,cs,canvas);P(r,ps+8,player);P(r,player,pc);P(r,world,fcClass);P(r,canvas+0x1b8,world);P(r,player+0x128,bag);I(r,bag+24,4);
   long[] items=new long[3];for(int n=0;n<3;n++){items[n]=r.Allocate(128);P(r,items[n],ic);I(r,items[n]+20,n==2?999:1276);I(r,items[n]+24,n==0?7:n==1?9:100);P(r,bag+32+n*8,items[n]);}r.Write(items[1]+97,new byte[]{1});
   C(NativeBackend.ReadBiMaCount(r,game)==16,"sum all matching stacks including locked; ignore other IDs");
   j.State="opened";j.TrainReady=true;j.GameSettingsReady=true;NativeBackend.PumpBiMa(r,game,j);C(NativeBackend.GetBiMaSnapshot(key).Count==16,"background sample saves count without opening bag UI");
   I(r,items[0]+24,8);NativeBackend.PumpBiMa(r,game,j);C(NativeBackend.GetBiMaSnapshot(key).Count==16,"sampling throttled");
   P(r,player+0x128,0);j.BiMaPollAt=DateTime.MinValue;NativeBackend.PumpBiMa(r,game,j);C(NativeBackend.GetBiMaSnapshot(key).Count==16,"failed read retains last value");P(r,player+0x128,bag);
   P(r,bag+32,0);P(r,bag+40,0);j.BiMaPollAt=DateTime.MinValue;NativeBackend.PumpBiMa(r,game,j);C(NativeBackend.GetBiMaSnapshot(key).Count==0,"verified no matching items replaces previous count with zero");
   NativeBackend.AcceptBiMa(j,42,DateTime.UtcNow);NativeBackend.RecordBiMaClosed(j,"test");NativeBackend.RecordBiMaClosed(j,"duplicate");
   C(File.ReadAllLines(j.LogFile).Count(x=>x.Contains("bi_ma_at_close"))==1&&File.ReadAllText(j.LogFile).Contains("\"quantity\":42"),"close log quantity and single record");
   var cache=(System.Collections.Generic.Dictionary<string,BiMaSnapshot>)typeof(NativeBackend).GetField("biMaStore",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);cache.Clear();C(NativeBackend.GetBiMaSnapshot(key).Count==42,"restart reloads last verified count");
   var reopened=new NativeJob("synthetic.exe","one","unused",null,true);jobs[reopened.Id]=reopened;NativeBackend.BindBiMaAccount(reopened.Id,key);NativeBackend.AcceptBiMa(reopened,5,DateTime.UtcNow);NativeBackend.AcceptBiMa(j,99,DateTime.UtcNow);C(NativeBackend.GetBiMaSnapshot(key).Count==5,"old session cannot overwrite latest login");
   C(NativeBackend.GetBiMaSnapshot("../escape").Count==-1,"store key path traversal rejected");
   string other=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(NativeBackend.BiMaDataDirectory,other+".txt"),"broken");C(NativeBackend.GetBiMaSnapshot(other).Count==-1,"corrupt count never becomes zero");
  }
  Console.WriteLine("PASS: ID1276 stack sum, locked items, background cadence, failed read retention, verified zero, log once at close, disk restoration and latest-login isolation. No game closed.");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
