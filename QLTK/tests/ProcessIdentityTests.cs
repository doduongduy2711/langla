namespace TGLL {
 public static class ProcessIdentityTests {
  static void Assert(bool value,string message){if(!value)throw new Exception(message);}
  public static string Run(){
   using(Process self=Process.GetCurrentProcess()){
    string path;long started;ManagedGames.ReadIdentity(self.Id,out path,out started);ManagedGames.Register(self,path);
    Assert(ManagedGames.IsRunning(self.Id),"Live registered process rejected");Assert(!ManagedGames.IsRunning(-1),"Unregistered PID accepted");
    var registry=(System.Collections.IDictionary)typeof(ManagedGames).GetField("games",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
    object identity=registry[self.Id];var type=identity.GetType();var time=type.GetField("Started",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);var image=type.GetField("Path",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    time.SetValue(identity,started-1);Assert(!ManagedGames.IsRunning(self.Id),"Reused PID accepted");time.SetValue(identity,started);
    image.SetValue(identity,path+".different");Assert(!ManagedGames.IsRunning(self.Id),"Wrong executable accepted");image.SetValue(identity,path);
    using(ManagedGames.BeginProcessReadScope()){
     Assert(ManagedGames.IsRunning(self.Id),"Scoped live process rejected");
     ManagedGames.Register(self,path);object replacement=registry[self.Id];time.SetValue(replacement,started-1);
     Assert(!ManagedGames.IsRunning(self.Id),"Scope cached an obsolete registration");time.SetValue(replacement,started);
     Assert(ManagedGames.RunningProcessIds().Contains(self.Id),"Live snapshot lost registered process");
    }
   }
   var info=new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoProfile -Command Start-Sleep -Milliseconds 500");info.UseShellExecute=false;info.CreateNoWindow=true;
   using(Process child=Process.Start(info)){
    ManagedGames.Register(child,info.FileName);Assert(ManagedGames.IsRunning(child.Id),"Live child rejected");Assert(child.WaitForExit(5000),"Test child did not exit");Assert(!ManagedGames.IsRunning(child.Id),"Exited process accepted");Assert(!ManagedGames.RunningProcessIds().Contains(child.Id),"Exited process retained in live snapshot");
   }
   Process[] games=Process.GetProcessesByName("TheGioiLangLa");var registered=new List<int>();
   foreach(Process game in games)using(game){try{string path;long started;ManagedGames.ReadIdentity(game.Id,out path,out started);ManagedGames.Register(game,path);registered.Add(game.Id);}catch{}}
   var watch=Stopwatch.StartNew();int live=0;foreach(int id in registered)if(ManagedGames.IsRunning(id))live++;long checks=watch.ElapsedMilliseconds;
   watch.Restart();int snapshot=ManagedGames.RunningProcessIds().Length;long scan=watch.ElapsedMilliseconds;
   return "PASS: PID creation time/path verified; unknown/exited/replaced identities rejected; scopes invalidated; "+live+" real game clients checked in "+checks+"ms; live snapshot in "+scan+"ms (read-only).";
  }
 }
}
