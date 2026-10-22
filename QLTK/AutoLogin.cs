using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TGLL {
    public sealed class LoginJob {
        public readonly string Id = Guid.NewGuid().ToString();
        public readonly string Username;
        public readonly DateTime Created = DateTime.UtcNow;
        public volatile string State = "queued";
        public volatile string Error = "";
        public volatile int ProcessId;
        public volatile bool CancelRequested;
        internal string Password;
        internal string Target;
        internal bool Existing;
        internal LoginJob(string target, string username, string password, int existingPid) {
            Target = Path.GetFullPath(target); Username = username; Password = password;
            Existing = existingPid > 0; ProcessId = existingPid;
        }
    }
    public static class AutoLogin {
        private static readonly BlockingCollection<LoginJob> queue = new BlockingCollection<LoginJob>(200);
        private static readonly ConcurrentDictionary<string, LoginJob> jobs = new ConcurrentDictionary<string, LoginJob>();
        private static readonly Thread worker;
        private static volatile LoginJob active;
        static AutoLogin() { worker = new Thread(Work); worker.IsBackground = true; worker.Start(); }
        public static LoginJob Enqueue(string target, string username, string password, int existingPid) {
            if (!File.Exists(target) || !String.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("game_invalid");
            if (String.IsNullOrEmpty(username) || String.IsNullOrEmpty(password) || username.Length > 128 || password.Length > 128) throw new ArgumentException("login_length_invalid");
            if (username.IndexOfAny(new char[]{'\r','\n','\t','\0'}) >= 0 || password.IndexOfAny(new char[]{'\r','\n','\t','\0'}) >= 0) throw new ArgumentException("login_characters_invalid");
            LoginJob job = new LoginJob(target, username, password, existingPid);
            jobs.TryAdd(job.Id, job);
            if (!queue.TryAdd(job)) { LoginJob ignored; jobs.TryRemove(job.Id, out ignored); job.Password = null; throw new InvalidOperationException("login_queue_full"); }
            foreach (var pair in jobs) {
                if (pair.Value.Created < DateTime.UtcNow.AddHours(-6) && (pair.Value.State == "submitted" || pair.Value.State == "failed")) { LoginJob ignored; jobs.TryRemove(pair.Key, out ignored); }
            }
            return job;
        }
        public static LoginJob Get(string id) { LoginJob job; return jobs.TryGetValue(id, out job) ? job : null; }
        public static LoginJob[] List() { return new List<LoginJob>(jobs.Values).ToArray(); }
        public static void CancelAll() {foreach(LoginJob job in jobs.Values) if(job.State!="submitted" && job.State!="failed") {job.CancelRequested=true;if(job.State=="queued") {job.Password=null;job.State="cancelled";}}}
        private static void Work() {
            foreach (LoginJob job in queue.GetConsumingEnumerable()) {
                if(job.CancelRequested) {job.Password=null;job.State="cancelled";continue;}
                active=job;
                try { Run(job); }
                catch (OperationCanceledException) { job.Error=job.CancelRequested ? "cancelled" : "focus_lost"; job.State=job.CancelRequested ? "cancelled" : "failed"; }
                catch (Exception e) {
                    string code=e.Message;
                    if (code != "game_exited" && code != "login_screen_not_found" && code != "window_not_found" && code != "wrong_process" && code != "input_failed" && code != "focus_not_acquired") code="launch_or_input_failed";
                    job.Error=code; job.State="failed";
                } finally { job.Password=null; active=null; }
            }
        }
        private static void Run(LoginJob job) {
            job.State="starting";
            using (Process process = job.Existing ? Process.GetProcessById(job.ProcessId) : Process.Start(new ProcessStartInfo(job.Target) { WorkingDirectory=Path.GetDirectoryName(job.Target), UseShellExecute=false })) {
                if (process==null) throw new Exception("game_exited");
                job.ProcessId=process.Id;
                IntPtr window=IntPtr.Zero;
                Stopwatch wait=Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 30000) {
                    if(job.CancelRequested) throw new OperationCanceledException();
                    if (process.HasExited) throw new Exception("game_exited");
                    process.Refresh(); window=process.MainWindowHandle;
                    if (window != IntPtr.Zero && IsWindowVisible(window)) break;
                    Thread.Sleep(200);
                }
                if (window==IntPtr.Zero) throw new Exception("window_not_found");
                VerifyProcess(window, process.Id, job.Target);
                AcquireFocus(window);
                CheckFocus(window);
                bool ready=false;
                wait.Restart();
                while(wait.ElapsedMilliseconds < 15000) {
                    CheckFocus(window);
                    using(Bitmap image=Capture(window)) { if(IsLoginScreen(image)) {ready=true;break;} }
                    Thread.Sleep(250);
                }
                if(!ready) throw new Exception("login_screen_not_found");
                job.State="typing";
                Fill(window,0.505,0.535,job.Username);
                Fill(window,0.505,0.637,job.Password);
                // Recheck the layout and identity immediately before submitting.
                VerifyProcess(window, process.Id, job.Target);
                using(Bitmap image=Capture(window)) { if(!IsLoginScreen(image)) throw new Exception("login_screen_not_found"); }
                Click(window,0.520,0.864);
                job.State="submitted"; // Submission is not proof of server acceptance.
            }
        }
        public static bool IsLoginScreen(Bitmap bitmap) {
            if(bitmap.Width<300 || bitmap.Height<180) return false;
            double ratio=(double)bitmap.Width/bitmap.Height;
            if(ratio<1.45 || ratio>2.15) return false;
            return Gold(bitmap,.377,.505,.030,.058)>.12 && Gold(bitmap,.377,.610,.030,.064)>.12 && Gold(bitmap,.420,.820,.180,.080)>.20;
        }
        private static double Gold(Bitmap image,double x,double y,double w,double h) {
            int yes=0,total=0;
            for(int px=(int)(image.Width*x);px<(int)(image.Width*(x+w));px+=2)
            for(int py=(int)(image.Height*y);py<(int)(image.Height*(y+h));py+=2) {
                Color c=image.GetPixel(px,py); total++;
                if(c.R>140 && c.G>75 && c.B<155 && c.R>c.G+10 && c.G>c.B+12) yes++;
            }
            return total==0 ? 0 : (double)yes/total;
        }
        private static Bitmap Capture(IntPtr window) {
            CheckFocus(window); RECT rect; if(!GetClientRect(window,out rect)) throw new Exception("window_not_found");
            POINT point=new POINT(); if(!ClientToScreen(window,ref point)) throw new Exception("window_not_found");
            Bitmap image=new Bitmap(rect.Right,rect.Bottom);
            try {using(Graphics g=Graphics.FromImage(image)) g.CopyFromScreen(point.X,point.Y,0,0,image.Size); CheckFocus(window); return image;}
            catch {image.Dispose();throw;}
        }
        private static void Fill(IntPtr window,double x,double y,string text) {
            Click(window,x,y); Thread.Sleep(150);
            // Unity's custom text fields may ignore Ctrl+A; Backspace also clears remembered values.
            Key(window,0x11,false); try {Key(window,0x41,false);Key(window,0x41,true);} finally {Release(0x11);}
            Thread.Sleep(80); Press(window,0x23); // End
            for(int i=0;i<128;i++) {Press(window,0x08);Thread.Sleep(15);}
            foreach(char c in text) {
                CheckFocus(window);
                INPUT down=Keyboard(0,c,4), up=Keyboard(0,c,6);
                Send(window,new INPUT[]{down,up});Thread.Sleep(70);
            }
            Thread.Sleep(150);
        }
        private static void Click(IntPtr window,double x,double y) {
            CheckFocus(window); RECT rect; GetClientRect(window,out rect);
            POINT point=new POINT{X=(int)(rect.Right*x),Y=(int)(rect.Bottom*y)};
            ClientToScreen(window,ref point); if(!SetCursorPos(point.X,point.Y)) throw new Exception("input_failed");
            CheckFocus(window); INPUT down=new INPUT{Type=0},up=new INPUT{Type=0};
            down.Data.Mouse.Flags=2;up.Data.Mouse.Flags=4;Send(window,new INPUT[]{down,up});Thread.Sleep(120);
        }
        private static void Press(IntPtr window,ushort key) {Send(window,new INPUT[]{Keyboard(key,0,0),Keyboard(key,0,2)});}
        private static void Key(IntPtr window,ushort key,bool up) {Send(window,new INPUT[]{Keyboard(key,0,up ? 2u : 0u)});}
        private static void Release(ushort key) {INPUT input=Keyboard(key,0,2);SendInput(1,new INPUT[]{input},Marshal.SizeOf(typeof(INPUT)));}
        private static INPUT Keyboard(ushort key,ushort character,uint flags) {INPUT input=new INPUT{Type=1};input.Data.Key.VirtualKey=key;input.Data.Key.Scan=character;input.Data.Key.Flags=flags;return input;}
        private static void Send(IntPtr window,INPUT[] inputs) {CheckFocus(window);if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))!=(uint)inputs.Length)throw new Exception("input_failed");}
        private static void AcquireFocus(IntPtr window) {
            ShowWindow(window,9);
            Stopwatch timeout=Stopwatch.StartNew();
            while(timeout.ElapsedMilliseconds<5000) {
                if(active!=null && active.CancelRequested) throw new OperationCanceledException();
                IntPtr foreground=GetForegroundWindow();
                uint ignored;
                uint foregroundThread=GetWindowThreadProcessId(foreground,out ignored);
                uint ownThread=GetCurrentThreadId();
                bool attached=false;
                try {
                    if(foregroundThread!=0 && foregroundThread!=ownThread) attached=AttachThreadInput(ownThread,foregroundThread,true);
                    BringWindowToTop(window);
                    SetForegroundWindow(window);
                } finally {
                    if(attached) AttachThreadInput(ownThread,foregroundThread,false);
                }
                Thread.Sleep(400);
                if(GetForegroundWindow()==window) return;
                Thread.Sleep(100);
            }
            throw new Exception("focus_not_acquired");
        }
        private static void CheckFocus(IntPtr window) {if((active!=null && active.CancelRequested) || GetForegroundWindow()!=window)throw new OperationCanceledException();}
        private static void VerifyProcess(IntPtr window,int pid,string path) {
            uint actual; GetWindowThreadProcessId(window,out actual);
            if(actual!=(uint)pid)throw new Exception("wrong_process");
            using(Process p=Process.GetProcessById(pid)) if(!String.Equals(Path.GetFullPath(p.MainModule.FileName),path,StringComparison.OrdinalIgnoreCase))throw new Exception("wrong_process");
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] private struct POINT {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] private struct INPUT {public uint Type;public INPUTUNION Data;}
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION {[FieldOffset(0)]public MOUSEINPUT Mouse;[FieldOffset(0)]public KEYBDINPUT Key;}
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT {public int X,Y;public uint MouseData,Flags,Time;public IntPtr Extra;}
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT {public ushort VirtualKey,Scan;public uint Flags,Time;public IntPtr Extra;}
        [DllImport("user32.dll")]private static extern uint SendInput(uint count,INPUT[] inputs,int size);
        [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint source,uint target,bool attach);
        [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref POINT point);
        [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    }
}
