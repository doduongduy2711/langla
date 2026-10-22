using System;
using System.Diagnostics;
using System.Threading;
using TGLL;
class LiveCountTests {
    static int Main(string[] args){
        if(args.Length>0&&args[0]=="child"){Thread.Sleep(20000);return 0;}
        string exe=Process.GetCurrentProcess().MainModule.FileName;
        Process first=null,second=null;
        try{
            var info=new ProcessStartInfo(exe,"child"){UseShellExecute=false,CreateNoWindow=true};
            first=Process.Start(info);second=Process.Start(info);
            ManagedGames.Register(first,exe);ManagedGames.Register(second,exe);ManagedGames.Register(first,exe);
            if(ManagedGames.RunningProcessIds().Length!=2)throw new Exception("Duplicate registration count");
            first.Kill();first.WaitForExit(3000);
            int[] live=ManagedGames.RunningProcessIds();
            if(live.Length!=1||live[0]!=second.Id)throw new Exception("Exited PID counted");
            second.Kill();second.WaitForExit(3000);
            if(ManagedGames.RunningProcessIds().Length!=0)throw new Exception("Stale identities counted");
            Console.WriteLine("PASS: real Windows child process identities, duplicate registration, process exit and empty registry.");return 0;
        }finally{
            foreach(Process p in new[]{first,second})if(p!=null){try{if(!p.HasExited)p.Kill();}catch{}p.Dispose();}
        }
    }
}
