using System;using System.Collections.Generic;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Linq;using System.Reflection;using System.Runtime.CompilerServices;using System.Runtime.InteropServices;using System.Security.Cryptography;using System.Text;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace TGLL {
 // Typed adapter to existing controllers. No arbitrary method/script evaluation is exposed.
 public sealed class NativeUiBridge {
  readonly Form root;readonly TabControl tabs;readonly DataGridView accounts;readonly Func<string[]> accountIds;readonly Func<string> status;
  readonly string session=Guid.NewGuid().ToString("N");int next;
  readonly Dictionary<Control,string> names=new Dictionary<Control,string>();readonly Dictionary<string,Control> controls=new Dictionary<string,Control>(StringComparer.Ordinal);
  readonly Dictionary<string,Operation> operations=new Dictionary<string,Operation>();
  sealed class Operation {internal string Id,Control,State="queued",Error="";internal DateTime At=DateTime.UtcNow;}
  sealed class GridVersion {internal long Value;public GridVersion(DataGridView grid){grid.CellValueChanged+=delegate{Value++;};grid.RowsAdded+=delegate{Value++;};grid.RowsRemoved+=delegate{Value++;};grid.DataBindingComplete+=delegate{Value++;};grid.Sorted+=delegate{Value++;};}}
  readonly ConditionalWeakTable<DataGridView,GridVersion> versions=new ConditionalWeakTable<DataGridView,GridVersion>();
  static object Get(Dictionary<string,object> map,string key){object v;return map!=null&&map.TryGetValue(key,out v)?v:null;}
  static string Text(Dictionary<string,object> map,string key){return Convert.ToString(Get(map,key),System.Globalization.CultureInfo.InvariantCulture);}
  static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>;}
  static object[] List(object value){return value as object[]??new object[0];}
  static bool Flag(Dictionary<string,object> map,string key){return Object.Equals(Get(map,key),true);}
  static int Number(Dictionary<string,object> map,string key,int fallback){int value;return Int32.TryParse(Text(map,key),out value)?value:fallback;}
  static string Hash(string value){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
  static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
  static readonly MethodInfo getLocalState=typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);
  static bool LocalVisible(Control control){var method=getLocalState;return method==null||Convert.ToBoolean(method.Invoke(control,new object[]{2}));}
  public NativeUiBridge(Form window,TabControl pages,DataGridView table,Func<string[]> ids,Func<string> statusText){root=window;tabs=pages;accounts=table;accountIds=ids;status=statusText;}
  string Id(Control control){string id;if(names.TryGetValue(control,out id))return id;id="u"+(++next);names[control]=id;controls[id]=control;return id;}
  Control Resolve(string id){Control control;Require(controls.TryGetValue(id,out control)&&!control.IsDisposed,"Điều khiển đã thay đổi; nạp lại tab trước khi thao tác.");return control;}
  static bool Descendant(Control child,Control parent){for(Control at=child;at!=null;at=at.Parent)if(Object.ReferenceEquals(at,parent))return true;return false;}
  Form Modal(){foreach(Form form in Application.OpenForms)if(!Object.ReferenceEquals(form,root)&&form.Modal)return form;return null;}
  delegate bool WindowCallback(IntPtr window,IntPtr state);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr state);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int size);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
  IntPtr NativeDialog(){if(!root.IsHandleCreated)return IntPtr.Zero;uint process;uint thread=GetWindowThreadProcessId(root.Handle,out process);IntPtr found=IntPtr.Zero;WindowCallback callback=delegate(IntPtr window,IntPtr state){if(IsWindowVisible(window)){var name=new StringBuilder(100);GetClassName(window,name,name.Capacity);if(name.ToString()=="#32770"){found=window;return false;}}return true;};EnumThreadWindows(thread,callback,IntPtr.Zero);GC.KeepAlive(callback);return found;}
  public bool ModalOpen{get{return Modal()!=null||NativeDialog()!=IntPtr.Zero||!root.Enabled;}}
  string ModalTitle(){var form=Modal();return form!=null?form.Text:ModalOpen?"Hộp thoại QLTK đang mở":"";}
  Control Frame(string id){if(id=="global")return root;var control=Resolve(id);Require(control is TabPage&&Object.ReferenceEquals(control.Parent,tabs),"Tab không hợp lệ.");return control;}
  void InFrame(Control control,Control frame){Require(Descendant(control,frame)&&!Object.ReferenceEquals(control,accounts)&&!Descendant(control,accounts)&&(!Object.ReferenceEquals(frame,root)||!Descendant(control,tabs)),"Điều khiển không thuộc tab đã chọn.");}
  Dictionary<string,DataGridViewRow> Rows(){string[] ids=accountIds()??new string[0];Require(ids.Length==accounts.Rows.Count,"Danh sách tài khoản đang đổi; nạp lại.");var result=new Dictionary<string,DataGridViewRow>(StringComparer.Ordinal);for(int n=0;n<ids.Length;n++)if(!String.IsNullOrEmpty(ids[n]))result[ids[n]]=accounts.Rows[n];return result;}
  string CurrentAccount(){var rows=Rows();foreach(var pair in rows)if(pair.Value.Selected)return pair.Key;return "";}
  void Context(Dictionary<string,object> request){
   Require(!ModalOpen,"Hoàn tất cửa sổ phụ đang mở trước khi đổi cấu hình.");
   var rows=Rows();string selected=Text(request,"account_id");Require(selected.Length==0||rows.ContainsKey(selected),"Tài khoản không còn trong bảng.");
   var selectedIds=new HashSet<string>(List(Get(request,"selected_ids")).Select(Convert.ToString),StringComparer.Ordinal);
   Require(selectedIds.All(rows.ContainsKey),"Danh sách đã chọn đã thay đổi.");
   if(selected!=CurrentAccount()){accounts.ClearSelection();if(selected.Length>0){accounts.CurrentCell=rows[selected].Cells[0];rows[selected].Selected=true;}else accounts.CurrentCell=null;}
   bool all=Flag(request,"selected_all");foreach(var pair in rows){bool check=all||selectedIds.Contains(pair.Key);if(!Object.Equals(pair.Value.Cells["pick"].Value,check))pair.Value.Cells["pick"].Value=check;}
  }
  string ItemsKey(System.Collections.IEnumerable items){var text=new List<string>();foreach(object item in items)text.Add(Convert.ToString(item));return Hash(String.Join("\u001f",text));}
  GridVersion Version(DataGridView grid){return versions.GetValue(grid,g=>new GridVersion(g));}
  static object CellValue(object value){if(value==null||value is DBNull)return "";if(value is bool)return value;return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);}
  string RowKey(DataGridViewRow row){return Hash(String.Join("\u001f",row.Cells.Cast<DataGridViewCell>().Select(c=>Convert.ToString(CellValue(c.Value)))));}
  Dictionary<string,object> Widget(Control c,Dictionary<string,object> views,int depth){
   if(Object.ReferenceEquals(c,accounts)||Object.ReferenceEquals(c,tabs))return null;
   Require(depth<20,"Cấu trúc điều khiển quá sâu.");
   string kind=c is CheckBox?"check":c is Button?"button":c is NumericUpDown?"number":c is ComboBox?"combo":c is CheckedListBox?"checks":c is ListBox?"list":c is DataGridView?"grid":c is TextBoxBase?"text":c is PictureBox?"image":c is ProgressBar?"progress":c is Label?"label":c is GroupBox?"group":c is TabPage?"page":c is TabControl?"tabs":"panel";
   int x=c.Left,y=c.Top;var parent=c.Parent as ScrollableControl;if(parent!=null){x-=parent.AutoScrollPosition.X;y-=parent.AutoScrollPosition.Y;}
   var node=new Dictionary<string,object>{{"id",Id(c)},{"kind",kind},{"text",c.Text},{"x",x},{"y",y},{"width",c.Width},{"height",c.Height},{"enabled",c.Enabled},{"visible",LocalVisible(c)}};
   var check=c as CheckBox;if(check!=null){node["checked"]=check.Checked;node["button_style"]=check.Appearance==Appearance.Button;}
   var number=c as NumericUpDown;if(number!=null){node["minimum"]=number.Minimum;node["maximum"]=number.Maximum;node["value"]=number.Value;node["increment"]=number.Increment;node["decimals"]=number.DecimalPlaces;}
   var combo=c as ComboBox;if(combo!=null){node["items"]=combo.Items.Cast<object>().Select(Convert.ToString).ToArray();node["items_key"]=ItemsKey(combo.Items);node["index"]=combo.SelectedIndex;node["editable"]=combo.DropDownStyle!=ComboBoxStyle.DropDownList;}
   var list=c as ListBox;if(list!=null){node["items"]=list.Items.Cast<object>().Select(Convert.ToString).ToArray();node["items_key"]=ItemsKey(list.Items);node["index"]=list.SelectedIndex;var checks=c as CheckedListBox;if(checks!=null)node["checked_indices"]=checks.CheckedIndices.Cast<int>().ToArray();}
   var text=c as TextBoxBase;if(text!=null){node["readonly"]=text.ReadOnly;node["multiline"]=text.Multiline;node["max_length"]=text.MaxLength;node["word_wrap"]=text.WordWrap;var plain=c as TextBox;if(plain!=null){node["scrollbars"]=(int)plain.ScrollBars;node["casing"]=(int)plain.CharacterCasing;}bool masked=plain!=null&&(plain.UseSystemPasswordChar||plain.PasswordChar!='\0');node["masked"]=masked;if(masked){node["has_value"]=plain.TextLength>0;node["text"]="";}}
   var image=c as PictureBox;if(image!=null&&image.Image!=null){using(var memory=new MemoryStream()){image.Image.Save(memory,ImageFormat.Png);if(memory.Length<1048576)node["image"]=Convert.ToBase64String(memory.ToArray());}}
   var progress=c as ProgressBar;if(progress!=null){node["minimum"]=progress.Minimum;node["maximum"]=progress.Maximum;node["value"]=progress.Value;node["marquee"]=progress.Style==ProgressBarStyle.Marquee;}
   var table=c as DataGridView;if(table!=null){
    node["epoch"]=Version(table).Value;node["row_count"]=table.Rows.Count;node["readonly"]=table.ReadOnly;node["multi_select"]=table.MultiSelect;
    node["columns"]=table.Columns.Cast<DataGridViewColumn>().Select(col=>new Dictionary<string,object>{{"name",col.Name},{"text",col.HeaderText},{"width",col.Width},{"visible",col.Visible},{"readonly",col.ReadOnly},{"kind",col is DataGridViewButtonColumn?"button":col is DataGridViewCheckBoxColumn?"check":"text"},{"button_text",col is DataGridViewButtonColumn?((DataGridViewButtonColumn)col).Text:""},{"use_text",col is DataGridViewButtonColumn&&((DataGridViewButtonColumn)col).UseColumnTextForButtonValue}}).ToArray();
    var view=Map(Get(views,Id(c)));int first=Math.Max(0,Number(view,"first",0));if(first>=table.Rows.Count)first=Math.Max(0,table.Rows.Count-50);int count=Math.Max(40,Math.Min(150,Number(view,"count",60)));int last=Math.Min(table.Rows.Count,first+count);
    var records=new List<object>();for(int n=first;n<last;n++){var row=table.Rows[n];records.Add(new Dictionary<string,object>{{"index",n},{"key",RowKey(row)},{"values",row.Cells.Cast<DataGridViewCell>().Select(cell=>CellValue(cell.Value)).ToArray()},{"new_row",row.IsNewRow}});}
    foreach(var selected in table.SelectedRows.Cast<DataGridViewRow>().Take(200))if(selected.Index<first||selected.Index>=last)records.Add(new Dictionary<string,object>{{"index",selected.Index},{"key",RowKey(selected)},{"values",selected.Cells.Cast<DataGridViewCell>().Select(cell=>CellValue(cell.Value)).ToArray()},{"new_row",selected.IsNewRow}});
    node["rows"]=records.ToArray();node["selected_rows"]=table.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).ToArray();node["current_row"]=table.CurrentCell==null?-1:table.CurrentCell.RowIndex;node["current_column"]=table.CurrentCell==null?0:table.CurrentCell.ColumnIndex;
   }
   if(kind=="panel"||kind=="group"||kind=="page"||kind=="tabs")node["children"]=c.Controls.Cast<Control>().Reverse().Select(child=>Widget(child,views,depth+1)).Where(child=>child!=null).ToArray();
   return node;
  }
  Dictionary<string,object> Catalog(){root.PerformLayout();var pages=new List<object>{new Dictionary<string,object>{{"id","global"},{"text","Điều khiển chung"}}};foreach(TabPage page in tabs.TabPages)pages.Add(new Dictionary<string,object>{{"id",Id(page)},{"text",page.Text}});return new Dictionary<string,object>{{"ok",true},{"ui_session",session},{"ui_version",2},{"backend_pid",System.Diagnostics.Process.GetCurrentProcess().Id},{"pages",pages.ToArray()}};}
  Dictionary<string,object> ReadFrame(Dictionary<string,object> request){
   var frame=Frame(Text(request,"page_id"));bool modal=ModalOpen;if(!modal){Context(request);var tab=frame as TabPage;if(tab!=null&&!Object.ReferenceEquals(tabs.SelectedTab,tab))tabs.SelectedTab=tab;frame.PerformLayout();}
   var views=Map(Get(request,"grid_views"))??new Dictionary<string,object>();
   object[] children=frame.Controls.Cast<Control>().Reverse().Where(c=>!Object.ReferenceEquals(frame,root)||c.Top<120).Select(c=>Widget(c,views,0)).Where(c=>c!=null).Cast<object>().ToArray();
   var ops=operations.Values.Select(o=>new Dictionary<string,object>{{"id",o.Id},{"control_id",o.Control},{"state",o.State},{"error",o.Error}}).ToArray();
   foreach(string id in operations.Where(p=>p.Value.State!="queued"&&p.Value.State!="running"&&(DateTime.UtcNow-p.Value.At).TotalMinutes>5).Select(p=>p.Key).ToArray())operations.Remove(id);
   if(controls.Count>2000)foreach(var pair in controls.Where(p=>p.Value.IsDisposed).ToArray()){controls.Remove(pair.Key);names.Remove(pair.Value);}
   return new Dictionary<string,object>{{"ok",true},{"ui_session",session},{"page_id",Text(request,"page_id")},{"account_id",CurrentAccount()},{"width",frame.ClientSize.Width},{"height",frame.ClientSize.Height},{"children",children},{"modal",modal},{"modal_title",ModalTitle()},{"status",status()},{"operations",ops}};
  }
  static void Raise(Control control,string method,EventArgs args){var m=control.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic);Require(m!=null,"Sự kiện không được hỗ trợ.");m.Invoke(control,new object[]{args});}
  void ValidateGrid(DataGridView grid,Dictionary<string,object> request){
   long epoch;Require(Int64.TryParse(Text(request,"epoch"),out epoch)&&epoch==Version(grid).Value,"Bảng dữ liệu đã đổi; nạp lại trước khi thao tác.");int row=Number(request,"row",-1);Require(row>=0&&row<grid.Rows.Count&&!grid.Rows[row].IsNewRow&&RowKey(grid.Rows[row])==Text(request,"row_key"),"Dòng đã thay đổi; chọn lại trước khi thao tác.");
  }
  void Change(Control target,Dictionary<string,object> patch){
   Require(target.Enabled,"Điều khiển đang bị khóa.");string kind=Text(patch,"event");
   var check=target as CheckBox;if(kind=="check"&&check!=null){Require(Get(patch,"value") is bool,"Giá trị chọn không hợp lệ.");check.Checked=Flag(patch,"value");return;}
   var number=target as NumericUpDown;if(kind=="number"&&number!=null){decimal value;Require(Decimal.TryParse(Text(patch,"value"),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>=number.Minimum&&value<=number.Maximum,"Giá trị nằm ngoài giới hạn.");number.Value=value;return;}
   var text=target as TextBoxBase;if(kind=="text"&&text!=null){string value=Text(patch,"value");Require(!text.ReadOnly&&value.Length<=Math.Max(text.MaxLength,0),"Ô nhập chỉ đọc hoặc nội dung quá dài.");text.Text=value;Raise(text,"OnLeave",EventArgs.Empty);return;}
   var combo=target as ComboBox;if(kind=="select"&&combo!=null){Require(ItemsKey(combo.Items)==Text(patch,"items_key"),"Danh sách lựa chọn đã đổi.");int index=Number(patch,"index",-1);Require(index>=-1&&index<combo.Items.Count,"Lựa chọn không hợp lệ.");combo.SelectedIndex=index;if(index<0&&combo.DropDownStyle!=ComboBoxStyle.DropDownList)combo.Text=Text(patch,"value");Raise(combo,"OnSelectionChangeCommitted",EventArgs.Empty);return;}
   var list=target as ListBox;if(list!=null&&(kind=="select"||kind=="item_check")){
    Require(ItemsKey(list.Items)==Text(patch,"items_key"),"Danh sách lựa chọn đã đổi.");int index=Number(patch,"index",-1);Require(index>=0&&index<list.Items.Count,"Lựa chọn không hợp lệ.");
    if(kind=="item_check"){var checks=list as CheckedListBox;Require(checks!=null&&Get(patch,"value") is bool,"Lựa chọn chiêu không hợp lệ.");checks.SetItemChecked(index,Flag(patch,"value"));}else list.SelectedIndex=index;return;
   }
   var grid=target as DataGridView;if(grid!=null&&(kind=="grid_select"||kind=="grid_edit"||kind=="grid_click"||kind=="grid_double")){
    ValidateGrid(grid,patch);int row=Number(patch,"row",-1),column=Number(patch,"column",0);Require(column>=0&&column<grid.Columns.Count,"Cột không hợp lệ.");var selected=List(Get(patch,"selected_rows")).Select(Convert.ToInt32).ToArray();Require(selected.All(n=>n>=0&&n<grid.Rows.Count&&!grid.Rows[n].IsNewRow),"Dòng chọn không hợp lệ.");grid.CurrentCell=grid.Rows[row].Cells[column];grid.ClearSelection();if(selected.Length==0)grid.Rows[row].Selected=true;else foreach(int n in selected)grid.Rows[n].Selected=true;
    if(kind=="grid_edit"){Require(!grid.ReadOnly&&!grid.Columns[column].ReadOnly&&!grid.Rows[row].Cells[column].ReadOnly,"Ô dữ liệu chỉ đọc.");grid.Rows[row].Cells[column].Value=Get(patch,"value");Raise(grid,"OnCellEndEdit",new DataGridViewCellEventArgs(column,row));}
    if(kind=="grid_click")Raise(grid,"OnCellContentClick",new DataGridViewCellEventArgs(column,row));
    if(kind=="grid_double")Raise(grid,"OnCellDoubleClick",new DataGridViewCellEventArgs(column,row));return;
   }
   throw new InvalidOperationException("Thao tác không phù hợp với điều khiển.");
  }
  Dictionary<string,object> Event(Dictionary<string,object> request){
   Require(Text(request,"ui_session")==session,"Bộ điều phối đã mở lại; nạp lại các tab.");Require(!ModalOpen,"Hoàn tất cửa sổ phụ trước khi thao tác tiếp.");Context(request);Control frame=Frame(Text(request,"page_id"));var tab=frame as TabPage;if(tab!=null)tabs.SelectedTab=tab;
   var target=Resolve(Text(request,"control_id"));InFrame(target,frame);Require(target.Enabled,"Điều khiển đang bị khóa.");
   if(Text(request,"event")=="click"){
    var button=target as Button;Require(button!=null,"Nút không hợp lệ.");
    foreach(object guard in List(Get(request,"guards"))){var g=Map(guard);var table=Resolve(Text(g,"control_id")) as DataGridView;Require(table!=null,"Bảng đã đổi.");InFrame(table,frame);ValidateGrid(table,g);}
    Require(operations.Values.Count(o=>o.State=="queued"||o.State=="running")<32,"Đang có nhiều thao tác chờ; đợi hoàn tất.");
    Require(!operations.Values.Any(o=>o.Control==Id(target)&&(o.State=="queued"||o.State=="running")),"Nút này đang xử lý; không gửi lại.");
    var operation=new Operation{Id=Guid.NewGuid().ToString("N"),Control=Id(target)};operations[operation.Id]=operation;var ignored=root.Handle;
    root.BeginInvoke((MethodInvoker)delegate{
     operation.State="running";try{Context(request);var requestedPage=frame as TabPage;if(requestedPage!=null)tabs.SelectedTab=requestedPage;Require(!target.IsDisposed&&target.Enabled,"Nút không còn sẵn sàng.");foreach(object guard in List(Get(request,"guards"))){var g=Map(guard);ValidateGrid((DataGridView)Resolve(Text(g,"control_id")),g);}Raise(button,"OnClick",EventArgs.Empty);operation.State="done";}catch{operation.State="failed";operation.Error="Thao tác chưa hoàn tất; kiểm tra trạng thái hoặc cửa sổ phụ.";}
    });
    return new Dictionary<string,object>{{"ok",true},{"operation_id",operation.Id},{"queued",true}};
   }
   Change(target,request);return new Dictionary<string,object>{{"ok",true},{"status",status()}};
  }
  Dictionary<string,object> Flags(Dictionary<string,object> request){Require(!ModalOpen,"Hoàn tất cửa sổ phụ trước.");var rows=Rows();string id=Text(request,"account_id"),column=Text(request,"column");Require(rows.ContainsKey(id)&&(column=="auto"||column=="up")&&Get(request,"value") is bool,"Tài khoản hoặc tùy chọn không hợp lệ.");accounts.CurrentCell=rows[id].Cells[column];rows[id].Cells[column].Value=Flag(request,"value");return new Dictionary<string,object>{{"ok",true}};}
  public string Handle(string json){
   var serializer=new JavaScriptSerializer{MaxJsonLength=8388608,RecursionLimit=64};
   try{var request=Map(serializer.DeserializeObject(json));string command=Text(request,"command");Dictionary<string,object> result;
    if(command=="manager_ui_catalog")result=Catalog();else if(command=="manager_ui_frame")result=ReadFrame(request);else if(command=="manager_ui_event")result=Event(request);else if(command=="manager_ui_flags")result=Flags(request);else if(command=="manager_ui_dialog"){var dialog=Modal();if(dialog!=null){dialog.BringToFront();dialog.Activate();}else{IntPtr handle=NativeDialog();if(handle!=IntPtr.Zero)SetForegroundWindow(handle);}result=new Dictionary<string,object>{{"ok",true}};}else throw new InvalidOperationException("Lệnh không hợp lệ.");
    return serializer.Serialize(result);
   }catch(InvalidOperationException error){return serializer.Serialize(new Dictionary<string,object>{{"ok",false},{"error","ui_changed"},{"message",error.Message}});}
   catch{return serializer.Serialize(new Dictionary<string,object>{{"ok",false},{"error","ui_operation_failed"},{"message","Chưa thực hiện được thao tác. Nạp lại tab và kiểm tra cửa sổ phụ hoặc trạng thái tài khoản."}});}
  }
 }
}
