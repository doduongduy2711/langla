using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TGLL.ClientTabs {
 public sealed class ClientInfo { public string Id,Name; public int Pid; }
 public sealed class Connection {
  readonly int port;readonly string token;
  static Dictionary<string,object> Load(string path){return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  public Connection(string directory){
   var config=Load(Path.Combine(directory,"settings.json"));port=Convert.ToInt32(config["socketPort"]);
   token=Convert.ToString(Load(Path.Combine(directory,"api-token.json"))["secret"]);
   if(token.StartsWith("AQAAANCM",StringComparison.Ordinal)){byte[] data=ProtectedData.Unprotect(Convert.FromBase64String(token),null,DataProtectionScope.CurrentUser);try{token=Encoding.UTF8.GetString(data);}finally{Array.Clear(data,0,data.Length);}}
  }
  public Task<List<ClientInfo>> ReadAsync(){return Task.Run(delegate{
   var json=new JavaScriptSerializer{MaxJsonLength=8388608};
   using(var socket=new TcpClient()){
    if(!socket.ConnectAsync("127.0.0.1",port).Wait(3000))throw new IOException("Không kết nối được QLTK.");
    socket.ReceiveTimeout=5000;socket.SendTimeout=3000;
    using(var stream=socket.GetStream()){
     byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(new {token=token,command="manager_snapshot"})+"\n");stream.Write(bytes,0,bytes.Length);Array.Clear(bytes,0,bytes.Length);
     using(var buffer=new MemoryStream()){
      var block=new byte[8192];bool done=false;
      while(!done){int n=stream.Read(block,0,block.Length);if(n==0)throw new IOException("QLTK đã ngắt kết nối.");int end=Array.IndexOf(block,(byte)10,0,n);int size=end<0?n:end;if(buffer.Length+size>8388608)throw new IOException("Trạng thái quá lớn.");buffer.Write(block,0,size);done=end>=0;}
      var result=json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(buffer.ToArray()));
      if(!result.ContainsKey("ok")||!Object.Equals(result["ok"],true))throw new IOException("Chưa đọc được trạng thái QLTK.");
      var accounts=(System.Collections.IEnumerable)result["accounts"];var clients=new List<ClientInfo>();
      foreach(Dictionary<string,object> a in accounts){int pid=a.ContainsKey("pid")?Convert.ToInt32(a["pid"]):0;if(pid<=0)continue;
       if(a.ContainsKey("ready")){if(!Object.Equals(a["ready"],true))continue;}else if(!a.ContainsKey("status")||Convert.ToString(a["status"])!="Đã mở")continue;
       string name=a.ContainsKey("username")?Convert.ToString(a["username"]):"Acc "+(clients.Count+1);clients.Add(new ClientInfo{Id=Convert.ToString(a["id"]),Pid=pid,Name=name});}
      return clients;
     }
    }
   }
  });}
 }
 // Only one selected game is attached. Never send WM_CLOSE or terminate a game.
 public sealed class WindowHost {
  [StructLayout(LayoutKind.Sequential)]public struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Placement {public int Length,Flags,Show;public Point Min,Max;public Rect Normal;}
  public delegate bool EnumProc(IntPtr hwnd,IntPtr arg);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr arg);
  [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int size);
  [DllImport("user32.dll")]public static extern IntPtr GetParent(IntPtr hwnd);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]public static extern IntPtr GetStyle(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetStyle(IntPtr hwnd,int index,IntPtr value);
  [DllImport("kernel32.dll")]static extern void SetLastError(uint error);
  [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr hwnd,ref Placement value);
  [DllImport("user32.dll")]static extern bool SetWindowPlacement(IntPtr hwnd,ref Placement value);
  [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr hwnd,int show);
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
  [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  sealed class SavedVisibility {internal int Pid,Show;internal DateTime Started;internal bool Visible;}
  readonly Dictionary<IntPtr,SavedVisibility> visited=new Dictionary<IntPtr,SavedVisibility>();
  IntPtr window,parent,style;Placement placement;bool visible;int pid;DateTime started;
  public IntPtr Window {get{return window;}}
  public static void ConfigureDpi(){foreach(var p in Process.GetProcessesByName("TheGioiLangLa")){using(p){IntPtr h=Find(p.Id);if(h!=IntPtr.Zero){SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(h));return;}}}}
  public static IntPtr Find(int pid){
   try{using(var p=Process.GetProcessById(pid)){if(p.HasExited||!String.Equals(p.ProcessName,"TheGioiLangLa",StringComparison.OrdinalIgnoreCase))return IntPtr.Zero;}}
   catch(ArgumentException){return IntPtr.Zero;}
   IntPtr found=IntPtr.Zero;
   EnumWindows(delegate(IntPtr h,IntPtr unused){int actual;GetWindowThreadProcessId(h,out actual);if(actual!=pid)return true;var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);if(name.ToString()!="UnityWndClass")return true;found=h;return false;},IntPtr.Zero);return found;
  }
  bool Valid(){if(window==IntPtr.Zero||!IsWindow(window))return false;int actual;GetWindowThreadProcessId(window,out actual);if(actual!=pid)return false;try{using(var p=Process.GetProcessById(pid))return !p.HasExited&&p.StartTime.ToUniversalTime()==started;}catch(ArgumentException){return false;}}
  public void Attach(IntPtr game,IntPtr panel,Size size){
   if(window!=IntPtr.Zero)throw new InvalidOperationException("Hãy tách client hiện tại trước.");
   if(!IsWindow(game)||!IsWindow(panel))throw new IOException("Cửa sổ client không còn sẵn sàng.");
   if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(game),GetWindowDpiAwarenessContext(panel)))throw new IOException("Client và cửa sổ gom đang dùng chế độ hiển thị khác nhau. Hãy tách client để dùng riêng.");
   int actual;GetWindowThreadProcessId(game,out actual);using(var p=Process.GetProcessById(actual))started=p.StartTime.ToUniversalTime();
   var saved=new Placement();saved.Length=Marshal.SizeOf(saved);if(!GetWindowPlacement(game,ref saved))throw new IOException("Không lưu được vị trí client.");
   window=game;pid=actual;parent=GetParent(game);style=GetStyle(game,-16);placement=saved;visible=IsWindowVisible(game);
   SavedVisibility original;
   if(!visited.TryGetValue(game,out original)||original.Pid!=pid||original.Started!=started){original=new SavedVisibility{Pid=pid,Started=started,Visible=visible,Show=saved.Show};visited[game]=original;}
   visible=original.Visible;
   try{
    ShowWindowAsync(game,0);
    long flags=(style.ToInt64()&~(0x80000000L|0x00c00000L|0x00040000L|0x00020000L|0x00010000L))|0x40000000L;
    SetLastError(0);SetStyle(game,-16,new IntPtr(flags));if(Marshal.GetLastWin32Error()!=0)throw new IOException("Không đổi được kiểu cửa sổ client.");
    SetLastError(0);SetParent(game,panel);if(Marshal.GetLastWin32Error()!=0)throw new IOException("Không ghép được client vào tab.");
    Resize(size);ShowWindowAsync(game,5);
   }catch{Detach(false);throw;}
  }
  public void Resize(Size size){if(Valid()&&!SetWindowPos(window,IntPtr.Zero,0,0,Math.Max(1,size.Width),Math.Max(1,size.Height),0x0034|0x4000))throw new IOException("Không chỉnh được vùng xem client.");}
  public void Detach(bool show){
   if(window==IntPtr.Zero)return;
   if(!Valid()){window=IntPtr.Zero;return;}
   ShowWindowAsync(window,0);
   SetLastError(0);SetParent(window,parent);if(Marshal.GetLastWin32Error()!=0)throw new IOException("Chưa tách được client. Hãy thử lại trước khi đóng cửa sổ gom.");
   SetLastError(0);SetStyle(window,-16,style);if(Marshal.GetLastWin32Error()!=0)throw new IOException("Chưa khôi phục được khung client.");
   if(!SetWindowPlacement(window,ref placement))throw new IOException("Chưa khôi phục được vị trí client.");
   SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x0037|0x4000);
   ShowWindowAsync(window,show?5:(visible?placement.Show:0));
   if(show){visited[window].Visible=true;visited[window].Show=5;}
   window=IntPtr.Zero;
  }
  public void SwitchAway(){IntPtr previous=window;Detach(false);if(previous!=IntPtr.Zero&&IsWindow(previous))ShowWindowAsync(previous,0);}
  public void RestoreAll(){
   Detach(false);
   foreach(var item in visited){if(!IsWindow(item.Key))continue;int actual;GetWindowThreadProcessId(item.Key,out actual);if(actual!=item.Value.Pid)continue;
    try{using(var p=Process.GetProcessById(actual)){if(!p.HasExited&&p.StartTime.ToUniversalTime()==item.Value.Started)ShowWindowAsync(item.Key,item.Value.Visible?item.Value.Show:0);}}catch(ArgumentException){}
   }
   visited.Clear();
  }
 }
 public sealed class TabsForm:Form {
  readonly Connection connection;readonly WindowHost host=new WindowHost();readonly TabControl tabs=new TabControl();readonly Panel view=new Panel();readonly Label notice=new Label();readonly Timer timer=new Timer();
  readonly Timer resizeTimer=new Timer();
  readonly Dictionary<string,TabPage> pages=new Dictionary<string,TabPage>();bool refreshing,closing,reconciling;string attachedId;
  public TabsForm(string directory){
   connection=new Connection(directory);Text="QLTK • Client theo tab";ClientSize=new Size(1000,700);MinimumSize=new Size(650,450);
   var tools=new FlowLayoutPanel{Dock=DockStyle.Top,Height=36};var refresh=new Button{Text="Làm mới",AutoSize=true};var detach=new Button{Text="Tách client",AutoSize=true};tools.Controls.Add(refresh);tools.Controls.Add(detach);
   notice.Dock=DockStyle.Bottom;notice.Height=30;notice.Text="Chọn một tab để xem client. Các acc khác tiếp tục chạy.";
   tabs.Dock=DockStyle.Top;tabs.Height=30;tabs.Multiline=false;view.Dock=DockStyle.Fill;view.BackColor=Color.Black;
   Controls.Add(view);Controls.Add(tabs);Controls.Add(tools);Controls.Add(notice);
   refresh.Click+=async delegate{await RefreshClients();};detach.Click+=delegate{TryDetach(true);};
   tabs.SelectedIndexChanged+=delegate{if(!reconciling)SelectClient();};
   tabs.MouseClick+=delegate{if(!reconciling)SelectClient();};
   resizeTimer.Interval=150;
   view.Resize+=delegate{resizeTimer.Stop();if(!closing)resizeTimer.Start();};
   resizeTimer.Tick+=delegate{resizeTimer.Stop();try{host.Resize(view.ClientSize);}catch(Exception e){notice.Text=e.Message;}};
   timer.Interval=3000;timer.Tick+=async delegate{await RefreshClients();};
   Shown+=async delegate{timer.Start();await RefreshClients();};
   FormClosing+=delegate(object s,FormClosingEventArgs e){closing=true;timer.Stop();resizeTimer.Stop();try{host.RestoreAll();}catch(Exception error){notice.Text=error.Message;e.Cancel=true;closing=false;timer.Start();}};
   FormClosed+=delegate{timer.Dispose();resizeTimer.Dispose();};
  }
  bool TryDetach(bool show){try{host.Detach(show);attachedId=null;notice.Text="Đã tách client; game tiếp tục chạy.";return true;}catch(Exception e){notice.Text=e.Message;return false;}}
  async Task RefreshClients(){
   if(refreshing||closing)return;refreshing=true;
   try{
    var clients=await connection.ReadAsync();if(closing||IsDisposed)return;
    var processes=Process.GetProcessesByName("TheGioiLangLa");HashSet<int> live;
    try{live=new HashSet<int>(processes.Select(p=>p.Id));}finally{foreach(var p in processes)p.Dispose();}
    clients=clients.Where(c=>live.Contains(c.Pid)).ToList();
    // Resolve game identity/window only for newly seen sessions. Refresh does not
    // attach or resize existing games, and never scans their memory.
    reconciling=true;
    try{
     var ids=new HashSet<string>(clients.Select(c=>c.Id));
     foreach(string id in pages.Keys.Where(id=>!ids.Contains(id)).ToArray()){
      if(id==attachedId&&!TryDetach(false))continue;var old=pages[id];tabs.TabPages.Remove(old);old.Dispose();pages.Remove(id);
     }
     foreach(var client in clients){
      TabPage page;
      if(!pages.TryGetValue(client.Id,out page)){if(WindowHost.Find(client.Pid)==IntPtr.Zero)continue;page=new TabPage(client.Name);page.Tag=client;pages.Add(client.Id,page);tabs.TabPages.Add(page);}
      else {var before=(ClientInfo)page.Tag;if(before.Pid!=client.Pid&&attachedId==client.Id&&!TryDetach(false))continue;page.Tag=client;if(page.Text!=client.Name)page.Text=client.Name;}
     }
    }finally{reconciling=false;}
    Text="QLTK • "+pages.Count+" client theo tab";
    if(host.Window==IntPtr.Zero)notice.Text="Chọn tab để xem game ("+pages.Count+" client).";
   }catch(Exception e){if(!closing)notice.Text=e.Message;}finally{refreshing=false;}
  }
  void SelectClient(){
   if(closing||tabs.SelectedTab==null)return;var client=(ClientInfo)tabs.SelectedTab.Tag;
   if(attachedId==client.Id&&host.Window!=IntPtr.Zero)return;
   try{host.SwitchAway();attachedId=null;}catch(Exception e){notice.Text=e.Message;return;}
   try{var window=WindowHost.Find(client.Pid);if(window==IntPtr.Zero)throw new IOException("Client chưa sẵn sàng hoặc đã thoát.");host.Attach(window,view.Handle,view.ClientSize);attachedId=client.Id;notice.Text=client.Name+" • thao tác trực tiếp trong vùng game";}
   catch(Exception e){notice.Text=e.Message;}
  }
 }
}
