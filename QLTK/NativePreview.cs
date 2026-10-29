using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]
namespace TGLL.Preview {
 public interface IManagerConnection {
  Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> arguments);
 }
 public static class JsonValue {
  public static object Get(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value:null;}
  public static string Text(Dictionary<string,object> data,string key){return Convert.ToString(Get(data,key),System.Globalization.CultureInfo.InvariantCulture);}
  public static bool Flag(Dictionary<string,object> data,string key){return Object.Equals(Get(data,key),true);}
  public static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>;}
  public static object[] Array(object value){return value as object[]??new object[0];}
 }
 public sealed class ManagerConnection:IManagerConnection {
  readonly string token;readonly int port;
  public ManagerConnection(string dataDirectory){
   string root=Path.GetFullPath(dataDirectory);var json=new JavaScriptSerializer{MaxJsonLength=8388608};
   var settings=json.DeserializeObject(File.ReadAllText(Path.Combine(root,"settings.json"))) as Dictionary<string,object>;
   port=Convert.ToInt32(JsonValue.Get(settings,"socketPort")??47651);
   if(port<1||port>65535||Object.Equals(JsonValue.Get(settings,"socketEnabled"),false))throw new InvalidOperationException("Bật Socket trong QLTK hiện tại trước khi kết nối bản thử.");
   var saved=json.DeserializeObject(File.ReadAllText(Path.Combine(root,"api-token.json"))) as Dictionary<string,object>;
   token=JsonValue.Text(saved,"secret");
   if(token.StartsWith("AQAAANCM",StringComparison.Ordinal)){
    byte[] bytes=ProtectedData.Unprotect(Convert.FromBase64String(token),null,DataProtectionScope.CurrentUser);
    try{token=Encoding.UTF8.GetString(bytes);}finally{System.Array.Clear(bytes,0,bytes.Length);}
   }
   if(String.IsNullOrEmpty(token))throw new InvalidOperationException("Chưa có mã kết nối nội bộ. Mở QLTK hiện tại trước.");
  }
  public Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> arguments){
   // Every socket/read/JSON operation runs outside the WinForms message thread.
   return Task.Run(delegate{
    var request=arguments==null?new Dictionary<string,object>():new Dictionary<string,object>(arguments);
    request["token"]=token;request["command"]=command;
    var json=new JavaScriptSerializer{MaxJsonLength=8388608,RecursionLimit=64};
    byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(request)+"\n");
    if(bytes.Length>32768)throw new InvalidOperationException("Chọn tối đa 200 tài khoản cho mỗi thao tác.");
    using(var client=new TcpClient()){
     var connecting=client.ConnectAsync(IPAddress.Loopback,port);
     if(!connecting.Wait(3000))throw new IOException("Không kết nối được QLTK hiện tại.");
     client.ReceiveTimeout=24000;client.SendTimeout=3000;
     using(var stream=client.GetStream()){
      stream.Write(bytes,0,bytes.Length);System.Array.Clear(bytes,0,bytes.Length);
      using(var response=new MemoryStream()){
       int b;while((b=stream.ReadByte())>=0&&b!=10){if(response.Length>=8388608)throw new IOException("Dữ liệu trạng thái vượt giới hạn.");response.WriteByte((byte)b);}
       if(b!=10)throw new IOException("Kết nối bị ngắt trước khi nhận đủ trạng thái.");
       var result=json.DeserializeObject(Encoding.UTF8.GetString(response.ToArray())) as Dictionary<string,object>;
       if(result==null)throw new IOException("Phản hồi trạng thái không hợp lệ.");return result;
      }
     }
    }
   });
  }
 }
 public sealed class AccountRow {
  public string Id,Username,Character,Zone,Bima,Proxy,Status,Job,Visibility;public bool Auto,Up;public int Pid;
  public string Fingerprint {get{return String.Join("\u001f",new[]{Id,Username,Character,Zone,Bima,Proxy,Status,Job,Visibility,Auto.ToString(),Up.ToString(),Pid.ToString()});}}
  public static AccountRow Parse(Dictionary<string,object> data){
   int pid;Int32.TryParse(JsonValue.Text(data,"pid"),out pid);
   string proxy=JsonValue.Text(data,"proxy");var p=JsonValue.Map(JsonValue.Get(data,"proxy"));if(p!=null)proxy=JsonValue.Text(p,"host")+":"+JsonValue.Text(p,"port");
   return new AccountRow{Id=JsonValue.Text(data,"id"),Username=JsonValue.Text(data,"username"),Character=JsonValue.Text(data,"character"),Zone=JsonValue.Text(data,"zone"),Bima=JsonValue.Text(data,"bima"),Proxy=proxy,Status=JsonValue.Text(data,"status"),Job=JsonValue.Text(data,"job_id"),Visibility=JsonValue.Text(data,"visibility"),Auto=JsonValue.Flag(data,"auto"),Up=JsonValue.Flag(data,"up"),Pid=pid};
  }
 }
 public sealed class BufferedGrid:DataGridView {public BufferedGrid(){DoubleBuffered=true;}}
 public sealed class ManagerForm:Form {
  readonly IManagerConnection connection;readonly bool demo;
  readonly BufferedGrid grid=new BufferedGrid();readonly HashSet<string> picked=new HashSet<string>(StringComparer.Ordinal);
  readonly List<AccountRow> accounts=new List<AccountRow>();List<AccountRow> visible=new List<AccountRow>();
  readonly Label summary=new Label(),notice=new Label(),activity=new Label(),details=new Label();
  readonly TextBox search=new TextBox();readonly CheckBox selectedOnly=new CheckBox();
  readonly Button open=new Button(),close=new Button(),show=new Button(),hide=new Button(),all=new Button(),stop=new Button();
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
  bool polling,acting,capable,closing,commandUncertain;DateTime capabilityRetry=DateTime.MinValue,lastSuccess=DateTime.MinValue;
  public int PollCount,ChangedRows,HeartbeatCount;public bool ActionInProgress{get{return acting;}}
  public DataGridView AccountGrid{get{return grid;}}public string Notice{get{return notice.Text;}}
  public ManagerForm(IManagerConnection service,bool demonstration){
   connection=service;demo=demonstration;
   Text="QLTK — C# WinForms thử nghiệm";Font=new Font("Segoe UI",9);ClientSize=new Size(1120,700);MinimumSize=new Size(850,500);StartPosition=FormStartPosition.CenterScreen;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(8)};
   layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(float height in new[]{48,32,28})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,110));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(layout);
   var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};layout.Controls.Add(tools,0,0);
   AddButton(tools,"Chọn tất cả",delegate{foreach(var row in accounts)picked.Add(row.Id);grid.InvalidateColumn(1);UpdateSummary();},105);
   AddButton(tools,"Bỏ chọn",delegate{picked.Clear();grid.InvalidateColumn(1);UpdateSummary();},82);
   SetupButton(tools,open,"Mở đã chọn",delegate{RunSelected("open_selected");},100);
   SetupButton(tools,close,"Tắt đã chọn",delegate{RunSelected("close_selected");},100);
   SetupButton(tools,show,"Hiện",delegate{RunSelected("show_selected");},66);
   SetupButton(tools,hide,"Ẩn",delegate{RunSelected("hide_selected");},66);
   SetupButton(tools,all,"Auto tất cả",delegate{RunAction("auto_all_start",new string[0]);},110);
   SetupButton(tools,stop,"Dừng Auto tất cả",delegate{RunAction("auto_all_stop",new string[0]);},135);
   AddButton(tools,"Đồng bộ",delegate{PollNow();},85);
   var filtering=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};filtering.Controls.Add(new Label{Text="Tìm tài khoản / nhân vật:",AutoSize=true,Padding=new Padding(0,5,0,0)});search.Width=230;filtering.Controls.Add(search);selectedOnly.Text="Chỉ acc đã chọn";selectedOnly.AutoSize=true;filtering.Controls.Add(selectedOnly);summary.AutoSize=true;summary.Padding=new Padding(14,5,0,0);filtering.Controls.Add(summary);layout.Controls.Add(filtering,0,1);
   activity.Dock=DockStyle.Fill;activity.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(activity,0,2);
   grid.Dock=DockStyle.Fill;grid.VirtualMode=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.FixedSingle;grid.RowTemplate.Height=24;
   string[] names={"#","Chọn","Auto","Up","Tài khoản","Nhân vật","Khu","Bí ma","Proxy","Trạng thái","PID","Ẩn/Hiện"};int[] widths={42,48,48,42,130,120,52,66,190,260,68,70};
   for(int n=0;n<names.Length;n++){DataGridViewColumn column=n>=1&&n<=3?(DataGridViewColumn)new DataGridViewCheckBoxColumn():new DataGridViewTextBoxColumn();column.HeaderText=names[n];column.Width=widths[n];column.ReadOnly=n!=1;column.SortMode=DataGridViewColumnSortMode.NotSortable;grid.Columns.Add(column);}
   grid.CellValueNeeded+=CellNeeded;grid.CellValuePushed+=CellPushed;grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.SelectionChanged+=delegate{ShowDetails();};grid.DataError+=delegate(object sender,DataGridViewDataErrorEventArgs e){e.ThrowException=false;};layout.Controls.Add(grid,0,3);
   var info=new GroupBox{Text="Tài khoản đang chọn",Dock=DockStyle.Fill};details.Dock=DockStyle.Fill;details.Padding=new Padding(8);info.Controls.Add(details);layout.Controls.Add(info,0,4);
   notice.Dock=DockStyle.Fill;notice.ForeColor=Color.FromArgb(30,70,120);layout.Controls.Add(notice,0,5);
   search.TextChanged+=delegate{ApplyFilter();};selectedOnly.CheckedChanged+=delegate{ApplyFilter();};
   timer.Interval=3000;timer.Tick+=delegate{PollNow();};Shown+=delegate{PollNow();timer.Start();};FormClosing+=delegate{closing=true;timer.Stop();};FormClosed+=delegate{timer.Dispose();};UpdateButtons();
   if(demo)notice.Text="DỮ LIỆU MẪU — không điều khiển hoặc mở game.";
  }
  static void SetupButton(Control parent,Button button,string text,Action action,int width){button.Text=text;button.Width=width;button.Height=32;button.Click+=delegate{action();};parent.Controls.Add(button);}
  static void AddButton(Control parent,string text,Action action,int width){SetupButton(parent,new Button(),text,action,width);}
  void CellNeeded(object sender,DataGridViewCellValueEventArgs e){if(e.RowIndex<0||e.RowIndex>=visible.Count)return;var r=visible[e.RowIndex];object[] cells={e.RowIndex+1,picked.Contains(r.Id),r.Auto,r.Up,r.Username,r.Character,r.Zone,r.Bima,r.Proxy,r.Status,r.Pid>0?r.Pid.ToString():"—",r.Visibility};e.Value=cells[e.ColumnIndex];}
  void CellPushed(object sender,DataGridViewCellValueEventArgs e){if(e.ColumnIndex!=1||e.RowIndex<0||e.RowIndex>=visible.Count)return;string id=visible[e.RowIndex].Id;if(Object.Equals(e.Value,true))picked.Add(id);else picked.Remove(id);UpdateSummary();if(selectedOnly.Checked)BeginInvoke((Action)ApplyFilter);}
  void ApplyFilter(){string query=search.Text.Trim();visible=accounts.Where(r=>(!selectedOnly.Checked||picked.Contains(r.Id))&&(query.Length==0||r.Username.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||r.Character.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();grid.RowCount=visible.Count;grid.Invalidate();UpdateSummary();ShowDetails();}
  void UpdateSummary(){summary.Text="Tài khoản: "+accounts.Count+"   •   Đã chọn: "+picked.Count+"   •   Hiển thị: "+visible.Count;}
  void ShowDetails(){if(grid.CurrentCell==null||grid.CurrentCell.RowIndex>=visible.Count){details.Text="Chọn một tài khoản trong bảng.";return;}var r=visible[grid.CurrentCell.RowIndex];details.Text="Tài khoản: "+r.Username+"    •    Nhân vật: "+r.Character+"    •    Khu: "+r.Zone+"\r\nTrạng thái: "+r.Status+"\r\nProxy: "+r.Proxy+"    •    Bí ma: "+r.Bima+"    •    PID: "+(r.Pid>0?r.Pid.ToString():"—");}
  void UpdateButtons(){open.Enabled=close.Enabled=show.Enabled=hide.Enabled=all.Enabled=stop.Enabled=!acting&&!demo&&capable;}
  public void ApplySnapshot(Dictionary<string,object> response){
   var next=JsonValue.Array(JsonValue.Get(response,"accounts")).Select(JsonValue.Map).Where(m=>m!=null).Select(AccountRow.Parse).Where(r=>r.Id.Length>0).ToList();
   if(next.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=next.Count)throw new InvalidDataException("Danh sách trạng thái bị trùng tài khoản.");
   bool same=accounts.Count==next.Count;for(int n=0;same&&n<accounts.Count;n++)if(accounts[n].Id!=next[n].Id)same=false;
   if(same){var positions=new Dictionary<string,int>(StringComparer.Ordinal);for(int n=0;n<visible.Count;n++)positions[visible[n].Id]=n;for(int n=0;n<accounts.Count;n++){if(accounts[n].Fingerprint==next[n].Fingerprint)continue;accounts[n]=next[n];int at;if(positions.TryGetValue(next[n].Id,out at)){visible[at]=next[n];grid.InvalidateRow(at);ChangedRows++;}}if(search.TextLength>0)ApplyFilter();else ShowDetails();}
   else{accounts.Clear();accounts.AddRange(next);var ids=new HashSet<string>(next.Select(r=>r.Id),StringComparer.Ordinal);picked.IntersectWith(ids);ApplyFilter();}
   if(JsonValue.Get(response,"preview_version")!=null){capable=true;activity.Text=JsonValue.Text(response,"activity");}
   else activity.Text="Đang xem danh sách từ bộ điều phối cũ. Có thể cuộn/tìm kiếm; cập nhật QLTK lên v289 rồi mở lại để bật kênh C#.";
   lastSuccess=DateTime.Now;UpdateButtons();UpdateSummary();
  }
  public async void PollNow(){
   if(polling||closing||connection==null)return;polling=true;
   try{
    Dictionary<string,object> response;
    if(capable||DateTime.UtcNow>=capabilityRetry){response=await connection.SendAsync("manager_snapshot",null);if(!JsonValue.Flag(response,"ok")&&JsonValue.Text(response,"error")=="unknown_command"){capable=false;capabilityRetry=DateTime.UtcNow.AddSeconds(30);response=await connection.SendAsync("list",null);}}
    else response=await connection.SendAsync("list",null);
    if(closing)return;if(!JsonValue.Flag(response,"ok"))throw new IOException("Bộ điều phối đang bận hoặc chưa phản hồi; giữ trạng thái đã nhận, không tự gửi lại thao tác.");
    ApplySnapshot(response);PollCount++;if(!acting&&!demo)notice.Text=commandUncertain?"Đã đồng bộ trạng thái; lệnh trước chưa có xác nhận. Kiểm tra trước khi gửi lại, bản thử không tự gửi lại lệnh.":"Đồng bộ lúc "+lastSuccess.ToString("HH:mm:ss")+". Cấu hình các hoạt động vẫn dùng cửa sổ QLTK hiện tại.";
   }catch(Exception){if(!closing)notice.Text="Chưa nhận được trạng thái mới. Giao diện vẫn dùng được; dữ liệu đang hiển thị có thể cũ. Không tự mở lại hoặc gửi lại lệnh.";}
   finally{polling=false;}
  }
  void RunSelected(string action){var ids=accounts.Where(r=>picked.Contains(r.Id)).Select(r=>r.Id).ToArray();if(ids.Length==0){notice.Text="Tích cột Chọn trước khi thao tác.";return;}if(ids.Length>200){notice.Text="Chọn tối đa 200 tài khoản cho mỗi thao tác; Auto tất cả áp dụng toàn bộ danh sách.";return;}RunAction(action,ids);}
  public async void RunAction(string action,string[] ids){
   if(acting||demo||closing||connection==null)return;if(!capable){notice.Text="Bộ điều phối cũ chưa hỗ trợ kênh C#. Các thao tác mở/tắt vẫn bị khóa để tránh mở trùng.";return;}acting=true;UpdateButtons();notice.Text="Đang gửi yêu cầu; bạn vẫn có thể cuộn, chọn và tìm kiếm tài khoản.";
   try{
    var response=await connection.SendAsync("manager_action",new Dictionary<string,object>{{"action",action},{"account_ids",ids}});if(!JsonValue.Flag(response,"ok"))throw new IOException();
    commandUncertain=false;if(!closing)notice.Text="Bộ điều phối đã nhận yêu cầu. Chờ trạng thái cập nhật.";
   }catch(Exception){commandUncertain=true;if(!closing)notice.Text="Chưa xác nhận được kết quả. Không tự thử lại để tránh mở trùng; chờ Đồng bộ hoặc kiểm tra cửa sổ QLTK hiện tại.";}
   finally{acting=false;if(!closing){UpdateButtons();PollNow();}}
  }
 }
 public sealed class DemoConnection:IManagerConnection {
  public Task<Dictionary<string,object>> SendAsync(string command,Dictionary<string,object> arguments){return Task.FromResult(Snapshot(5000));}
  public static Dictionary<string,object> Snapshot(int count){var rows=new List<object>();for(int n=0;n<count;n++)rows.Add(new Dictionary<string,object>{{"id","sample-"+n},{"username","taikhoan"+(n+1)},{"character","Nhân vật "+(n+1)},{"zone",n%30},{"bima",n%100},{"proxy","proxy.example:8080"},{"status",n%3==0?"Sơn Cáp: đang hoạt động":"Đã tắt"},{"auto",n%3==0},{"up",false},{"pid",n%3==0?10000+n:0},{"visibility",n%3==0?"Ẩn":"—"}});return new Dictionary<string,object>{{"ok",true},{"preview_version",1},{"activity","Dữ liệu mẫu 5.000 tài khoản • không mở game"},{"accounts",rows.ToArray()}};}
 }
 public static class Program {
  [STAThread] public static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   bool demo=args.Contains("--demo");string data=null;for(int n=0;n<args.Length-1;n++)if(args[n]=="--data")data=args[n+1];
   try{
    if(!demo&&data==null){string defaultData=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data");if(Directory.Exists(defaultData))data=defaultData;else using(var picker=new FolderBrowserDialog{Description="Chọn thư mục Data của QLTK đang chạy"}){if(picker.ShowDialog()!=DialogResult.OK)return 0;data=picker.SelectedPath;}}
    IManagerConnection service=demo?(IManagerConnection)new DemoConnection():new ManagerConnection(data);
    Application.Run(new ManagerForm(service,demo));return 0;
   }catch(Exception){MessageBox.Show("Chưa kết nối được dữ liệu QLTK. Mở QLTK hiện tại, bật Socket rồi chọn đúng thư mục Data. Bản thử không thay đổi tài khoản hoặc mật khẩu.","QLTK C# thử nghiệm",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
  }
 }
}
