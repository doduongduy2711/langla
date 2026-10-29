using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;using TGLL;using TGLL.Preview;
class NativeUiBridgeTests {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8388608,RecursionLimit=64};
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static Dictionary<string,object> Map(object value){return JsonValue.Map(value);}static object[] List(object value){return JsonValue.Array(value);}static string Text(Dictionary<string,object> node,string key){return JsonValue.Text(node,key);}
 static Dictionary<string,object> Request(NativeUiBridge bridge,Dictionary<string,object> fields){return Map(Json.DeserializeObject(bridge.Handle(Json.Serialize(fields))));}
 static Dictionary<string,object> Context(string page,string account,string session,string command){return new Dictionary<string,object>{{"command",command},{"page_id",page},{"account_id",account},{"ui_session",session},{"selected_ids",new string[0]}};}
 static IEnumerable<Dictionary<string,object>> Nodes(object[] nodes){foreach(var raw in nodes){var n=Map(raw);yield return n;foreach(var child in Nodes(List(JsonValue.Get(n,"children"))))yield return child;}}
 static void Pump(int ms){var time=System.Diagnostics.Stopwatch.StartNew();while(time.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(2);}}
 sealed class FixtureConnection:IManagerConnection {
  readonly Dictionary<string,object> bundle;readonly Dictionary<string,object> catalog;readonly string account;
  public FixtureConnection(string path){bundle=Map(Json.DeserializeObject(File.ReadAllText(path)));catalog=Map(Json.DeserializeObject(Text(bundle,"catalog")));var first=Map(JsonValue.Get(bundle,"frames"));var firstFrame=Map(Json.DeserializeObject(Convert.ToString(first.Values.First())));account=Text(firstFrame,"account_id");}
  public Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> args){
   if(command=="manager_snapshot"){var snapshot=DemoConnection.Snapshot(1);snapshot["ui_version"]=2;Map(List(snapshot["accounts"])[0])["id"]=account;return Task.FromResult(snapshot);}
   if(command=="manager_ui_catalog")return Task.FromResult(catalog);
   if(command=="manager_ui_frame"){var frames=Map(JsonValue.Get(bundle,"frames"));return Task.FromResult(Map(Json.DeserializeObject(Convert.ToString(frames[Text(args,"page_id")]))));}
   return Task.FromResult(new Dictionary<string,object>{{"ok",false},{"message","Read-only fixture"}});
  }
 }
 static void VerifyRealPages(string path,string output){
  var bundle=Map(Json.DeserializeObject(File.ReadAllText(path)));var catalog=Map(Json.DeserializeObject(Text(bundle,"catalog")));var pages=List(JsonValue.Get(catalog,"pages"));Check(pages.Length==27,"original manager page count changed");var frames=Map(JsonValue.Get(bundle,"frames"));int count=0,buttons=0;var kinds=new HashSet<string>();
  foreach(var raw in pages){var page=Map(raw);var frame=Map(Json.DeserializeObject(Convert.ToString(frames[Text(page,"id")])));Check(JsonValue.Flag(frame,"ok"),"real page failed to export: "+Text(page,"text"));foreach(var node in Nodes(List(JsonValue.Get(frame,"children")))){count++;kinds.Add(Text(node,"kind"));if(Text(node,"kind")=="button")buttons++;}}
  Check(buttons>70&&count>250,"missing original controls/actions");
  using(var form=new ManagerForm(new FixtureConnection(path),false)){
   form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-15000,-15000);form.ShowInTaskbar=false;form.Show();Pump(100);Check(form.FeaturePageCount==27,"native window missing function tabs");
   TabControl native=form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single();
   foreach(TabPage page in native.TabPages){native.SelectedTab=page;Pump(15);var panel=page.Tag as RemoteFeaturePanel;if(panel!=null)Check(panel.Controls.Count>0,"native controls missing: "+page.Text);}
   native.SelectedTab=native.TabPages.Cast<TabPage>().First(p=>p.Text=="Auto Sơn Cáp");Pump(30);
   if(output.Length>0)using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(output);}
   form.Close();
  }
  Console.WriteLine("PASS: all "+pages.Length+" real manager pages rendered in native C#: "+count+" controls, "+buttons+" original action buttons, widget kinds="+String.Join(",",kinds));
 }
 [STAThread]static int Main(string[] args){try{
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  using(var root=new Form{ClientSize=new Size(850,700)})using(var tabs=new TabControl{Dock=DockStyle.Bottom,Height=500})using(var accounts=new DataGridView{AllowUserToAddRows=false}){
   root.Controls.Add(tabs);root.Controls.Add(accounts);accounts.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick"});accounts.Columns.Add(new DataGridViewCheckBoxColumn{Name="auto"});accounts.Columns.Add(new DataGridViewCheckBoxColumn{Name="up"});accounts.Rows.Add(false,false,false);accounts.Rows.Add(false,false,false);
   var page=new TabPage("Functions");tabs.TabPages.Add(page);var input=new TextBox{Text="168",Location=new Point(10,10)};var checkbox=new CheckBox{Text="Enable",Location=new Point(10,40)};var combo=new ComboBox{Location=new Point(10,70),DropDownStyle=ComboBoxStyle.DropDownList};combo.Items.AddRange(new object[]{"one","two"});combo.SelectedIndex=0;
   var masked=new TextBox{Text="NEVER-SERIALIZE-SECRET",UseSystemPasswordChar=true,Location=new Point(10,100)};var run=new Button{Text="Run",Location=new Point(10,130)};var number=new NumericUpDown{Minimum=1,Maximum=60,Value=12,Location=new Point(10,160)};
   var data=new DataGridView{AllowUserToAddRows=false,Location=new Point(10,200),Size=new Size(500,240)};data.Columns.Add("id","ID");data.Columns.Add("price","Giá");for(int n=0;n<5000;n++)data.Rows.Add(n,100+n);page.Controls.AddRange(new Control[]{input,checkbox,combo,masked,run,number,data});
   string saved="",clicked="";int selections=0;input.Leave+=delegate{saved=(accounts.Rows[0].Selected?"a":"b")+":"+input.Text;};combo.SelectionChangeCommitted+=delegate{selections++;};run.Click+=delegate{clicked=accounts.Rows[0].Selected?"a":"b";};
   var bridge=new NativeUiBridge(root,tabs,accounts,()=>new[]{"a","b"},()=>"fixture");var catalog=Request(bridge,new Dictionary<string,object>{{"command","manager_ui_catalog"}});string session=Text(catalog,"ui_session"),id=Text(Map(List(catalog["pages"])[1]),"id");
   var frame=Request(bridge,Context(id,"a",session,"manager_ui_frame"));Check(JsonValue.Flag(frame,"ok"),"hidden backend frame not available");string raw=Json.Serialize(frame);Check(!raw.Contains("NEVER-SERIALIZE-SECRET"),"masked secret leaked into frame");var nodes=Nodes(List(frame["children"])).ToArray();Check(nodes.All(n=>JsonValue.Flag(n,"visible")),"hidden backend made its widgets invisible");
   var text=nodes.First(n=>Text(n,"kind")=="text"&&Text(n,"text")=="168");var change=Context(id,"a",session,"manager_ui_event");change["control_id"]=Text(text,"id");change["event"]="text";change["value"]="1276\r\n168";Check(JsonValue.Flag(Request(bridge,change),"ok")&&saved=="a:1276\r\n168","text was not committed through original Leave handler");
   var choice=nodes.First(n=>Text(n,"kind")=="combo");change=Context(id,"a",session,"manager_ui_event");change["control_id"]=Text(choice,"id");change["event"]="select";change["items_key"]=Text(choice,"items_key");change["index"]=1;Check(JsonValue.Flag(Request(bridge,change),"ok")&&selections==1&&combo.SelectedIndex==1,"game option commit event did not fire");change["items_key"]="stale";Check(!JsonValue.Flag(Request(bridge,change),"ok")&&selections==1,"stale option index accepted");
   var grid=nodes.First(n=>Text(n,"kind")=="grid");var view=Context(id,"a",session,"manager_ui_frame");view["grid_views"]=new Dictionary<string,object>{{Text(grid,"id"),new Dictionary<string,object>{{"first",1200},{"count",50}}}};frame=Request(bridge,view);grid=Nodes(List(frame["children"])).First(n=>Text(n,"kind")=="grid");var rows=List(grid["rows"]);Check(rows.Length<100&&Convert.ToInt32(grid["row_count"])==5000&&Convert.ToInt32(Map(rows[0])["index"])==1200,"grid sent whole dataset or lost scroll window");
   var row=Map(rows[0]);change=Context(id,"a",session,"manager_ui_event");change["control_id"]=Text(grid,"id");change["event"]="grid_select";change["epoch"]=grid["epoch"];change["row"]=1200;change["column"]=0;change["row_key"]=row["key"];data.Rows[1200].Cells[1].Value=99999;Check(!JsonValue.Flag(Request(bridge,change),"ok"),"stale price/row allowed an action");
   var button=nodes.First(n=>Text(n,"kind")=="button");change=Context(id,"a",session,"manager_ui_event");change["control_id"]=Text(button,"id");change["event"]="click";var queued=Request(bridge,change);Check(JsonValue.Flag(queued,"ok")&&JsonValue.Flag(queued,"queued"),"button not queued");Check(!JsonValue.Flag(Request(bridge,change),"ok"),"button queued twice before completion");Request(bridge,Context(id,"b",session,"manager_ui_frame"));Pump(50);Check(clicked=="a","queued button ran against newly selected account");
   change["ui_session"]="old-session";Check(!JsonValue.Flag(Request(bridge,change),"ok"),"stale backend session allowed action");
   var flag=new Dictionary<string,object>{{"command","manager_ui_flags"},{"account_id","b"},{"column","auto"},{"value",true}};Check(JsonValue.Flag(Request(bridge,flag),"ok")&&Object.Equals(accounts.Rows[1].Cells["auto"].Value,true),"native account auto checkbox not applied");
   Request(bridge,Context(id,"a",session,"manager_ui_frame"));using(var modal=new Form{Text="isolated modal",StartPosition=FormStartPosition.Manual,Location=new Point(-15000,-15000),ShowInTaskbar=false})using(var timer=new System.Windows.Forms.Timer{Interval=30}){timer.Tick+=delegate{timer.Stop();try{var blocked=Request(bridge,Context(id,"b",session,"manager_ui_frame"));Check(JsonValue.Flag(blocked,"modal")&&accounts.Rows[0].Selected,"modal changed the active account");flag["value"]=false;Check(!JsonValue.Flag(Request(bridge,flag),"ok"),"account flag changed while modal was open");}finally{modal.Close();}};timer.Start();modal.ShowDialog(root);}
   Console.WriteLine("PASS: hidden host visibility, masked fields, original text/option events, dataset-window paging, stale price guard, queued account context, button deduplication, restart guard and Auto flag. No game operations.");
  }
  if(args.Length>=2&&args[0]=="--real-pages")VerifyRealPages(args[1],args.Length>=3?args[2]:"");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
