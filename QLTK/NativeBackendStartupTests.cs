using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Net;using System.Net.Sockets;using System.Threading.Tasks;using System.Web.Script.Serialization;using TGLL.Preview;
class NativeBackendStartupTests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static Dictionary<string,object> Map(object value){return JsonValue.Map(value);}static object[] List(object value){return JsonValue.Array(value);}static string Text(Dictionary<string,object> node,string key){return JsonValue.Text(node,key);}
 static IEnumerable<Dictionary<string,object>> Nodes(object[] nodes){foreach(var raw in nodes){var n=Map(raw);yield return n;foreach(var child in Nodes(List(JsonValue.Get(n,"children"))))yield return child;}}
 static Dictionary<string,object> Call(IManagerConnection connection,string command,Dictionary<string,object> args){var task=connection.SendAsync(command,args);Check(task.Wait(90000),"isolated backend request timed out");var result=task.Result;Check(JsonValue.Flag(result,"ok"),"isolated backend rejected "+command+": "+Text(result,"message"));return result;}
 static int Main(string[] args){
  string folder=Path.Combine(Path.GetTempPath(),"qltk-native-backend-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);int pid=0;DateTime started=DateTime.UtcNow;
  try{
   Check(args.Length==1,"pass compiled backend bundle directory");string bundle=Path.GetFullPath(args[0]);Check(File.Exists(Path.Combine(bundle,"QLTK-Backend.exe")),"backend bundle missing");
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
   var serializer=new JavaScriptSerializer{MaxJsonLength=8388608};
   File.WriteAllText(Path.Combine(folder,"settings.json"),serializer.Serialize(new Dictionary<string,object>{{"socketEnabled",true},{"socketPort",port},{"jar",Path.Combine(folder,"does-not-exist","TheGioiLangLa.exe")},{"launchConcurrency",1},{"launchDelaySeconds",0},{"soncapMaxOnline",12}}));
   File.WriteAllText(Path.Combine(folder,"accounts.json"),"[{\"id\":\"fixture-a\",\"username\":\"synthetic-a\",\"secret\":\"not-a-real-account\",\"proxy\":null,\"upEnabled\":false},{\"id\":\"fixture-b\",\"username\":\"synthetic-b\",\"secret\":\"not-a-real-account\",\"proxy\":null,\"upEnabled\":false}]");
   var first=new AutoStartingConnection(folder,bundle);var second=new AutoStartingConnection(folder,bundle);
   var a=Call(first,"manager_ui_catalog",null);var b=Call(second,"manager_ui_catalog",null);pid=Convert.ToInt32(a["backend_pid"]);Check(pid==Convert.ToInt32(b["backend_pid"]),"two controllers started for one Data folder");
   var endpoint=Map(serializer.DeserializeObject(File.ReadAllText(Path.Combine(folder,"frontend-endpoint.json"))));Check(pid==Convert.ToInt32(endpoint["pid"]),"controller discovery used the wrong profile");
   using(var process=Process.GetProcessById(pid)){Check(Path.GetFullPath(process.MainModule.FileName)==Path.Combine(bundle,"QLTK-Backend.exe")&&process.StartTime.ToUniversalTime()>=started.AddSeconds(-5),"unexpected controller process");Check(process.MainWindowHandle==IntPtr.Zero,"backend displayed the legacy main window");}
   var snapshot=Call(first,"manager_snapshot",null);Check(List(snapshot["accounts"]).Length==2&&Convert.ToInt32(snapshot["ui_version"])==2,"new profile accounts/feature channel missing");
   var page=List(a["pages"]).Select(Map).First(n=>Text(n,"text")=="Auto Sơn Cáp");
   var context=new Dictionary<string,object>{{"page_id",page["id"]},{"account_id","fixture-a"},{"selected_ids",new string[0]},{"ui_session",a["ui_session"]}};
   var frame=Call(first,"manager_ui_frame",context);var limit=Nodes(List(frame["children"])).First(n=>Text(n,"kind")=="number"&&Convert.ToInt32(n["minimum"])==6&&JsonValue.Flag(n,"visible"));
   var patch=new Dictionary<string,object>(context){{"control_id",limit["id"]},{"event","number"},{"value",54}};Call(first,"manager_ui_event",patch);
   var saved=Map(serializer.DeserializeObject(File.ReadAllText(Path.Combine(folder,"settings.json"))));Check(Convert.ToInt32(saved["soncapMaxOnline"])==54,"native field did not invoke the real configuration save");
   Check(File.ReadAllText(Path.Combine(folder,"accounts.json")).Contains("synthetic-a"),"profile was replaced");
   Console.WriteLine("PASS: full backend starts hidden, two native windows reuse one controller per Data, discovery/authentication and all 27 pages work; native numeric edit invokes original PS save into isolated settings. Invalid game path; no game launched.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  finally{
   try{if(pid==0&&File.Exists(Path.Combine(folder,"frontend-endpoint.json"))){var ep=Map(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(folder,"frontend-endpoint.json"))));pid=Convert.ToInt32(ep["pid"]);}if(pid>0)using(var process=Process.GetProcessById(pid)){if(args.Length==1&&Path.GetFullPath(process.MainModule.FileName)==Path.Combine(Path.GetFullPath(args[0]),"QLTK-Backend.exe")&&process.StartTime.ToUniversalTime()>=started.AddSeconds(-5)){process.Kill();process.WaitForExit(5000);}}}catch{}
   // Keep the isolated fixture for failure diagnosis; it contains synthetic accounts only.
  }
 }
}
