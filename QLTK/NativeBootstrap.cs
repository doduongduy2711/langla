using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Net;using System.Net.Sockets;using System.Security.Cryptography;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace TGLL.Preview {
 public sealed class AutoStartingConnection:IManagerConnection {
  volatile Task<ManagerConnection> ready;readonly object retryGate=new object();readonly string dataDirectory,programDirectory;public volatile string Status="Đang tìm bộ điều phối của thư mục Data...";
  public AutoStartingConnection(string dataDirectory,string programDirectory){this.dataDirectory=dataDirectory;this.programDirectory=programDirectory;ready=Task.Run(()=>Connect(dataDirectory,programDirectory));}
  public void RetryIfFailed(){lock(retryGate)if(ready.IsFaulted||ready.IsCanceled){Status="Đang thử kết nối lại bộ điều phối...";ready=Task.Run(()=>Connect(dataDirectory,programDirectory));}}
  public string DataDirectory{get{return dataDirectory;}}
  public bool Starting{get{return !ready.IsCompleted;}}
  public async Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> arguments){var client=await ready;return await client.SendAsync(command,arguments);}
  static Dictionary<string,object> Read(string path){try{return new JavaScriptSerializer{MaxJsonLength=8388608}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;}catch{return null;}}
  static bool Listening(int port){if(port<1||port>65535)return false;try{using(var client=new TcpClient()){var connect=client.ConnectAsync(IPAddress.Loopback,port);return connect.Wait(500)&&client.Connected;}}catch{return false;}}
  static int Port(Dictionary<string,object> map,int fallback){int value;return Int32.TryParse(JsonValue.Text(map,"port"),out value)&&value>0&&value<=65535?value:fallback;}
  static int ConfiguredPort(string data){int value;return Int32.TryParse(JsonValue.Text(Read(Path.Combine(data,"settings.json")),"socketPort"),out value)&&value>0&&value<=65535?value:47651;}
  static int EndpointPort(string data){return Port(Read(Path.Combine(data,"frontend-endpoint.json")),ConfiguredPort(data));}
  static string DataKey(string data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).Replace("-","");}
  static bool LegacyOwner(string data){string parent=Directory.GetParent(data).FullName;foreach(string name in new[]{"QLTK","QLTK-net48","QLTK-Backend"})foreach(var process in Process.GetProcessesByName(name))using(process){try{string folder=Path.GetDirectoryName(process.MainModule.FileName);if(String.Equals(folder,parent,StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}}return false;}
  ManagerConnection Existing(string data,int port){
   if(!Listening(port)||!File.Exists(Path.Combine(data,"api-token.json")))return null;
   var client=new ManagerConnection(data,port);try{var ping=client.SendAsync("ping",null).GetAwaiter().GetResult();if(JsonValue.Flag(ping,"ok")||JsonValue.Text(ping,"error")=="response_timeout")return client;return null;}catch{return client;}
  }
  ManagerConnection Connect(string directory,string program){
   string data=Path.GetFullPath(directory);if(!Directory.Exists(data))throw new IOException("Thư mục Data không tồn tại.");
   string key=DataKey(data);using(var startup=new Mutex(false,"Local\\TGLL.Start."+key)){
    bool owns=false;try{
     try{owns=startup.WaitOne(60000);}catch(AbandonedMutexException){owns=true;}if(!owns)throw new IOException("Bộ điều phối đang khởi động ở cửa sổ khác.");
     int port=EndpointPort(data);var existing=Existing(data,port);if(existing!=null){Status="Đã kết nối bộ điều phối đang chạy.";return existing;}
     using(var owner=new Mutex(false,"Local\\TGLL.Manager."+key)){
      bool available;try{available=owner.WaitOne(0);}catch(AbandonedMutexException){available=true;}
      if(!available){Status="Đợi bộ điều phối hiện tại khởi động...";return Wait(data,null);}owner.ReleaseMutex();
     }
     if(LegacyOwner(data))throw new IOException("QLTK cũ đang chạy nhưng chưa có kết nối. Bật Socket trong QLTK cũ hoặc mở lại sau khi cập nhật.");
     string host=Path.Combine(program,"QLTK-Backend.exe");if(!File.Exists(host))throw new IOException("Chưa có QLTK-Backend.exe; giải nén đầy đủ gói C#.");
     if(Listening(port)){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();}
     Status="Đang nạp bộ điều phối và các chức năng...";
     var info=new ProcessStartInfo(host,"--backend-only --data-dir \""+data+"\" --socket-port "+port){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=program};
     using(var child=Process.Start(info)){return Wait(data,child);}
    }catch{Status="Chưa kết nối được bộ điều phối. Kiểm tra thư mục Data hoặc mở lại QLTK cũ sau cập nhật.";throw;}
    finally{if(owns)startup.ReleaseMutex();}
   }
  }
  ManagerConnection Wait(string data,Process child){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<60000){var client=Existing(data,EndpointPort(data));if(client!=null){Status="Đã nạp đầy đủ bộ điều phối.";return client;}if(child!=null&&child.HasExited)throw new IOException("Bộ điều phối không khởi động được.");Thread.Sleep(250);}throw new IOException("Chưa thấy kết nối từ bộ điều phối. Không mở thêm phiên khác.");}
 }
}

