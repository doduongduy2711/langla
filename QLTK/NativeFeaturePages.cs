using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace TGLL.Preview {
 public sealed partial class ManagerForm {
  readonly TabControl featureTabs=new TabControl();readonly Dictionary<string,RemoteFeaturePanel> featurePanels=new Dictionary<string,RemoteFeaturePanel>();
  readonly SemaphoreSlim featureCommands=new SemaphoreSlim(1,1);bool featureSupported,featureBusy,featureDirty,nativeModal;string uiSession="";
  DateTime globalRefreshAt=DateTime.MinValue;RemoteFeaturePanel globalPanel;
  int backendPid;readonly Button nativeDialog=new Button();
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(int process);
  readonly HashSet<string> changingFlags=new HashSet<string>();
  async void SetNativeAccountFlag(AccountRow row,string column,bool value){
   if(!featureSupported||nativeModal||demo||closing)return;string key=row.Id+":"+column;if(!changingFlags.Add(key))return;bool previous=column=="auto"?row.Auto:row.Up;if(column=="auto")row.Auto=value;else row.Up=value;grid.Invalidate();
   await featureCommands.WaitAsync();try{await FeatureCall("manager_ui_flags",new Dictionary<string,object>{{"account_id",row.Id},{"column",column},{"value",value}});notice.Text="Đã lưu lựa chọn "+column+" cho "+row.Username;}catch(Exception e){if(column=="auto")row.Auto=previous;else row.Up=previous;if(!closing)notice.Text="Chưa lưu lựa chọn: "+e.Message;}finally{featureCommands.Release();changingFlags.Remove(key);if(!closing){grid.Invalidate();PollNow();}}
  }
  public int FeaturePageCount{get{return featureTabs.TabPages.Count-1;}}
  void InitializeFeaturePages(TableLayoutPanel layout,Control information){
   layout.RowStyles[3]=new RowStyle(SizeType.Absolute,245);layout.RowStyles[4]=new RowStyle(SizeType.Percent,100);
   layout.Controls.Remove(information);featureTabs.Dock=DockStyle.Fill;featureTabs.Multiline=true;featureTabs.Appearance=TabAppearance.Buttons;var overview=new TabPage("Thông tin tài khoản");overview.Controls.Add(information);featureTabs.TabPages.Add(overview);layout.Controls.Add(featureTabs,0,4);featureTabs.SelectedIndexChanged+=delegate{RequestFeatureRefresh();};
   var tools=layout.GetControlFromPosition(0,0);layout.Controls.Remove(tools);
   var header=new Panel{Dock=DockStyle.Fill};tools.Dock=DockStyle.Top;tools.Height=34;tools.Visible=true;header.Controls.Add(tools);
   globalPanel=new RemoteFeaturePanel("global",SendFeatureEvent,RequestFeatureRefresh){Dock=DockStyle.Fill};
   header.Controls.Add(globalPanel);globalPanel.BringToFront();tools.BringToFront();layout.Controls.Add(header,0,0);layout.RowStyles[0].Height=159;
   tools.Controls.Clear();SetupButton(tools,nativeDialog,"Mở hộp thoại",FocusNativeDialog,105);
   var update=new Button();SetupButton(tools,update,"Cập nhật C#",async delegate{
    update.Enabled=false;try{notice.Text="Đang kiểm tra bản C# trên GitHub...";string stage=await FrontendUpdate.Download();if(stage==null){notice.Text="Giao diện C# đã là bản mới nhất.";return;}var owner=connection as AutoStartingConnection;if(owner==null)throw new IOException("Chưa chọn Data.");
     if(MessageBox.Show("Đã tải bản C# mới. Cần đóng các game trước khi cập nhật cả giao diện và bộ điều phối. Tiếp tục mở lại QLTK C#?","Cập nhật C#",MessageBoxButtons.YesNo)==DialogResult.Yes){FrontendUpdate.Start(stage,owner.DataDirectory);Close();}
    }catch(Exception e){notice.Text="Chưa cập nhật: "+e.Message;}finally{if(!IsDisposed)update.Enabled=true;}
   },160);
  }
  async void FocusNativeDialog(){try{if(backendPid>0)AllowSetForegroundWindow(backendPid);await FeatureCall("manager_ui_dialog",null);}catch(Exception error){if(!closing)notice.Text=error.Message;}}
  string FeatureAccount(){return grid.CurrentCell!=null&&grid.CurrentCell.RowIndex<visible.Count?visible[grid.CurrentCell.RowIndex].Id:"";}
  Dictionary<string,object> FeatureContext(string page,string account){return new Dictionary<string,object>{{"page_id",page},{"account_id",account},{"ui_session",uiSession},{"selected_all",accounts.Count>0&&picked.Count==accounts.Count},{"selected_ids",picked.Count==accounts.Count?new string[0]:picked.ToArray()}};}
  static Dictionary<string,object> Merge(Dictionary<string,object> left,Dictionary<string,object> right){var result=new Dictionary<string,object>(left);foreach(var pair in right)result[pair.Key]=pair.Value;return result;}
  async Task<Dictionary<string,object>> FeatureCall(string command,Dictionary<string,object> args){var result=await connection.SendAsync(command,args);if(!JsonValue.Flag(result,"ok"))throw new InvalidOperationException(JsonValue.Text(result,"message").Length>0?JsonValue.Text(result,"message"):"Bộ điều phối chưa sẵn sàng; nạp lại tab.");return result;}
  void RequestFeatureRefresh(){
   featureDirty=true;if(closing||featureBusy||!featureSupported||!IsHandleCreated)return;RefreshFeatures();
  }
  async void RefreshFeatures(){
   if(featureBusy||closing||!featureSupported)return;featureBusy=true;featureDirty=false;
   try{
    if(uiSession.Length==0){
     var catalog=await FeatureCall("manager_ui_catalog",null);if(closing)return;uiSession=JsonValue.Text(catalog,"ui_session");Int32.TryParse(JsonValue.Text(catalog,"backend_pid"),out backendPid);
     foreach(var page in JsonValue.Array(JsonValue.Get(catalog,"pages"))){var model=JsonValue.Map(page);string id=JsonValue.Text(model,"id");if(id.Length==0||featurePanels.ContainsKey(id))continue;if(id=="global"){featurePanels[id]=globalPanel;continue;}var tab=new TabPage(JsonValue.Text(model,"text"));var panel=new RemoteFeaturePanel(id,SendFeatureEvent,RequestFeatureRefresh);panel.Dock=DockStyle.Fill;tab.Tag=panel;tab.Controls.Add(panel);featureTabs.TabPages.Add(tab);featurePanels[id]=panel;}
    }
    var active=featureTabs.SelectedTab==null?null:featureTabs.SelectedTab.Tag as RemoteFeaturePanel;
    if(active!=null){
     string account=FeatureAccount();if(active.ContextAccount!=account)await active.CommitForAccountSwitch();var context=FeatureContext(active.PageId,account);context["grid_views"]=active.GridViews();
     var frame=await FeatureCall("manager_ui_frame",context);if(closing)return;
     if(JsonValue.Text(frame,"ui_session")!=uiSession){ResetFeatures();return;}
     if(Object.ReferenceEquals(featureTabs.SelectedTab.Tag,active)&&account==FeatureAccount()){
      if(!JsonValue.Flag(frame,"modal")&&JsonValue.Text(frame,"account_id")!=account){RequestFeatureRefresh();return;}
      active.ApplyFrame(frame,context);if(JsonValue.Flag(frame,"modal"))notice.Text="Mở hộp thoại đang mở: "+JsonValue.Text(frame,"modal_title")+". Hoàn tất cửa sổ đó trước khi đổi cấu hình.";
     }
    }
    if(globalPanel.ContextAccount!=FeatureAccount()||(DateTime.UtcNow-globalRefreshAt).TotalSeconds>=10){
    string globalAccount=FeatureAccount();if(globalPanel.ContextAccount!=globalAccount)await globalPanel.CommitForAccountSwitch();
    var globalContext=FeatureContext("global",globalAccount);var globalFrame=await FeatureCall("manager_ui_frame",globalContext);if(closing)return;
    if(JsonValue.Text(globalFrame,"ui_session")!=uiSession){ResetFeatures();return;}
    if(globalAccount==FeatureAccount()){
     globalFrame["children"]=JsonValue.Array(JsonValue.Get(globalFrame,"children")).Where(raw=>{var n=JsonValue.Map(raw);int y;return Int32.TryParse(JsonValue.Text(n,"y"),out y)&&y<120;}).ToArray();
     globalPanel.ApplyFrame(globalFrame,globalContext);
    }
     globalRefreshAt=DateTime.UtcNow;
    }
   }catch(Exception error){if(!closing)notice.Text="Chưa nạp được chức năng: "+error.Message;}
   finally{featureBusy=false;if(featureDirty&&!closing)BeginInvoke((Action)RequestFeatureRefresh);}
  }
  void ResetFeatures(){foreach(var panel in featurePanels.Values)if(panel!=globalPanel)panel.Parent.Dispose();featurePanels.Clear();uiSession="";globalRefreshAt=DateTime.MinValue;featureDirty=true;}
  async Task<Dictionary<string,object>> SendFeatureEvent(string page,Dictionary<string,object> context,Dictionary<string,object> change){
   await featureCommands.WaitAsync();
   try{
    if(closing)return new Dictionary<string,object>();notice.Text="Đang áp dụng thao tác; giao diện vẫn có thể cuộn và chuyển tab.";
    if(backendPid>0)AllowSetForegroundWindow(backendPid);var result=await FeatureCall("manager_ui_event",Merge(context,change));if(!closing){notice.Text=JsonValue.Text(result,"operation_id").Length>0?"Đã xếp thao tác; đợi trạng thái hoặc hoàn tất cửa sổ phụ nếu có.":"Đã áp dụng thay đổi.";RequestFeatureRefresh();}return result;
   }catch(Exception error){if(!closing){notice.Text="Chưa áp dụng: "+error.Message;RequestFeatureRefresh();}throw;}
   finally{featureCommands.Release();}
  }
 }
 public sealed class RemoteFeaturePanel:Panel {
  public readonly string PageId;
  readonly Func<string,Dictionary<string,object>,Dictionary<string,object>,Task<Dictionary<string,object>>> send;readonly Action refresh;
  readonly Dictionary<string,Control> widgets=new Dictionary<string,Control>();readonly Dictionary<string,Dictionary<string,object>> nodes=new Dictionary<string,Dictionary<string,object>>();
  readonly Dictionary<string,string> pendingButtons=new Dictionary<string,string>();readonly Dictionary<string,Dictionary<string,object>> edits=new Dictionary<string,Dictionary<string,object>>();
  readonly HashSet<string> activeEdits=new HashSet<string>();TaskCompletionSource<bool> flushCompletion;
  readonly Dictionary<string,GridData> grids=new Dictionary<string,GridData>();readonly System.Windows.Forms.Timer debounce=new System.Windows.Forms.Timer();
  Dictionary<string,object> context=new Dictionary<string,object>();bool applying,flushing;string structure="",account="";
  public string ContextAccount{get{return account;}}
  public async Task CommitForAccountSwitch(){foreach(var text in widgets.Values.OfType<TextBox>())CaptureText(text);foreach(var grid in widgets.Values.OfType<DataGridView>())grid.EndEdit();Enabled=false;await FlushAsync();}
  sealed class GridData {internal long Epoch=-1;internal int Count;internal Dictionary<int,Dictionary<string,object>> Rows=new Dictionary<int,Dictionary<string,object>>();}
  static object Value(Dictionary<string,object> node,string key){return JsonValue.Get(node,key);}
  static string NodeText(Dictionary<string,object> node,string key){return JsonValue.Text(node,key);}
  static int Int(Dictionary<string,object> node,string key,int fallback){int n;return Int32.TryParse(NodeText(node,key),out n)?n:fallback;}
  static decimal Decimal(Dictionary<string,object> node,string key,decimal fallback){decimal n;return System.Decimal.TryParse(NodeText(node,key),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out n)?n:fallback;}
  static string Shape(object[] children){return String.Join("|",children.Select(JsonValue.Map).Where(n=>n!=null).Select(n=>NodeText(n,"id")+":"+NodeText(n,"kind")+"["+Shape(JsonValue.Array(Value(n,"children")))+"]"));}
  public RemoteFeaturePanel(string page,Func<string,Dictionary<string,object>,Dictionary<string,object>,Task<Dictionary<string,object>>> sender,Action requestRefresh){PageId=page;send=sender;refresh=requestRefresh;AutoScroll=true;debounce.Interval=250;debounce.Tick+=delegate{debounce.Stop();FlushEdits();};}
  protected override void Dispose(bool disposing){if(disposing){debounce.Dispose();foreach(var picture in widgets.Values.OfType<PictureBox>())if(picture.Image!=null)picture.Image.Dispose();}base.Dispose(disposing);}
  public Dictionary<string,object> GridViews(){var result=new Dictionary<string,object>();foreach(var pair in widgets){var grid=pair.Value as DataGridView;if(grid==null)continue;result[pair.Key]=new Dictionary<string,object>{{"first",Math.Max(0,grid.FirstDisplayedScrollingRowIndex-10)},{"count",Math.Max(50,grid.DisplayedRowCount(false)+25)}};}return result;}
  public void ApplyFrame(Dictionary<string,object> frame,Dictionary<string,object> requestContext){
   applying=true;try{
    string nextAccount=NodeText(requestContext,"account_id"),shape=Shape(JsonValue.Array(Value(frame,"children")));
    if(nextAccount!=account){account=nextAccount;}
    context=new Dictionary<string,object>(requestContext);
    foreach(var raw in JsonValue.Array(Value(frame,"operations"))){var op=JsonValue.Map(raw);string id=NodeText(op,"id");if(NodeText(op,"state")!="queued"&&NodeText(op,"state")!="running")foreach(string button in pendingButtons.Where(p=>p.Value==id).Select(p=>p.Key).ToArray())pendingButtons.Remove(button);}
    if(shape!=structure){foreach(Control c in Controls.Cast<Control>().ToArray())c.Dispose();widgets.Clear();nodes.Clear();grids.Clear();structure=shape;Build(this,JsonValue.Array(Value(frame,"children")));}
    MergeWidgets(JsonValue.Array(Value(frame,"children")));Enabled=!JsonValue.Flag(frame,"modal");
    int right=0,bottom=0;foreach(Control c in Controls){right=Math.Max(right,c.Right+12);bottom=Math.Max(bottom,c.Bottom+12);}AutoScrollMinSize=new Size(right,bottom);
   }finally{applying=false;}
  }
  bool Pending(string id){return activeEdits.Contains(account+"|"+id)||edits.Values.Any(p=>NodeText(p,"control_id")==id&&NodeText(JsonValue.Map(Value(p,"_context")),"account_id")==account);}
  void Queue(string id,Dictionary<string,object> change,bool immediate){if(applying||!Enabled)return;change["control_id"]=id;change["_context"]=new Dictionary<string,object>(context);string key=account+"|"+id+"|"+NodeText(change,"event")+"|"+NodeText(change,"index")+"|"+NodeText(change,"row")+"|"+NodeText(change,"column");edits[key]=change;if(immediate)FlushEdits();else{debounce.Stop();debounce.Start();}}
  async void FlushEdits(){try{await FlushAsync();}catch{refresh();}}
  async Task FlushAsync(){
   if(flushing){if(!await flushCompletion.Task)throw new InvalidOperationException("Chưa lưu xong ô nhập trước đó.");return;}flushing=true;flushCompletion=new TaskCompletionSource<bool>();bool success=false;
   try{while(edits.Count>0&&!IsDisposed){var pair=edits.First();edits.Remove(pair.Key);var patch=pair.Value;var targetContext=JsonValue.Map(Value(patch,"_context"));patch.Remove("_context");string active=NodeText(targetContext,"account_id")+"|"+NodeText(patch,"control_id");activeEdits.Add(active);try{await send(PageId,targetContext,patch);}finally{activeEdits.Remove(active);}}success=true;}
   finally{flushing=false;flushCompletion.TrySetResult(success);}
  }
  Dictionary<string,object> Choice(string id,string kind,int index){return new Dictionary<string,object>{{"event",kind},{"index",index},{"items_key",NodeText(nodes[id],"items_key")}};}
  Dictionary<string,object> GridPatch(string id,string kind,int row,int column){GridData data;Dictionary<string,object> record;if(!grids.TryGetValue(id,out data)||!data.Rows.TryGetValue(row,out record)){refresh();return null;}return new Dictionary<string,object>{{"event",kind},{"row",row},{"column",column},{"row_key",NodeText(record,"key")},{"epoch",data.Epoch}};}
  object[] Guards(){var result=new List<object>();foreach(var pair in widgets){var grid=pair.Value as DataGridView;if(grid==null||grid.CurrentCell==null)continue;var guard=GridPatch(pair.Key,"guard",grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);if(guard==null)throw new InvalidOperationException("Đợi tải đầy đủ dòng đã chọn trước khi thao tác.");guard["control_id"]=pair.Key;result.Add(guard);}return result.ToArray();}
  async void ClickButton(string id){
   if(applying||pendingButtons.ContainsKey(id))return;
   var clickContext=new Dictionary<string,object>(context);
   try{
    foreach(var grid in widgets.Values.OfType<DataGridView>())grid.EndEdit();foreach(var text in widgets.Values.OfType<TextBox>())if(text.Modified)CaptureText(text);
    await FlushAsync();if(NodeText(clickContext,"account_id")!=account)throw new InvalidOperationException("Tài khoản đã thay đổi; chọn lại thao tác.");var result=await send(PageId,clickContext,new Dictionary<string,object>{{"control_id",id},{"event","click"},{"guards",Guards()}});
    string operation=NodeText(result,"operation_id");if(operation.Length>0){pendingButtons[id]=operation;widgets[id].Enabled=false;}refresh();
   }catch{refresh();}
  }
  void CaptureText(TextBox text){if(!applying&&text.Modified&&!text.ReadOnly){text.Modified=false;Queue(Convert.ToString(text.Tag),new Dictionary<string,object>{{"event","text"},{"value",text.Text}},false);}}
  void Build(Control parent,object[] children){
   foreach(var raw in children){var node=JsonValue.Map(raw);if(node==null)continue;string id=NodeText(node,"id"),kind=NodeText(node,"kind");Control c;
    switch(kind){
     case "button":var button=new Button();button.Click+=delegate{ClickButton(id);};c=button;break;
     case "check":var check=new CheckBox();check.CheckedChanged+=delegate{Queue(id,new Dictionary<string,object>{{"event","check"},{"value",check.Checked}},false);};c=check;break;
     case "number":var number=new NumericUpDown();number.ValueChanged+=delegate{Queue(id,new Dictionary<string,object>{{"event","number"},{"value",number.Value}},false);};c=number;break;
     case "combo":var combo=new ComboBox();combo.SelectionChangeCommitted+=delegate{Queue(id,Choice(id,"select",combo.SelectedIndex),false);};combo.Leave+=delegate{if(combo.DropDownStyle!=ComboBoxStyle.DropDownList&&!applying){var p=Choice(id,"select",combo.SelectedIndex);p["value"]=combo.Text;Queue(id,p,false);}};c=combo;break;
     case "checks":var checks=new CheckedListBox{CheckOnClick=true};checks.ItemCheck+=delegate(object sender,ItemCheckEventArgs e){var p=Choice(id,"item_check",e.Index);p["value"]=e.NewValue==CheckState.Checked;Queue(id,p,false);};c=checks;break;
     case "list":var list=new ListBox();list.SelectedIndexChanged+=delegate{if(list.SelectedIndex>=0)Queue(id,Choice(id,"select",list.SelectedIndex),false);};c=list;break;
     case "text":var text=new TextBox();text.Leave+=delegate{CaptureText(text);};c=text;break;
     case "grid":var grid=new BufferedGrid{VirtualMode=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,BackgroundColor=Color.White};
      grid.CellValueNeeded+=delegate(object sender,DataGridViewCellValueEventArgs e){GridData data;Dictionary<string,object> record;if(grids.TryGetValue(id,out data)&&data.Rows.TryGetValue(e.RowIndex,out record)){var values=JsonValue.Array(Value(record,"values"));if(e.ColumnIndex<values.Length)e.Value=values[e.ColumnIndex];}};
      grid.CellValuePushed+=delegate(object sender,DataGridViewCellValueEventArgs e){var p=GridPatch(id,"grid_edit",e.RowIndex,e.ColumnIndex);if(p!=null){p["value"]=e.Value;Queue(id,p,false);}};
      grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
      grid.CellEnter+=delegate(object sender,DataGridViewCellEventArgs e){if(applying)return;var p=GridPatch(id,"grid_select",e.RowIndex,e.ColumnIndex);if(p!=null)Queue(id,p,false);};
      grid.SelectionChanged+=delegate{if(applying||grid.CurrentCell==null)return;var p=GridPatch(id,"grid_select",grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);if(p!=null){p["selected_rows"]=grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).ToArray();Queue(id,p,false);}};
      grid.CellContentClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0&&grid.Columns[e.ColumnIndex] is DataGridViewButtonColumn){var p=GridPatch(id,"grid_click",e.RowIndex,e.ColumnIndex);if(p!=null)Queue(id,p,true);}};
      grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0){var p=GridPatch(id,"grid_double",e.RowIndex,e.ColumnIndex);if(p!=null)Queue(id,p,true);}};
      grid.Scroll+=delegate{if(!applying)refresh();};grid.DataError+=delegate(object sender,DataGridViewDataErrorEventArgs e){e.ThrowException=false;};c=grid;break;
     case "image":c=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom};break;
     case "progress":c=new ProgressBar();break;
     case "label":c=new Label();break;
     case "group":c=new GroupBox();break;
     default:c=new Panel();break;
    }
    c.Tag=id;c.SetBounds(Int(node,"x",0),Int(node,"y",0),Math.Max(1,Int(node,"width",100)),Math.Max(1,Int(node,"height",24)));parent.Controls.Add(c);widgets[id]=c;nodes[id]=node;
    if(Value(node,"children")!=null)Build(c,JsonValue.Array(Value(node,"children")));
   }
  }
  void MergeWidgets(object[] children){
   foreach(var raw in children){var n=JsonValue.Map(raw);if(n==null)continue;string id=NodeText(n,"id");Control c;if(!widgets.TryGetValue(id,out c))continue;nodes[id]=n;c.Visible=JsonValue.Flag(n,"visible");c.Enabled=JsonValue.Flag(n,"enabled")&&!pendingButtons.ContainsKey(id);
    if(!(c is TextBoxBase)&&!(c is ComboBox)&&c.Text!=NodeText(n,"text"))c.Text=NodeText(n,"text");
    var check=c as CheckBox;if(check!=null&&!Pending(id)){check.Appearance=JsonValue.Flag(n,"button_style")?Appearance.Button:Appearance.Normal;check.Checked=JsonValue.Flag(n,"checked");}
    var number=c as NumericUpDown;if(number!=null&&!Pending(id)){number.Minimum=Decimal(n,"minimum",0);number.Maximum=Decimal(n,"maximum",100);number.DecimalPlaces=Int(n,"decimals",0);number.Increment=Math.Max(0.001m,Decimal(n,"increment",1));decimal target=Math.Max(number.Minimum,Math.Min(number.Maximum,Decimal(n,"value",0)));if(number.Value!=target)number.Value=target;}
    var combo=c as ComboBox;if(combo!=null&&!combo.DroppedDown&&!Pending(id)){var items=JsonValue.Array(Value(n,"items")).Select(Convert.ToString).ToArray();if(!combo.Items.Cast<object>().Select(Convert.ToString).SequenceEqual(items)){combo.Items.Clear();combo.Items.AddRange(items);}combo.DropDownStyle=JsonValue.Flag(n,"editable")?ComboBoxStyle.DropDown:ComboBoxStyle.DropDownList;combo.SelectedIndex=Math.Min(combo.Items.Count-1,Int(n,"index",-1));if(combo.SelectedIndex<0&&combo.DropDownStyle!=ComboBoxStyle.DropDownList&&!combo.ContainsFocus)combo.Text=NodeText(n,"text");}
    var list=c as ListBox;if(list!=null&&!Pending(id)){var items=JsonValue.Array(Value(n,"items")).Select(Convert.ToString).ToArray();if(!list.Items.Cast<object>().Select(Convert.ToString).SequenceEqual(items)){list.Items.Clear();list.Items.AddRange(items);}list.SelectedIndex=Math.Min(list.Items.Count-1,Int(n,"index",-1));var checks=list as CheckedListBox;if(checks!=null){var indices=new HashSet<int>(JsonValue.Array(Value(n,"checked_indices")).Select(Convert.ToInt32));for(int k=0;k<checks.Items.Count;k++)checks.SetItemChecked(k,indices.Contains(k));}}
    var text=c as TextBox;if(text!=null){text.ReadOnly=JsonValue.Flag(n,"readonly");text.Multiline=JsonValue.Flag(n,"multiline");text.MaxLength=Math.Max(1,Int(n,"max_length",32767));text.WordWrap=JsonValue.Flag(n,"word_wrap");text.ScrollBars=(ScrollBars)Math.Max(0,Math.Min(3,Int(n,"scrollbars",text.Multiline?2:0)));text.CharacterCasing=(CharacterCasing)Math.Max(0,Math.Min(2,Int(n,"casing",0)));text.UseSystemPasswordChar=JsonValue.Flag(n,"masked");if((!text.ContainsFocus||!text.Modified)&&!Pending(id)&&text.Text!=NodeText(n,"text"))text.Text=NodeText(n,"text");}
    var picture=c as PictureBox;if(picture!=null){string encoded=NodeText(n,"image");if(encoded.Length>0&&Convert.ToString(picture.AccessibleDescription)!=encoded){using(var memory=new MemoryStream(Convert.FromBase64String(encoded)))using(var image=Image.FromStream(memory)){var old=picture.Image;picture.Image=new Bitmap(image);if(old!=null)old.Dispose();}picture.AccessibleDescription=encoded;}}
    var progress=c as ProgressBar;if(progress!=null){progress.Minimum=Int(n,"minimum",0);progress.Maximum=Math.Max(progress.Minimum+1,Int(n,"maximum",100));progress.Value=Math.Max(progress.Minimum,Math.Min(progress.Maximum,Int(n,"value",0)));progress.Style=JsonValue.Flag(n,"marquee")?ProgressBarStyle.Marquee:ProgressBarStyle.Blocks;}
    var grid=c as DataGridView;if(grid!=null)MergeGrid(id,grid,n);
    if(Value(n,"children")!=null)MergeWidgets(JsonValue.Array(Value(n,"children")));
   }
  }
  void MergeGrid(string id,DataGridView grid,Dictionary<string,object> node){
   var columns=JsonValue.Array(Value(node,"columns")).Select(JsonValue.Map).ToArray();bool rebuild=grid.Columns.Count!=columns.Length;for(int n=0;!rebuild&&n<columns.Length;n++)if(grid.Columns[n].Name!=NodeText(columns[n],"name"))rebuild=true;
   if(rebuild){grid.Columns.Clear();foreach(var col in columns){string kind=NodeText(col,"kind");DataGridViewColumn c=kind=="button"?(DataGridViewColumn)new DataGridViewButtonColumn():kind=="check"?(DataGridViewColumn)new DataGridViewCheckBoxColumn():new DataGridViewTextBoxColumn();c.Name=NodeText(col,"name");c.HeaderText=NodeText(col,"text");c.Width=Math.Max(35,Int(col,"width",90));c.Visible=JsonValue.Flag(col,"visible");c.ReadOnly=JsonValue.Flag(col,"readonly");c.SortMode=DataGridViewColumnSortMode.NotSortable;var button=c as DataGridViewButtonColumn;if(button!=null){button.Text=NodeText(col,"button_text");button.UseColumnTextForButtonValue=JsonValue.Flag(col,"use_text");}grid.Columns.Add(c);}}
   GridData data;if(!grids.TryGetValue(id,out data)){data=new GridData();grids[id]=data;}long epoch;Int64.TryParse(NodeText(node,"epoch"),out epoch);if(epoch!=data.Epoch){data.Rows.Clear();data.Epoch=epoch;}
   foreach(var raw in JsonValue.Array(Value(node,"rows"))){var row=JsonValue.Map(raw);data.Rows[Int(row,"index",-1)]=row;}
   data.Count=Math.Max(0,Int(node,"row_count",0));if(grid.RowCount!=data.Count)grid.RowCount=data.Count;grid.ReadOnly=JsonValue.Flag(node,"readonly");grid.MultiSelect=JsonValue.Flag(node,"multi_select");
   if(!grid.ContainsFocus&&!Pending(id)&&!grid.IsCurrentCellInEditMode){int row=Int(node,"current_row",-1),column=Int(node,"current_column",0);if(row>=0&&row<grid.RowCount&&column>=0&&column<grid.Columns.Count&&(grid.CurrentCell==null||grid.CurrentCell.RowIndex!=row||grid.CurrentCell.ColumnIndex!=column)){grid.CurrentCell=grid.Rows[row].Cells[column];refresh();}var selected=new HashSet<int>(JsonValue.Array(Value(node,"selected_rows")).Select(Convert.ToInt32));foreach(DataGridViewRow current in grid.SelectedRows)if(!selected.Contains(current.Index))current.Selected=false;foreach(int index in selected)if(index>=0&&index<grid.RowCount)grid.Rows[index].Selected=true;}
   if(!grid.IsCurrentCellInEditMode)grid.Invalidate();
  }
 }
}
