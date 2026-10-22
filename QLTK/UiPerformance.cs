using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.Windows.Forms.VisualStyles;
namespace TGLL {
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
