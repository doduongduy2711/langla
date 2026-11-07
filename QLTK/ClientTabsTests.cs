using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TGLL.ClientTabs;
public static class ClientTabsTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out WindowHost.Rect rect);
 [STAThread]public static void Main(string[] args){
  Application.EnableVisualStyles();
  if(args.Length>0&&args[0]=="--synthetic-client"){
   using(var client=new Form()){client.StartPosition=FormStartPosition.Manual;client.Bounds=new Rectangle(80,90,550,400);client.Shown+=delegate{Console.WriteLine(client.Handle.ToInt64());Console.Out.Flush();};Application.Run(client);}return;
  }
  using(var container=new Form())using(var panel=new Panel())using(var game=new Form())using(var hidden=new Form()){
   panel.Size=new Size(640,420);container.Controls.Add(panel);var parent=panel.Handle;
   game.StartPosition=FormStartPosition.Manual;game.Bounds=new Rectangle(70,80,550,400);game.Show();Application.DoEvents();
   IntPtr hwnd=game.Handle;IntPtr style=WindowHost.GetStyle(hwnd,-16);WindowHost.Rect bounds;GetWindowRect(hwnd,out bounds);
   var host=new WindowHost();host.Attach(hwnd,parent,panel.ClientSize);
   Check(WindowHost.GetParent(hwnd)==parent,"Client was not attached");
   host.Resize(new Size(720,450));host.SwitchAway();Application.DoEvents();
   Check(WindowHost.GetParent(hwnd)==IntPtr.Zero,"Switched client remains parented");
   Check((WindowHost.GetStyle(hwnd,-16).ToInt64()&~0x10000000L)==(style.ToInt64()&~0x10000000L),"Window style was not restored");
   Check(!WindowHost.IsWindowVisible(hwnd),"Background tab client was left visible");
   host.Attach(hidden.Handle,parent,panel.ClientSize);host.RestoreAll();Application.DoEvents();
   Check(WindowHost.GetParent(hidden.Handle)==IntPtr.Zero,"Hidden client remains parented after close");
   Check(!WindowHost.IsWindowVisible(hidden.Handle),"Original hidden state changed");
   Check(WindowHost.IsWindowVisible(hwnd),"Original visible state was not restored");
   WindowHost.Rect restored;GetWindowRect(hwnd,out restored);
   Check(restored.Left==bounds.Left&&restored.Top==bounds.Top&&restored.Right==bounds.Right&&restored.Bottom==bounds.Bottom,"Original bounds changed");
   host.Attach(hwnd,parent,panel.ClientSize);host.Detach(true);Application.DoEvents();
   Check(WindowHost.IsWindow(hwnd)&&WindowHost.GetParent(hwnd)==IntPtr.Zero&&WindowHost.IsWindowVisible(hwnd),"Detach closed or hid the game");
   for(int i=0;i<120;i++){host.Attach(hwnd,parent,panel.ClientSize);host.SwitchAway();}
   host.RestoreAll();Check(WindowHost.IsWindow(hwnd),"Repeated switches destroyed client");
   host.Attach(hidden.Handle,parent,panel.ClientSize);hidden.Dispose();host.RestoreAll();
   Check(host.Window==IntPtr.Zero,"Exited client was retained");
   Console.WriteLine("PASS: attach, resize, detach, 120 switches, original bounds/styles/visibility, client exit and restore before container disposal. Synthetic windows only; no real game touched.");
  }
  using(var frame=new Form())using(var panel=new Panel()){
   panel.Size=new Size(650,450);frame.Controls.Add(panel);IntPtr parent=panel.Handle;
   var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--synthetic-client"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true};
   using(var child=Process.Start(info)){
    var host=new WindowHost();
    try{
     var line=child.StandardOutput.ReadLineAsync();Check(line.Wait(5000),"Synthetic child did not initialize");IntPtr hwnd=new IntPtr(Int64.Parse(line.Result));
     WindowHost.Rect original;GetWindowRect(hwnd,out original);IntPtr originalStyle=WindowHost.GetStyle(hwnd,-16);
     for(int i=0;i<20;i++){host.Attach(hwnd,parent,panel.ClientSize);host.Resize(new Size(600+i,400+i));host.SwitchAway();}
     host.RestoreAll();System.Threading.Thread.Sleep(200);
     WindowHost.Rect restored;GetWindowRect(hwnd,out restored);
     Check(WindowHost.GetParent(hwnd)==IntPtr.Zero,"Cross-process client remains attached");
     Check(WindowHost.IsWindow(hwnd)&&WindowHost.IsWindowVisible(hwnd),"Cross-process client did not survive restore");
     Check(WindowHost.GetStyle(hwnd,-16)==originalStyle,"Cross-process style changed");
     Check(restored.Left==original.Left&&restored.Top==original.Top&&restored.Right==original.Right&&restored.Bottom==original.Bottom,"Cross-process restore bounds changed");
     Console.WriteLine("PASS: separate synthetic process survives 20 attach/resize/switch cycles and restores style, bounds and visibility.");
    }finally{host.RestoreAll();if(!child.HasExited){child.CloseMainWindow();if(!child.WaitForExit(3000))child.Kill();}}
   }
  }
 }
}
