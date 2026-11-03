using System;using System.Diagnostics;using System.Threading;using System.Windows.Forms;using TGLL;
class MoveProbeForm : SmoothManagerForm {public void MoveMessage(int id){var m=Message.Create(Handle,id,IntPtr.Zero,IntPtr.Zero);base.WndProc(ref m);}}
class MoveFormTests {
 static void C(bool value,string message){if(!value)throw new Exception(message);}
 static void Pump(int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(1);}}
 [STAThread]static void Main(){
  using(var form=new MoveProbeForm())using(var active=new System.Windows.Forms.Timer())using(var stopped=new System.Windows.Forms.Timer()){
   var handle=form.Handle;int ticks=0,resizes=0;active.Interval=10;active.Tick+=delegate{ticks++;};form.WatchTimer(active);form.WatchTimer(active);form.WatchTimer(stopped);active.Start();Pump(80);C(ticks>0,"fixture timer not running");
   int workerTicks=0;bool running=true;var worker=new Thread(delegate(){while(Volatile.Read(ref running)){Interlocked.Increment(ref workerTicks);Thread.Sleep(1);}});worker.Start();
   try{
    form.Resize+=delegate{resizes++;};form.MoveMessage(0x231);form.BeginUserMove();int before=ticks;
    C(!active.Enabled&&!stopped.Enabled,"UI timers not paused at native move entry");
    for(int n=0;n<50;n++)form.Width++;
    Pump(250);C(ticks==before,"UI timer entered managed callbacks during move");C(resizes==0,"resize callback fired for every intermediate size");C(workerTicks>10,"background work stopped during move");
    form.MoveMessage(0x232);C(resizes==1,"final resize not coalesced exactly once");C(active.Enabled&&!stopped.Enabled,"timer running state not restored");Pump(80);C(ticks>before,"timer did not resume");form.MoveMessage(0x232);C(resizes==1,"duplicate move end repeated layout");
   }finally{Volatile.Write(ref running,false);worker.Join();}
  }
  Console.WriteLine("PASS: move entry pauses active UI timers, stopped timers stay stopped, zero timer/resize callbacks during 250ms move, one final resize, timers resume and worker continues. No real app input.");
 }
}
