using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.IO;using System.Linq;using System.Net;using System.Net.Sockets;using System.Text;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;using TGLL.Preview;
class NativePreviewTests {
 sealed class WaitingConnection:IManagerConnection {
  public readonly TaskCompletionSource<Dictionary<string,object>> Poll=new TaskCompletionSource<Dictionary<string,object>>(),Action=new TaskCompletionSource<Dictionary<string,object>>();public int Polls,Actions;
  public Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> args){if(command=="manager_snapshot"){Polls++;return Poll.Task;}Actions++;return Action.Task;}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Pump(int ms){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}
 static void Transport(){
  string folder=Path.Combine(Path.GetTempPath(),"qltk-preview-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
  try{
   int port=((IPEndPoint)listener.LocalEndpoint).Port;File.WriteAllText(Path.Combine(folder,"settings.json"),"{\"socketPort\":"+port+",\"socketEnabled\":true}");File.WriteAllText(Path.Combine(folder,"api-token.json"),"{\"secret\":\"synthetic-token\"}");
   var server=Task.Run(delegate{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){var input=new StreamReader(stream,Encoding.UTF8,false,1024,true);var message=new JavaScriptSerializer().DeserializeObject(input.ReadLine()) as Dictionary<string,object>;Check(JsonValue.Text(message,"token")=="synthetic-token"&&JsonValue.Text(message,"command")=="manager_snapshot","missing local authentication");byte[] reply=Encoding.UTF8.GetBytes("{\"ok\":true,\"accounts\":[]}\n");stream.Write(reply,0,reply.Length);}});
   var response=new ManagerConnection(folder).SendAsync("manager_snapshot",null);Check(response.Wait(5000)&&JsonValue.Flag(response.Result,"ok"),"transport failed");Check(server.Wait(5000),"transport fixture stalled");
  }finally{listener.Stop();Directory.Delete(folder,true);}
 }
 [STAThread]static int Main(string[] args){
  try{
   if(args.Length==2&&args[0]=="--probe-data"){
    var response=new ManagerConnection(args[1]).SendAsync("list",null);Check(response.Wait(27000)&&JsonValue.Flag(response.Result,"ok"),"read-only live connection failed");
    Console.WriteLine("PASS: live manager list connected, accounts="+JsonValue.Array(JsonValue.Get(response.Result,"accounts")).Length+". Read-only; no game commands.");return 0;
   }
   Transport();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Check(!typeof(ManagerForm).Assembly.GetReferencedAssemblies().Any(a=>a.Name=="System.Management.Automation"),"native form embeds PowerShell");
   var service=new WaitingConnection();using(var form=new ManagerForm(service,false)){
    form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-15000,-15000);form.ShowInTaskbar=false;form.Show();
    int beats=0;long last=0,maxGap=0;var clock=Stopwatch.StartNew();using(var timer=new System.Windows.Forms.Timer{Interval=20}){
     timer.Tick+=delegate{long now=clock.ElapsedMilliseconds;if(last!=0)maxGap=Math.Max(maxGap,now-last);last=now;beats++;};timer.Start();
     Pump(3300);Check(service.Polls==1&&beats>=60,"UI blocked or repeated overlapping poll during backend wait");
     service.Poll.SetResult(DemoConnection.Snapshot(5000));Pump(250);Check(form.AccountGrid.RowCount==5000&&form.PollCount==1,"large snapshot missing");
     form.AccountGrid.FirstDisplayedScrollingRowIndex=1000;int first=form.AccountGrid.FirstDisplayedScrollingRowIndex;
     var snapshot=DemoConnection.Snapshot(5000);var rows=JsonValue.Array(JsonValue.Get(snapshot,"accounts"));JsonValue.Map(rows[1000])["status"]="Đã chuyển hoạt động";
     form.ApplySnapshot(snapshot);Check(form.ChangedRows==1&&form.AccountGrid.FirstDisplayedScrollingRowIndex==first,"snapshot rebuilt/scrolled entire grid");
     int changed=form.ChangedRows;form.ApplySnapshot(snapshot);Check(form.ChangedRows==changed,"unchanged snapshot repainted rows");
     form.RunAction("auto_all_start",new string[0]);form.RunAction("auto_all_start",new string[0]);int before=beats;Pump(1600);
     Check(service.Actions==1&&form.ActionInProgress&&beats-before>=35,"command blocked UI or duplicated operation");
     service.Action.SetResult(new Dictionary<string,object>{{"ok",false},{"error","response_timeout"}});Pump(80);Check(!form.ActionInProgress&&service.Actions==1&&form.Notice.Contains("chưa có xác nhận"),"unknown outcome retried or hidden by status refresh");
     Check(maxGap<700,"message thread starved during background command");timer.Stop();
     var demo=new ManagerForm(new DemoConnection(),true);using(demo){demo.StartPosition=FormStartPosition.Manual;demo.Location=new Point(-15000,-15000);demo.ShowInTaskbar=false;demo.Show();Pump(100);if(args.Length==2&&args[0]=="--image"){using(var image=new Bitmap(demo.Width,demo.Height)){demo.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(args[1]);}}demo.Close();}
     Console.WriteLine("PASS: native .NET 4.8 form, authenticated loopback transport, 5000 virtual rows, changed-row-only paint, scroll preservation, UI heartbeat during blocked poll/command, one in-flight poll and no duplicate/retried mutation. heartbeat="+beats+", max_gap_ms="+maxGap+". No real games or account writes.");
    }form.Close();
   }return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