namespace TGLL.Preview {
 public static class FrontendUpdate {
  public const int Version=291;
  public static async Task<string> Download(){
   return await Task.Run(()=>{
    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
    using(var web=new WebClient()){
     web.Headers["User-Agent"]="QLTK-CSharp-Updater";
     var json=new JavaScriptSerializer{MaxJsonLength=8388608};
     var release=JsonValue.Map(json.DeserializeObject(web.DownloadString("https://api.github.com/repos/doduongduy2711/langla/releases/latest")));
     int version;if(!Int32.TryParse(JsonValue.Text(release,"tag_name").TrimStart('v'),out version)||version<=Version)return null;
     var asset=System.Linq.Enumerable.FirstOrDefault(JsonValue.Array(JsonValue.Get(release,"assets")),x=>JsonValue.Text(JsonValue.Map(x),"name")=="QLTK-CSharp-Preview.zip");
     if(asset==null)throw new IOException("Bản phát hành chưa có gói C#.");var map=JsonValue.Map(asset);string url=JsonValue.Text(map,"browser_download_url"),digest=JsonValue.Text(map,"digest");
     if(url!="https://github.com/doduongduy2711/langla/releases/download/v"+version+"/QLTK-CSharp-Preview.zip"||!digest.StartsWith("sha256:"))throw new IOException("Không xác minh được gói cập nhật.");
     string stage=Path.Combine(Path.GetTempPath(),"QLTK-CSharp-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);string zip=Path.Combine(stage,"update.zip");web.DownloadFile(url,zip);
     using(var sha=SHA256.Create())using(var input=File.OpenRead(zip))if("sha256:"+BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=digest.ToLowerInvariant())throw new IOException("Mã kiểm tra gói không khớp.");
     // Extract only the frontend: never replace Data, controller, game files or sources in use.
     using(var archive=System.IO.Compression.ZipFile.OpenRead(zip))foreach(string name in new[]{"QLTK-CSharp-Preview.exe","QLTK-CSharp-Preview.exe.config"}){
      var entry=archive.GetEntry(name);if(entry==null||entry.Length>16777216)throw new IOException("Gói C# thiếu file hoặc quá lớn.");System.IO.Compression.ZipFileExtensions.ExtractToFile(entry,Path.Combine(stage,name));
     }
     return stage;
    }
   });
  }
  static string Quote(string s){if(s.Contains("\"")||s.EndsWith("\\"))throw new IOException("Đường dẫn không hợp lệ.");return "\""+s+"\"";}
  public static void Start(string stage,string data){
   string current=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
   string helper=Path.Combine(stage,"apply.exe");File.Copy(current,helper);File.Copy(current+".config",helper+".config");
   var info=new ProcessStartInfo(helper,"--apply-update "+Process.GetCurrentProcess().Id+" "+Quote(current)+" "+Quote(stage)+" "+Quote(data??"")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};Process.Start(info);
  }
  public static int Apply(string[] args){
   string backup=null,target=null;bool replaced=false;
   try{
    if(args.Length!=5)return 2;int pid=Int32.Parse(args[1]);target=Path.GetFullPath(args[2]);string stage=Path.GetFullPath(args[3]);
    if(!String.Equals(stage,AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("Sai thư mục cập nhật.");
    Process old=null;try{old=Process.GetProcessById(pid);}catch(ArgumentException){}if(old!=null)using(var process=old){if(!String.Equals(process.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase))throw new IOException("Sai cửa sổ cần cập nhật.");if(!process.WaitForExit(30000))throw new IOException("Cửa sổ cũ chưa đóng.");}
    backup=target+".before-update";File.Copy(target,backup,true);File.Copy(Path.Combine(stage,"QLTK-CSharp-Preview.exe"),target,true);replaced=true;
    File.Copy(Path.Combine(stage,"QLTK-CSharp-Preview.exe.config"),target+".config",true);
    Process.Start(new ProcessStartInfo(target,"--data "+Quote(args[4])){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target)});return 0;
   }catch(Exception e){if(replaced&&backup!=null)try{File.Copy(backup,target,true);}catch{}System.Windows.Forms.MessageBox.Show("Chưa cập nhật giao diện: "+e.Message,"QLTK");return 1;}
  }
 }
}
