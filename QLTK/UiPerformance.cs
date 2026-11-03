using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.Windows.Forms.VisualStyles;
namespace TGLL {
 public class SmoothManagerForm : Form {
  readonly System.Collections.Generic.List<Timer> watchedTimers=new System.Collections.Generic.List<Timer>();
  readonly System.Collections.Generic.List<Timer> pausedTimers=new System.Collections.Generic.List<Timer>();
  bool moving,deferredResize;
  public void WatchTimer(Timer timer){if(timer!=null&&!watchedTimers.Contains(timer))watchedTimers.Add(timer);}
  internal void BeginUserMove(){
   if(moving)return;moving=true;deferredResize=false;SuspendLayout();
   foreach(Timer timer in watchedTimers)if(timer.Enabled){pausedTimers.Add(timer);timer.Stop();}
  }
  internal void EndUserMove(){
   if(!moving)return;moving=false;ResumeLayout(false);
   try{if(deferredResize){deferredResize=false;base.OnResize(EventArgs.Empty);}}
   finally{foreach(Timer timer in pausedTimers){try{if(!IsDisposed&&!Disposing)timer.Start();}catch(ObjectDisposedException){}}pausedTimers.Clear();}
  }
  protected override void OnResize(EventArgs e){if(moving){deferredResize=true;return;}base.OnResize(e);}
  protected override void WndProc(ref Message message){
   if(message.Msg==0x231)BeginUserMove();
   try{base.WndProc(ref message);}finally{if(message.Msg==0x232)EndUserMove();}
  }
 }
 public sealed class AccountPrivacy {
  readonly DataGridView grid;bool enabled;
  public AccountPrivacy(DataGridView grid){this.grid=grid;grid.CellFormatting+=FormatCell;}
  public bool Enabled {get{return enabled;}set{if(enabled==value)return;enabled=value;foreach(string name in new[]{"username","character","proxy"})if(grid.Columns.Contains(name))grid.InvalidateColumn(grid.Columns[name].Index);}}
  public string Format(string column,string value){
   if(!enabled||(column!="username"&&column!="character"&&column!="proxy")||String.IsNullOrEmpty(value)||value=="—"||value=="Không dùng")return value;
   if(value.Length<=2)return "***";
   int edge=value.Length>4?2:1;return value.Substring(0,edge)+"***"+value.Substring(value.Length-edge);
  }
  void FormatCell(object sender,DataGridViewCellFormattingEventArgs e){if(!enabled||e.RowIndex<0||e.ColumnIndex<0)return;string name=grid.Columns[e.ColumnIndex].Name;if(name!="username"&&name!="character"&&name!="proxy")return;e.Value=Format(name,Convert.ToString(e.Value));e.FormattingApplied=true;}
 }
 public sealed class ResponsiveGrid : DataGridView {
  public ResponsiveGrid(){DoubleBuffered=true;AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;}
  protected override void WndProc(ref Message message){
   if(message.Msg==0x20a||message.Msg==0x20e||message.Msg==0x114||message.Msg==0x115)UiPerformance.NoteScroll();
   base.WndProc(ref message);
  }
 }
 public static class UiPerformance {
  static long scrollUntil;
  public static bool IsScrolling {get{return System.Diagnostics.Stopwatch.GetTimestamp()<System.Threading.Interlocked.Read(ref scrollUntil);}}
  public static void NoteScroll(){System.Threading.Interlocked.Exchange(ref scrollUntil,System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency/4);}
  public static void Buffer(Control control){
   typeof(Control).GetProperty("DoubleBuffered",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(control,true,null);
  }
  public static void InstallOpenPainting(DataGridView grid){grid.CellPainting+=PaintOpen;Buffer(grid);}
  static void PaintOpen(object sender,DataGridViewCellPaintingEventArgs e){
   var grid=(DataGridView)sender;
   if(e.RowIndex<0||e.ColumnIndex<0||grid.Columns[e.ColumnIndex].Name!="open"||!Object.Equals(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag,true))return;
   e.PaintBackground(e.ClipBounds,true);e.Paint(e.ClipBounds,DataGridViewPaintParts.Border);Rectangle rect=e.CellBounds;rect.Inflate(-2,-2);
   ButtonRenderer.DrawButton(e.Graphics,rect,"OPEN",grid.Font,false,PushButtonState.Disabled);e.Handled=true;
  }
 }
}
