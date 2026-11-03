using System;using System.Diagnostics;using System.Reflection;using System.Threading;using TGLL;
class ProcessReadScopeTests {
 static void C(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(string[] args){
  if(args.Length>0){Thread.Sleep(30000);return;}
  string exe=Assembly.GetExecutingAssembly().Location;var self=Process.GetCurrentProcess();ManagedGames.Register(self,exe);
  var timer=Stopwatch.StartNew();for(int n=0;n<3000;n++)C(ManagedGames.IsRunning(self.Id),"self not running");long plain=timer.ElapsedTicks;
  timer.Restart();using(ManagedGames.BeginProcessReadScope()){for(int n=0;n<3000;n++)C(ManagedGames.IsRunning(self.Id),"cached self missing");ManagedGames.Register(self,exe);C(ManagedGames.IsRunning(self.Id),"replacement registration missing");}long cached=timer.ElapsedTicks;
  using(var child=Process.Start(new ProcessStartInfo(exe,"--child"){UseShellExecute=false,CreateNoWindow=true})){
   try{
    ManagedGames.Register(child,exe);
    using(ManagedGames.BeginProcessReadScope()){
     C(ManagedGames.IsRunning(child.Id),"child missing before exit");child.Kill();C(child.WaitForExit(5000),"fixture did not exit");
     C(ManagedGames.IsRunning(child.Id),"positive read should conservatively retain capacity until pass ends");
     bool workerLive=true;var worker=new Thread(delegate(){workerLive=ManagedGames.IsRunning(child.Id);});worker.Start();worker.Join();C(!workerLive,"read scope leaked to worker thread");
     using(ManagedGames.BeginProcessReadScope())C(!ManagedGames.IsRunning(child.Id),"nested pass did not read fresh exit");
     C(ManagedGames.IsRunning(child.Id),"nested dispose failed to restore outer scope");
    }
    C(!ManagedGames.IsRunning(child.Id),"exit cached across queue passes");
   }finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}
  }
  Console.WriteLine("PASS: 3000 repeated process reads unscoped_ms="+(plain*1000.0/Stopwatch.Frequency).ToString("F2")+" scoped_ms="+(cached*1000.0/Stopwatch.Frequency).ToString("F2")+"; exit holds only current pass, next pass/worker sees exit, nested scopes and re-registration supported. Only synthetic fixture process used.");
 }
}
