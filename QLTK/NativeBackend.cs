using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TGLL {
    // Bound startup concurrency; a slow login/popup must not block every account.
    internal sealed class LaunchQueue : IDisposable {
        readonly BlockingCollection<NativeJob> pending;
        readonly List<Thread> workers=new List<Thread>();readonly object gate=new object();readonly Action<NativeJob> run;int limit,active;bool stopping;
        internal LaunchQueue(int concurrency,int capacity,Action<NativeJob> run){
            pending=new BlockingCollection<NativeJob>(capacity);this.run=run;SetConcurrency(concurrency);
        }
        internal void SetConcurrency(int value){if(value<1||value>100)throw new ArgumentOutOfRangeException("value");lock(gate){limit=value;while(!stopping&&workers.Count<value){Thread worker=new Thread(Consume);worker.IsBackground=true;workers.Add(worker);worker.Start();}Monitor.PulseAll(gate);}}
        void Consume(){while(true){NativeJob job;lock(gate){while(true){if(stopping&&pending.Count==0)return;if(active<limit&&pending.TryTake(out job)){active++;break;}Monitor.Wait(gate);}}try{run(job);}finally{lock(gate){active--;Monitor.PulseAll(gate);}}}}
        internal bool TryAdd(NativeJob job){lock(gate){if(stopping)return false;bool added=pending.TryAdd(job);Monitor.PulseAll(gate);return added;}}
        public void Dispose(){Thread[] snapshot;lock(gate){stopping=true;pending.CompleteAdding();snapshot=workers.ToArray();Monitor.PulseAll(gate);}foreach(Thread worker in snapshot)worker.Join(1000);}
    }
    internal sealed class LaunchPacer {
        readonly object gate=new object();readonly Stopwatch clock=Stopwatch.StartNew();long last=-1;int delay;
        internal void SetDelay(int milliseconds){if(milliseconds<0||milliseconds>3600000)throw new ArgumentOutOfRangeException("milliseconds");lock(gate){delay=milliseconds;Monitor.PulseAll(gate);}}
        internal void Wait(Action check){lock(gate){while(true){check();long remaining=last<0?0:delay-(clock.ElapsedMilliseconds-last);if(remaining<=0){last=clock.ElapsedMilliseconds;return;}Monitor.Wait(gate,(int)Math.Min(remaining,50));}}}
    }
    internal sealed class FileHashCache {
        sealed class Entry {internal long Length,Written;internal string Hash;}
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        internal int Calculations;
        internal string Read(string path){
            path=Path.GetFullPath(path);
            lock(entries){
                FileInfo before=new FileInfo(path);long length=before.Length,written=before.LastWriteTimeUtc.Ticks;
                Entry entry;if(entries.TryGetValue(path,out entry)&&entry.Length==length&&entry.Written==written)return entry.Hash;
                string hash;using(SHA256 sha=SHA256.Create())using(FileStream f=File.OpenRead(path))hash=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
                Calculations++;FileInfo after=new FileInfo(path);
                if(after.Length!=length||after.LastWriteTimeUtc.Ticks!=written){entries.Remove(path);throw new Exception("native_build_changed");}
                entries[path]=new Entry{Length=length,Written=written,Hash=hash};return hash;
            }
        }
    }
    public sealed class VitalsSnapshot {
        public readonly int HP,MP,MaxHP,MaxMP;public readonly DateTime CapturedAt;
        internal VitalsSnapshot(int hp,int mp,int maxHp,int maxMp,DateTime at){HP=hp;MP=mp;MaxHP=maxHp;MaxMP=maxMp;CapturedAt=at;}
    }
    public sealed class CloseGamesResult { public int Closed,Failed; }
    public sealed class CloseGamesOperation {
        volatile bool completed;public bool Completed {get{return completed;}}
        public CloseGamesResult Result {get;private set;}public string Error {get;private set;}
        internal static CloseGamesOperation Start(Func<CloseGamesResult> action){
            var op=new CloseGamesOperation();var worker=new Thread(delegate(){try{op.Result=action();}catch(Exception e){op.Error=e.Message;}finally{op.completed=true;}});worker.IsBackground=true;worker.Start();return op;
        }
    }
    public sealed class CloseBatchOperation {public volatile bool Completed;public volatile int Failed;public string Error="";}
    public sealed class ArrangeGamesResult { public int Arranged,Failed,NotReady; }
    public sealed class ArrangeGamesOperation {
        volatile bool completed;public bool Completed {get{return completed;}}
        public ArrangeGamesResult Result {get;private set;}public string Error {get;private set;}
        internal static ArrangeGamesOperation Start(Func<ArrangeGamesResult> action){
            ArrangeGamesOperation operation=new ArrangeGamesOperation();
            Thread worker=new Thread(delegate(){try{operation.Result=action();}catch(Exception e){operation.Error=e.Message;}finally{operation.completed=true;}});
            worker.IsBackground=true;worker.Start();return operation;
        }
    }
    public sealed class VisibilityGamesResult { public int Changed,Failed,NotReady; }
    public static class ManagedGames {
        internal static readonly object Gate=new object();
        sealed class Identity {internal int Id;internal long Started;internal string Path;}
        static readonly Dictionary<int,Identity> games=new Dictionary<int,Identity>();
        public static void Register(Process process,string target) {
            string path=System.IO.Path.GetFullPath(target);
            // MainModule may expose an incomplete loader entry at process startup.
            // Query the kernel's executable path and creation time instead.
            Stopwatch ready=Stopwatch.StartNew();
            while(true){
                if(process.HasExited)throw new Exception("game_exited");
                try{
                    string actual;long started;ReadIdentity(process.Id,out actual,out started);
                    if(process.HasExited)throw new Exception("game_exited");
                    if(!String.Equals(path,actual,StringComparison.OrdinalIgnoreCase))throw new Exception("native_wrong_process");
                    lock(Gate)games[process.Id]=new Identity{Id=process.Id,Started=started,Path=path};
                    return;
                }catch(System.ComponentModel.Win32Exception e){
                    if(e.NativeErrorCode!=299&&e.NativeErrorCode!=6&&e.NativeErrorCode!=87)throw;
                }
                if(ready.ElapsedMilliseconds>=5000)throw new Exception("native_process_not_ready");
                Thread.Sleep(25);
            }
        }
        internal static void ReadIdentity(int pid,out string path,out long started){
            IntPtr handle=OpenProcessIdentity(0x1000,false,pid);
            if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try{
                StringBuilder buffer=new StringBuilder(32768);int size=buffer.Capacity;
                if(!QueryFullProcessImageName(handle,0,buffer,ref size))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                long creation,exit,kernel,user;
                if(!GetProcessTimes(handle,out creation,out exit,out kernel,out user))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                path=System.IO.Path.GetFullPath(buffer.ToString());started=DateTime.FromFileTimeUtc(creation).Ticks;
            }finally{CloseIdentityHandle(handle);}
        }
        static bool Matches(Process process,Identity game){
            if(process.HasExited)return false;
            string actual;long started;ReadIdentity(process.Id,out actual,out started);
            return started==game.Started&&String.Equals(actual,game.Path,StringComparison.OrdinalIgnoreCase);
        }
        [DllImport("kernel32.dll",EntryPoint="OpenProcess",SetLastError=true)]static extern IntPtr OpenProcessIdentity(int access,bool inherit,int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll",EntryPoint="CloseHandle")]static extern bool CloseIdentityHandle(IntPtr handle);

        // Tile only windows belonging to processes started by this QLTK session.
        public static ArrangeGamesResult ArrangeWindows(int left,int top,int width,int height) {
            if(width<1||height<1)throw new ArgumentException("screen_invalid");
            List<Identity> ordered;lock(Gate){ordered=new List<Identity>(games.Values);}
            // Window calls must never hold the registry gate: visibility/status polling
            // and process registration continue even while a game window is slow.
            {
                ArrangeGamesResult result=new ArrangeGamesResult();
                ordered.Sort(delegate(Identity a,Identity b){return a.Started.CompareTo(b.Started);});
                List<IntPtr> windows=new List<IntPtr>();int cellWidth=1,cellHeight=1;
                foreach(Identity game in ordered){
                    try{using(Process p=Process.GetProcessById(game.Id)){
                        if(!Matches(p,game))continue;
                        IntPtr hwnd=FindGameWindow(p.Id);if(hwnd==IntPtr.Zero){result.NotReady++;continue;}
                        RECT rect;if(!GetWindowRect(hwnd,out rect)){result.Failed++;continue;}
                        if(IsIconic(hwnd)||IsZoomed(hwnd)){
                            WINDOWPLACEMENT placement=new WINDOWPLACEMENT();placement.length=Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                            if(!GetWindowPlacement(hwnd,ref placement)){result.Failed++;continue;}
                            rect=placement.normal;ShowWindowAsync(hwnd,4);
                        }
                        cellWidth=Math.Max(cellWidth,rect.right-rect.left);cellHeight=Math.Max(cellHeight,rect.bottom-rect.top);windows.Add(hwnd);
                    }}catch(ArgumentException){}catch{result.Failed++;}
                }
                for(int i=0;i<windows.Count;i++){
                    int[] position=TilePosition(i,cellWidth,cellHeight,width,height);
                    if(SetWindowPos(windows[i],IntPtr.Zero,left+position[0],top+position[1],0,0,0x4015)){result.Arranged++;}else{result.Failed++;}
                }
                return result;
            }
        }
        public static ArrangeGamesOperation BeginArrangeWindows(int left,int top,int width,int height){
            if(width<1||height<1)throw new ArgumentException("screen_invalid");
            return ArrangeGamesOperation.Start(delegate(){return ArrangeWindows(left,top,width,height);});
        }
        internal static int[] TilePosition(int index,int cellWidth,int cellHeight,int width,int height){
            int columns=Math.Max(1,width/cellWidth),rows=Math.Max(1,height/cellHeight),capacity=columns*rows;
            int slot=index%capacity,page=index/capacity;
            return new int[]{Math.Min((slot%columns)*cellWidth+page*18,Math.Max(0,width-cellWidth)),Math.Min((slot/columns)*cellHeight+page*18,Math.Max(0,height-cellHeight))};
        }
        // MainWindowHandle omits hidden windows. Enumerate owned top-level game
        // windows so Show can recover them; validate process identity every time.
        internal static IntPtr PortalWindow(int pid){return IsRunning(pid)?FindGameWindow(pid):IntPtr.Zero;}
        static IntPtr FindGameWindow(int pid){
            IntPtr found=IntPtr.Zero;
            EnumWindow callback=delegate(IntPtr window,IntPtr arg){
                uint actual;GetWindowThreadProcessId(window,out actual);
                if(actual==(uint)pid&&GetWindow(window,4)==IntPtr.Zero&&
                    (GetWindowLong(window,-16)&0x00c00000)==0x00c00000){found=window;return false;}
                return true;
            };
            EnumWindows(callback,IntPtr.Zero);GC.KeepAlive(callback);return found;
        }
        public static VisibilityGamesResult SetAllVisible(bool visible){
            lock(Gate){
                VisibilityGamesResult result=new VisibilityGamesResult();
                foreach(Identity game in games.Values){
                    try{using(Process p=Process.GetProcessById(game.Id)){
                        if(!Matches(p,game))continue;
                        IntPtr window=FindGameWindow(p.Id);if(window==IntPtr.Zero){result.NotReady++;continue;}
                        if(IsWindowVisible(window)==visible){result.Changed++;continue;}
                        if(ShowWindowAsync(window,visible?4:0))result.Changed++;else result.Failed++;
                    }}catch(ArgumentException){}catch{result.Failed++;}
                }
                return result;
            }
        }
        static IntPtr ValidatedWindow(Identity game){
            using(Process p=Process.GetProcessById(game.Id)){
                if(!Matches(p,game))return IntPtr.Zero;
                return FindGameWindow(p.Id);
            }
        }
        // -1 means unavailable, 0 hidden, 1 visible. Query the actual window,
        // including changes made by Hide/Show All or outside this UI.
        public static int WindowVisibility(int pid){
            lock(Gate){Identity game;if(!games.TryGetValue(pid,out game))return -1;
                try{IntPtr window=ValidatedWindow(game);return window==IntPtr.Zero?-1:(IsWindowVisible(window)?1:0);}catch{return -1;}
            }
        }
        // Chụp danh sách một lần; kiểm tra identity ngoài khóa để không giữ UI.
        // Chỉ phục vụ hiển thị: dùng PID đã kiểm tra, quét cửa sổ một lần cho cả bảng.
        // Các nút mở/tắt/ẩn vẫn xác minh identity riêng ngay trước thao tác.
        public static Dictionary<int,int> WindowVisibilities(int[] livePids){
            Dictionary<int,int> result=new Dictionary<int,int>();
            lock(Gate){foreach(int pid in livePids??new int[0])if(games.ContainsKey(pid))result[pid]=-1;}
            if(result.Count==0)return result;
            EnumWindow callback=delegate(IntPtr window,IntPtr arg){
                uint actual;GetWindowThreadProcessId(window,out actual);int pid=unchecked((int)actual);
                if(result.ContainsKey(pid)&&result[pid]<0&&GetWindow(window,4)==IntPtr.Zero&&
                    (GetWindowLong(window,-16)&0x00c00000)==0x00c00000)result[pid]=IsWindowVisible(window)?1:0;
                return true;
            };
            EnumWindows(callback,IntPtr.Zero);GC.KeepAlive(callback);return result;
        }
        public static int[] RunningProcessIds(){
            List<Identity> snapshot;lock(Gate){snapshot=new List<Identity>(games.Values);}
            List<int> live=new List<int>();
            foreach(Identity game in snapshot){
                try{using(Process p=Process.GetProcessById(game.Id)){
                    if(Matches(p,game))live.Add(game.Id);else RemoveClosedIdentity(game.Id,game);
                }}catch(ArgumentException){RemoveClosedIdentity(game.Id,game);}catch{}
            }
            return live.ToArray();
        }
        public static bool IsRunning(int pid){
            lock(Gate){Identity game;if(!games.TryGetValue(pid,out game))return false;
                try{using(Process p=Process.GetProcessById(pid)){return Matches(p,game);}}catch{return false;}
            }
        }
        public static bool SetVisible(int pid,bool visible){
            lock(Gate){Identity game;if(!games.TryGetValue(pid,out game))return false;
                try{IntPtr window=ValidatedWindow(game);if(window==IntPtr.Zero)return false;
                    return IsWindowVisible(window)==visible||ShowWindowAsync(window,visible?4:0);
                }catch{return false;}
            }
        }
        delegate bool EnumWindow(IntPtr window,IntPtr arg);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr arg);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [StructLayout(LayoutKind.Sequential)]struct RECT {internal int left,top,right,bottom;}
        [StructLayout(LayoutKind.Sequential)]struct POINT {internal int x,y;}
        [StructLayout(LayoutKind.Sequential)]struct WINDOWPLACEMENT {internal int length,flags,showCmd;internal POINT min,max;internal RECT normal;}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr window,ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        public static CloseGamesResult CloseOne(int pid) {
            CloseGamesResult result=new CloseGamesResult();Identity game;
            lock(Gate){if(!games.TryGetValue(pid,out game))return result;}
                try{using(Process p=Process.GetProcessById(game.Id)){
                    if(!Matches(p,game)){RemoveClosedIdentity(pid,game);return result;}
                    NativeBackend.CaptureBiMaBeforeClose(pid);p.CloseMainWindow();if(!p.WaitForExit(100))p.Kill();
                    if(!p.WaitForExit(1000)){result.Failed++;return result;}
                    result.Closed++;NativeBackend.RecordBiMaClosed(pid,"manager_close");RemoveClosedIdentity(pid,game);
                }}catch(ArgumentException){RemoveClosedIdentity(pid,game);}catch{result.Failed++;}
                return result;
        }
        static void RemoveClosedIdentity(int pid,Identity expected){lock(Gate){Identity current;if(games.TryGetValue(pid,out current)&&Object.ReferenceEquals(current,expected))games.Remove(pid);}}
        internal static CloseGamesResult CloseSnapshot(int[] ids,Func<int,CloseGamesResult> close){
            var result=new CloseGamesResult();foreach(int id in ids){try{var item=close(id);result.Closed+=item.Closed;result.Failed+=item.Failed;}catch{result.Failed++;}}return result;
        }
        public static CloseGamesResult CloseAll() {
            int[] ids;lock(Gate){ids=games.Keys.ToArray();}
            // Chờ thoát ngoài khóa registry; giao diện vẫn đọc được trạng thái game.
            return CloseSnapshot(ids,CloseOne);
        }
    }
    // Only the exact analysed x64 build is supported. No file on disk is patched.
    public sealed class SocksConfig {
        public string Host, Username, Password; public int Port;
        public SocksConfig(string host,int port,string username,string password) {
            if(String.IsNullOrWhiteSpace(host)||port<1||port>65535)throw new ArgumentException("proxy_invalid");
            Host=host;Port=port;Username=username??"";Password=password??"";
            if((Username.Length==0)!=(Password.Length==0)||Encoding.UTF8.GetByteCount(Username)>255||Encoding.UTF8.GetByteCount(Password)>255)throw new ArgumentException("proxy_invalid");
        }
    }
    internal sealed class Code64 {
        internal readonly List<byte> Bytes=new List<byte>();
        readonly Dictionary<string,int> labels=new Dictionary<string,int>();
        readonly List<KeyValuePair<int,string>> fixups=new List<KeyValuePair<int,string>>();
        internal void Emit(params byte[] b){Bytes.AddRange(b);}
        internal void Q(long v){Bytes.AddRange(BitConverter.GetBytes(v));}
        internal void I(int v){Bytes.AddRange(BitConverter.GetBytes(v));}
        internal void Label(string s){labels.Add(s,Bytes.Count);}
        internal void Jump(string s){Emit(0xe9);fixups.Add(new KeyValuePair<int,string>(Bytes.Count,s));I(0);}
        internal void J(byte cc,string s){Emit(0x0f,cc);fixups.Add(new KeyValuePair<int,string>(Bytes.Count,s));I(0);}
        internal void Rax(long v){Emit(0x48,0xb8);Q(v);}
        internal void Rcx(long v){Emit(0x48,0xb9);Q(v);}
        internal void Rdx(long v){Emit(0x48,0xba);Q(v);}
        internal void Call(long p){Rax(p);Emit(0xff,0xd0);}
        internal void Phase(byte phase){Emit(0xc7,0x43,0x04,phase,0,0,0);}
        internal byte[] Finish(){foreach(var f in fixups){byte[] b=BitConverter.GetBytes(labels[f.Value]-f.Key-4);for(int i=0;i<4;i++)Bytes[f.Key+i]=b[i];}return Bytes.ToArray();}
    }
    internal sealed class Remote : IDisposable {
        internal IntPtr Handle; internal readonly Process Process;
        internal Remote(Process p){Process=p;Handle=OpenProcess(0x438,false,p.Id);if(Handle==IntPtr.Zero)throw new Exception("native_access_denied");}
        internal long Allocate(int size){IntPtr p=VirtualAllocEx(Handle,IntPtr.Zero,(UIntPtr)size,0x3000,4);if(p==IntPtr.Zero)throw new Exception("native_allocate_failed");return p.ToInt64();}
        internal long Near(long target,int size) {
            long aligned=target&~65535L;
            for(long delta=65536;delta<0x70000000;delta+=65536) foreach(long sign in new long[]{1,-1}) {
                long address=aligned+sign*delta;if(address<65536)continue;
                IntPtr p=VirtualAllocEx(Handle,new IntPtr(address),(UIntPtr)size,0x3000,4);if(p!=IntPtr.Zero)return p.ToInt64();
            }
            throw new Exception("native_allocate_failed");
        }
        internal byte[] Read(long p,int n){byte[] b=new byte[n];UIntPtr read;if(!ReadProcessMemory(Handle,new IntPtr(p),b,(UIntPtr)n,out read)||read.ToUInt64()!=(ulong)n)throw new Exception("native_read_failed");return b;}
        internal void Write(long p,byte[] b){UIntPtr written;if(!WriteProcessMemory(Handle,new IntPtr(p),b,(UIntPtr)b.Length,out written)||written.ToUInt64()!=(ulong)b.Length)throw new Exception("native_write_failed");}
        internal long Ptr(long p){return BitConverter.ToInt64(Read(p,8),0);}
        internal void Protect(long p,int n,uint permission){uint old;if(!VirtualProtectEx(Handle,new IntPtr(p),(UIntPtr)n,permission,out old))throw new Exception("native_protect_failed");}
        internal long Utf8(string s){byte[] b=Encoding.UTF8.GetBytes(s+"\0");long p=Allocate(b.Length);Write(p,b);return p;}
        internal void Install(long p,byte[] bytes) {
            uint old;if(!VirtualProtectEx(Handle,new IntPtr(p),(UIntPtr)bytes.Length,0x40,out old))throw new Exception("native_protect_failed");
            try{Write(p,bytes);if(!FlushInstructionCache(Handle,new IntPtr(p),(UIntPtr)bytes.Length))throw new Exception("native_write_failed");}
            finally{uint ignored;VirtualProtectEx(Handle,new IntPtr(p),(UIntPtr)bytes.Length,old,out ignored);}
        }
        internal void Suspended(Action action) {
            List<IntPtr> stopped=new List<IntPtr>();
            try {
                foreach(ProcessThread thread in Process.Threads){IntPtr h=OpenThread(2,false,(uint)thread.Id);if(h==IntPtr.Zero)throw new Exception("native_access_denied");if(SuspendThread(h)==0xffffffff){CloseHandle(h);throw new Exception("native_access_denied");}stopped.Add(h);}
                action();
            } finally {foreach(IntPtr h in stopped){ResumeThread(h);CloseHandle(h);}}
        }
        public void Dispose(){if(Handle!=IntPtr.Zero){CloseHandle(Handle);Handle=IntPtr.Zero;}}
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr address,UIntPtr size,uint type,uint protection);
        [DllImport("kernel32.dll")]static extern bool ReadProcessMemory(IntPtr p,IntPtr address,byte[] bytes,UIntPtr n,out UIntPtr read);
        [DllImport("kernel32.dll")]static extern bool WriteProcessMemory(IntPtr p,IntPtr address,byte[] bytes,UIntPtr n,out UIntPtr written);
        [DllImport("kernel32.dll")]static extern bool VirtualProtectEx(IntPtr p,IntPtr address,UIntPtr n,uint permission,out uint old);
        [DllImport("kernel32.dll")]static extern bool FlushInstructionCache(IntPtr p,IntPtr address,UIntPtr n);
        [DllImport("kernel32.dll")]static extern IntPtr OpenThread(uint access,bool inherit,uint tid);
        [DllImport("kernel32.dll")]static extern uint SuspendThread(IntPtr t);
        [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr t);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr p);
    }
    internal static partial class NativeCode {
        // Release movement through both libGDX state and the game's canvas handler.
        // Called on the game thread only, before retrying a failed SonCap auto enable.
        internal static byte[] ReleaseMovement(long translate,long state,long release,long mapSlot=0,long inputRelease=0){
            Code64 a=new Code64();a.Emit(0x53,0x48,0x83,0xec,0x20);
            for(int key=19;key<=22;key++){
                a.Emit(0xb9);a.I(key);a.Emit(0x33,0xd2,0x45,0x33,0xc0);a.Call(state);
                a.Emit(0xb9);a.I(key);a.Emit(0x33,0xd2);a.Call(translate);a.Emit(0x3d,0,0,0,0x80);a.J(0x84,"skip"+key);
                a.Emit(0x8b,0xd8,0x8b,0xd0,0x33,0xc9,0x45,0x33,0xc0);a.Call(release);
                // The canvas may forward keyUp to an overlay. Also release the actual
                // world's gm input: its +0x34 flag cancels builtin auto in fc.Update.
                if(mapSlot!=0&&inputRelease!=0){
                    Slot(a,mapSlot,"skip"+key);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"skip"+key);a.Emit(0x48,0x8b,0x40,16,0x48,0x85,0xc0);a.J(0x84,"skip"+key);a.Emit(0x48,0x8b,0x48,0x38,0x48,0x85,0xc9);a.J(0x84,"skip"+key);a.Emit(0x8b,0xd3,0x45,0x33,0xc0);a.Call(inputRelease);
                }
                a.Label("skip"+key);
            }
            a.Emit(0x48,0x83,0xc4,0x20,0x5b,0xc3);return a.Finish();
        }
        internal static byte[] Settings(long data,long canvasSlot,long fcSlot,long getter,long toggle,long callback,long save=0,long rootSlot=0,long movementRelease=0) {
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x30);a.Emit(0x48,0xbb);a.Q(data);
            a.Emit(0xc7,0x43,4,0,0,0,0);
            a.Rax(canvasSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x30,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0xbe,0xb8,1,0,0,0x48,0x85,0xff);a.J(0x84,"done");a.Rax(fcSlot);a.Emit(0x48,0x8b,0,0x48,0x39,0x07);a.J(0x85,"done");
            // Read the current game singleton, rather than retaining an obsolete panel across map changes.
            if(rootSlot!=0){a.Rax(rootSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x40,8,0x48,0x89,0x43,8);}
            a.Emit(0x48,0x8b,0x73,8,0x48,0x85,0xf6);a.J(0x85,"root");a.Emit(0x33,0xc9);a.Call(getter);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0x43,8,0x48,0x89,0xc6);
            a.Label("root");a.Emit(0xc7,0x43,4,1,0,0,0,0x0f,0xb6,0x86,0x88,1,0,0,0x89,0x43,16);
            a.Emit(0x83,0x3b,0);a.J(0x84,"done");a.Emit(0x83,0x3b,3);a.J(0x84,"done");a.Emit(0x8b,0x03,0x89,0x43,52,0xc7,0x03,3,0,0,0,0xc7,0x43,48,0,0,0,0,0x83,0xf8,1);a.J(0x85,"option");
            if(movementRelease!=0){a.Emit(0x83,0x7b,32,1);a.J(0x85,"noMovementRelease");a.Emit(0x83,0x7b,60,1);a.J(0x85,"noMovementRelease");a.Call(movementRelease);a.Emit(0xc7,0x43,60,0,0,0,0);a.Label("noMovementRelease");}
            a.Emit(0x48,0x89,0xf1,0x8b,0x53,32,0x45,0x33,0xc0);a.Call(toggle);a.Jump("ok");
            a.Label("option");a.Emit(0x83,0xf8,2);a.J(0x85,"reject");a.Emit(0x48,0x8b,0x7b,40,0x48,0x85,0xff);a.J(0x84,"reject");a.Emit(0x83,0x7b,28,2);a.J(0x84,"model");
            // Tab checkboxes are reparented into mj.e0 (the settings panel).
            a.Emit(0x48,0x39,0x77,0x48);a.J(0x84,"owned");a.Emit(0x48,0x8b,0x86,0xe0,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x39,0x47,0x48);a.J(0x85,"reject");a.Label("owned");a.Emit(0x83,0x7b,28,0);a.J(0x85,"dropdown");a.Emit(0x8b,0x43,32,0x88,0x87,0x84,0,0,0,0x45,0x33,0xc0);a.Jump("event");
            a.Label("dropdown");a.Emit(0x4c,0x8b,0x87,0x88,0,0,0,0x4d,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x8b,0x43,32,0x41,0x89,0x40,20);a.Jump("event");
            a.Label("model");a.Emit(0x8b,0x43,24,0x3d,0xe8,3,0,0);a.J(0x84,"keyboard");a.Emit(0x3d,0xed,3,0,0);a.J(0x85,"reject");a.Emit(0x48,0x3b,0xbe,0x48,1,0,0);a.J(0x85,"reject");a.Jump("modelWrite");
            a.Label("keyboard");a.Emit(0x48,0x3b,0xbe,0x40,1,0,0);a.J(0x85,"reject");
            a.Label("modelWrite");a.Emit(0x8b,0x43,32,0x89,0x47,20,0x49,0x89,0xf8);
            a.Label("event");a.Emit(0x48,0x89,0xf1,0x8b,0x53,24,0x49,0x89,0xf9,0x48,0xc7,0x44,0x24,32,0,0,0,0);a.Call(callback);if(save!=0){a.Emit(0x48,0x89,0xf1,0x33,0xd2);a.Call(save);}
            a.Label("ok");a.Emit(0x83,0x7b,52,1);a.J(0x85,"ackSuccess");a.Emit(0x0f,0xb6,0x86,0x88,1,0,0,0x3b,0x43,32);a.J(0x85,"ackUnconfirmed");
            a.Label("ackSuccess");a.Emit(0xc7,0x43,48,1,0,0,0);a.Jump("ackSnapshot");a.Label("ackUnconfirmed");a.Emit(0xc7,0x43,48,0xfe,0xff,0xff,0xff);
            a.Label("ackSnapshot");a.Emit(0x0f,0xb6,0x86,0x88,1,0,0,0x89,0x43,16,0xc7,0x03,0,0,0,0);a.Jump("done");
            a.Label("reject");a.Emit(0xc7,0x43,48,0xff,0xff,0xff,0xff,0xc7,0x03,0,0,0,0);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x30,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }

        // Translate the right key through fv, then send it directly to the world gm input.
        // A canvas/overlay forwarding handler can acknowledge the key without moving the player.
        // data+116=held, +128=start millis, +136=result, +140=translated key.
        internal static byte[] PortalKey(long data,long mapSlot,long translate,long down,long up,long clock){
            Code64 a=new Code64();a.Emit(0x53,0x48,0x83,0xec,0x20,0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x7b,116,0);a.J(0x85,"held");
            a.Emit(0x83,0x3b,9);a.J(0x85,"idle");a.Emit(0x83,0x7b,4,1);a.J(0x85,"reject");a.Jump("checkMap");
            a.Label("held");a.Emit(0x83,0x7b,4,1);a.J(0x85,"release");a.Emit(0x83,0x3b,6);a.J(0x84,"release");
            a.Label("checkMap");a.Rax(mapSlot);a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,"invalid");a.Emit(0xa8,1);a.J(0x85,"invalid");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"invalid");a.Emit(0x48,0x8b,0x40,16,0x48,0x85,0xc0);a.J(0x84,"invalid");a.Emit(0x48,0x8b,0x50,0x38,0x48,0x85,0xd2);a.J(0x84,"invalid");a.Emit(0x48,0x89,0x93,0x90,0,0,0,0x0f,0xbf,0x80,0x34,1,0,0,0x3b,0x43,96);a.J(0x85,"invalid");
            a.Emit(0x83,0x7b,116,0);a.J(0x84,"press");a.Emit(0x33,0xc9);a.Call(clock);a.Emit(0x48,0x2b,0x83,0x80,0,0,0,0x48,0x3d,0x58,2,0,0);a.J(0x83,"release");a.Jump("handled");
            a.Label("press");a.Emit(0xb9,22,0,0,0,0x33,0xd2);a.Call(translate);a.Emit(0x3d,0,0,0,0x80);a.J(0x84,"reject");a.Emit(0x89,0x83,0x8c,0,0,0,0x8b,0xd0,0x48,0x8b,0x8b,0x90,0,0,0,0x45,0x33,0xc0);a.Call(down);a.Emit(0x33,0xc9);a.Call(clock);a.Emit(0x48,0x89,0x83,0x80,0,0,0,0xc7,0x43,116,1,0,0,0,0xc7,0x83,0x88,0,0,0,1,0,0,0,0xc7,0x03,0,0,0,0);a.Jump("handled");
            a.Label("invalid");a.Emit(0x83,0x7b,116,0);a.J(0x84,"reject");
            a.Label("release");a.Emit(0x8b,0x93,0x8c,0,0,0,0x48,0x8b,0x8b,0x90,0,0,0,0x45,0x33,0xc0);a.Call(up);a.Emit(0xc7,0x43,116,0,0,0,0,0xc7,0x83,0x88,0,0,0,2,0,0,0);a.Jump("handled");
            a.Label("reject");a.Emit(0xc7,0x03,0,0,0,0,0xc7,0x83,0x88,0,0,0,0xff,0xff,0xff,0xff);a.Jump("handled");
            a.Label("idle");a.Emit(0x33,0xc0);a.Jump("done");a.Label("handled");a.Emit(0xb8,1,0,0,0);a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x5b,0xc3);return a.Finish();
        }

        // Weekly quest snapshot and commands, exclusively on the game thread.
        internal static byte[] Quest(long data,long game,long portalKey=0){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x48,0x83,0xec,0x20);a.Emit(0x48,0xbb);a.Q(data);
            if(portalKey!=0){a.Call(portalKey);a.Emit(0x85,0xc0);a.J(0x85,"return");}
            a.Emit(0x83,0x3b,6);a.J(0x84,"enabled");a.Emit(0x83,0x7b,4,1);a.J(0x85,"return");a.Label("enabled");a.Emit(0x83,0x3b,99);a.J(0x84,"return");a.Emit(0xc7,0x43,8,0,0,0,0);
            Slot(a,game+0x16ee888,"return",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"return");a.Emit(0x48,0x8b,0x70,16,0x48,0x85,0xf6);a.J(0x84,"return");
            a.Emit(0x83,0x3b,6);a.J(0x84,"cancelRoute");a.Emit(0xff,0x43,64);a.Emit(0x33,0xc9);a.Call(game+0x3672c0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"publish");a.Emit(0x83,0x7b,116,1);a.J(0x84,"beastCount");a.Emit(0x8b,0x80,0x88,0,0,0);a.Jump("countReady");a.Label("beastCount");a.Emit(0x8b,0x80,0x8c,0,0,0);a.Label("countReady");a.Emit(0x89,0x43,12);
            a.Emit(0x83,0x7b,116,1);a.J(0x84,"beastTask");a.Emit(0x48,0x8b,0xbe,0x18,3,0,0);a.Jump("taskReady");a.Label("beastTask");a.Emit(0x48,0x8b,0xbe,0x20,3,0,0);a.Label("taskReady");a.Emit(0x48,0x89,0x7b,48,0x48,0x85,0xff,0x0f,0x95,0xc0,0x0f,0xb6,0xc0,0x89,0x43,16,0xc7,0x43,20,0,0,0,0);a.J(0x84,"routeSnapshot");
            a.Emit(0x8b,0x47,24,0x89,0x43,24,0x8b,0x47,52,0x89,0x43,28,0x3b,0x47,24,0x0f,0x9e,0xc0,0x0f,0xb6,0xc0,0x89,0x43,20);
            a.Label("routeSnapshot");a.Emit(0xc7,0x43,108,0xff,0xff,0xff,0xff,0xc7,0x43,112,0xff,0xff,0xff,0xff,0x48,0x85,0xff);a.J(0x84,"questTargetSnapshot");a.Emit(0x8b,0x47,36,0x89,0x43,108,0x8b,0x47,20,0x89,0x43,112);a.Label("questTargetSnapshot");a.Emit(0x0f,0xbf,0x86,0x34,1,0,0,0x89,0x43,80,0xc7,0x43,84,0xff,0xff,0xff,0xff,0x48,0x85,0xff);a.J(0x84,"mapSnapshot");// jc.gpg (+0x1c) is the map, copied from kj.gwq (+0x28).
            a.Emit(0x8b,0x47,40,0x89,0x43,84);a.Label("mapSnapshot");a.Emit(0x48,0x83,0xbe,0x10,3,0,0,0,0x0f,0x95,0xc0,0x0f,0xb6,0xc0,0x89,0x43,32,0xc7,0x43,8,1,0,0,0);
            a.Label("publish");a.Emit(0x8b,0x43,116,0x89,0x43,120);a.Emit(0xff,0x43,64,0x83,0x7b,8,1);a.J(0x85,"return");a.Emit(0x83,0x3b,0);a.J(0x84,"return");a.Emit(0x8b,0x03,0x89,0x43,40,0xc7,0x03,99,0,0,0,0xc7,0x43,36,0,0,0,0);
            a.Emit(0x83,0xf8,1);a.J(0x84,"meiNativeRoute");a.Emit(0x83,0xf8,2);a.J(0x84,"route");a.Emit(0x83,0xf8,3);a.J(0x84,"npc");a.Emit(0x83,0xf8,9);a.J(0x84,"closeRasaMenu");a.Emit(0x83,0xf8,8);a.J(0x84,"farmRoute");a.Jump("menu");
            // The game NPC navigator checks the current map first, then resolves distant NPCs.
            // Calling only its catalogue resolver on a map containing Mei could throw and leave request=99.
            a.Label("meiNativeRoute");a.Emit(0xb9,102,0,0,0,0x83,0x7b,116,1);a.J(0x85,"npcRouteId");a.Emit(0xb9,98,0,0,0);a.Label("npcRouteId");a.Emit(0x33,0xd2);a.Call(game+0x4d04a0);a.Emit(0x4c,0x8b,0xae,0x10,3,0,0,0x4d,0x85,0xed);a.J(0x84,"done");a.Emit(0x41,0x8b,0x45,28,0x89,0x43,88,0x85,0xc0);a.J(0x88,"done");a.Emit(0x4c,0x89,0x6b,72);a.Jump("ok");
            a.Label("route");a.Emit(0x48,0x85,0xff);a.J(0x84,"done");Slot(a,game+0x16ee4a8,"done",game+0x1dd900);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x49,0x89,0xc5,0x48,0x89,0xc1,0x33,0xd2);a.Call(game+0x2ff3c0);
            int[] src={32,28,36,40,44,48};int[] dst={16,20,24,28,32,36};for(int i=0;i<src.Length;i++)a.Emit(0x8b,0x47,(byte)src[i],0x41,0x89,0x45,(byte)dst[i]);a.Emit(0x8b,0x47,40,0x41,0x89,0x45,48);
            a.Emit(0x4c,0x89,0x6b,72,0x4c,0x89,0xae,0x10,3,0,0,0x48,0x8d,0x8e,0x10,3,0,0,0x4c,0x89,0xea);a.Call(game+0x12d9e0);a.Jump("ok");
            // Đường đi bãi quái dùng bộ dẫn đường sẵn có; không gửi lặp yêu cầu đổi map.
            a.Label("farmRoute");Slot(a,game+0x16ee4a8,"done",game+0x1dd900);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x49,0x89,0xc5,0x48,0x89,0xc1,0x33,0xd2);a.Call(game+0x2ff3c0);
            a.Emit(0x8b,0x43,96,0x41,0x89,0x45,28,0x41,0x89,0x45,48,0x8b,0x43,100,0x41,0x89,0x45,32,0x8b,0x43,104,0x41,0x89,0x45,36);
            a.Emit(0x4c,0x89,0x6b,72,0x4c,0x89,0xae,0x10,3,0,0,0x48,0x8d,0x8e,0x10,3,0,0,0x4c,0x89,0xea);a.Call(game+0x12d9e0);a.Jump("ok");
            a.Label("npc");a.Emit(0x48,0x8b,0xbe,0x58,1,0,0,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x45,0x33,0xe4);
            a.Label("npcLoop");a.Emit(0x41,0x81,0xfc,128,0,0,0);a.J(0x83,"done");VectorCount(a);a.Emit(0x41,0x39,0xc4);a.J(0x8d,"done");VectorElement(a);a.Emit(0x4d,0x85,0xed);a.J(0x84,"npcNext");Slot(a,game+0x16ee910,"done",game+0x1dd900);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"npcNext");a.Emit(0x83,0x7b,116,1);a.J(0x84,"beastNpcId");a.Emit(0x66,0x41,0x83,0x7d,80,102);a.J(0x85,"npcNext");a.Jump("npcIdReady");a.Label("beastNpcId");a.Emit(0x66,0x41,0x83,0x7d,80,98);a.J(0x85,"npcNext");a.Label("npcIdReady");a.Emit(0x41,0x80,0x7d,61,6);a.J(0x84,"npcNext");a.Emit(0x41,0x8b,0x45,56,0x89,0x43,56);
            a.Emit(0x4c,0x89,0xae,0xb0,2,0,0,0x48,0x8d,0x8e,0xb0,2,0,0,0x4c,0x89,0xea);a.Call(game+0x12d9e0);a.Emit(0x4c,0x89,0xe9,0x33,0xd2);a.Call(game+0x4e19f0);a.Jump("ok");
            a.Label("npcNext");a.Emit(0x41,0xff,0xc4);a.Jump("npcLoop");
            a.Label("menu");a.Emit(0x4c,0x8b,0xae,0x90,0,0,0,0x4d,0x85,0xed);a.J(0x84,"done");Slot(a,game+0x16efe08,"done",game+0x1dd900);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"done");a.Emit(0x41,0x8b,0x85,0xdc,0,0,0,0x3b,0x43,56);a.J(0x85,"done");a.Emit(0x49,0x8b,0x8d,0xa8,0,0,0,0x48,0x85,0xc9);a.J(0x84,"done");a.Emit(0x33,0xd2);
            a.Label("options");a.Emit(0x81,0xfa,128,0,0,0);a.J(0x83,"done");a.Emit(0x3b,0x51,24);a.J(0x83,"done");a.Emit(0x48,0x8b,0x44,0xd1,32,0x83,0x7b,40,5);a.J(0x84,"receive");a.Emit(0x83,0x7b,40,7);a.J(0x84,"complete");
            a.Emit(0x83,0x7b,116,1);a.J(0x84,"beastRoot");
            string[] roots={"Nhiệm vụ tuần hoàn",":-chatNhiệm vụ tuần hoàn",":-chatNhiệm vụ tuần hoàn,Nhận,Huỷ,Hoàn Thành"};for(int i=0;i<roots.Length;i++){MatchString(a,roots[i],"rootMiss"+i);a.Jump("select");a.Label("rootMiss"+i);}a.Jump("optionNext");
            a.Label("beastRoot");string[] beastRoots={"Nhiệm vụ thu phục mãnh thú",":-chatNhiệm vụ thu phục mãnh thú",":-chatNhiệm vụ thu phục mãnh thú,Nhận,Huỷ,Hoàn Thành"};for(int i=0;i<beastRoots.Length;i++){MatchString(a,beastRoots[i],"beastRootMiss"+i);a.Jump("select");a.Label("beastRootMiss"+i);}a.Jump("optionNext");
            a.Label("complete");string[] completions={"Hoàn Thành",":-chatHoàn Thành","Hoàn thành",":-chatHoàn thành"};for(int i=0;i<completions.Length;i++){MatchString(a,completions[i],"completeMiss"+i);a.Jump("select");a.Label("completeMiss"+i);}a.Jump("optionNext");
            a.Label("receive");MatchString(a,"Nhận","receive2");a.Jump("select");a.Label("receive2");MatchString(a,":-chatNhận","optionNext");
            a.Label("select");a.Emit(0x49,0x8b,0x85,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x89,0x50,96,0x41,0xc7,0x85,0xc0,0,0,0,1,0,0,0,0x4c,0x89,0xe9,0x33,0xd2);a.Call(game+0x2d8f00);a.Jump("ok");a.Label("optionNext");a.Emit(0xff,0xc2);a.Jump("options");
            // Close only the exact active Rasa menu sampled by the worker. hy.rh uses
            // the same virtual close slot (0x578); receive/turn-in phases retain it.
            a.Label("closeRasaMenu");a.Emit(0x83,0x7b,116,1);a.J(0x85,"done");a.Emit(0x4c,0x8b,0xae,0x90,0,0,0,0x4d,0x85,0xed);a.J(0x84,"done");a.Emit(0x4c,0x3b,0xab,0x80,0,0,0);a.J(0x85,"done");
            Slot(a,game+0x16efe08,"done",game+0x1dd900);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"done");a.Emit(0x41,0x8b,0x85,0xdc,0,0,0,0x3b,0x43,56);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0x8e,0x88,0,0,0,0x48,0x85,0xc9);a.J(0x84,"done");a.Emit(0x48,0x8b,0x01,0x4c,0x89,0xea,0x4c,0x8b,0x80,0x10,2,0,0,0xff,0x90,8,2,0,0,0x84,0xc0);a.J(0x84,"done");
            a.Emit(0x49,0x8b,0x45,0,0x4c,0x89,0xe9,0x48,0x8b,0x90,0x80,5,0,0,0xff,0x90,0x78,5,0,0);a.Jump("ok");
            a.Label("cancelRoute");a.Emit(0x48,0x8b,0x43,72,0x48,0x85,0xc0);a.J(0x84,"clearOwned");a.Emit(0x48,0x39,0x86,0x10,3,0,0);a.J(0x85,"clearOwned");a.Emit(0x48,0xc7,0x86,0x10,3,0,0,0,0,0,0,0x48,0x8d,0x8e,0x10,3,0,0,0x33,0xd2);a.Call(game+0x12d9e0);a.Label("clearOwned");a.Emit(0x48,0xc7,0x43,72,0,0,0,0);a.Jump("done");
            a.Label("ok");a.Emit(0xc7,0x43,36,1,0,0,0);a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }

        internal static byte[] TravelMenu(long data,long fcSlot,long menuSlot,long menuTick,long initialize){
            Code64 a=new Code64();a.Emit(0x53,0x55,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x7b,64,2);a.J(0x84,"active");a.Emit(0x83,0x7b,64,3);a.J(0x85,"done");
            a.Label("active");Slot(a,fcSlot,"done",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x70,0x10,0x48,0x85,0xf6);a.J(0x84,"done");Slot(a,fcSlot,"done",initialize);a.Emit(0x48,0x39,0x06);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0xae,0x90,0,0,0,0x48,0x85,0xed);a.J(0x84,"done");Slot(a,menuSlot,"done",initialize);a.Emit(0x48,0x39,0x45,0);a.J(0x85,"done");
            a.Emit(0x4c,0x8b,0xb5,0xa8,0,0,0,0x4d,0x85,0xf6);a.J(0x84,"done");a.Emit(0x48,0x83,0xbd,0xb8,0,0,0,0);a.J(0x84,"done");a.Emit(0x45,0x33,0xe4);
            a.Label("options");a.Emit(0x41,0x81,0xfc,0x80,0,0,0);a.J(0x83,"done");a.Emit(0x45,0x3b,0x66,0x18);a.J(0x83,"done");a.Emit(0x4b,0x8b,0x44,0xe6,0x20);a.Emit(0x83,0x7b,64,2);a.J(0x85,"sand");
            MatchMenuText(a,"Làng","village_2");a.Jump("village_selected");a.Label("village_2");MatchMenuText(a,":-chatLàng","option_next");a.Label("village_selected");a.Emit(0xc7,0x43,64,3,0,0,0);a.Jump("select");
            a.Label("sand");MatchMenuText(a,"Làng cát","sand_2");a.Jump("sand_selected");a.Label("sand_2");MatchMenuText(a,"Làng Cát","sand_3");a.Jump("sand_selected");a.Label("sand_3");MatchMenuText(a,":-chatLàng cát","sand_4");a.Jump("sand_selected");a.Label("sand_4");MatchMenuText(a,":-chatLàng Cát","option_next");a.Label("sand_selected");a.Emit(0xc7,0x43,64,4,0,0,0);
            a.Label("select");a.Emit(0x48,0x8b,0x85,0xb8,0,0,0,0x44,0x89,0x60,0x60,0xc7,0x85,0xc0,0,0,0,1,0,0,0,0x48,0x89,0xe9,0x33,0xd2);a.Call(menuTick);a.Jump("done");
            a.Label("option_next");a.Emit(0x41,0xff,0xc4);a.Jump("options");
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5e,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5d,0x5b,0xc3);return a.Finish();
        }

        internal static byte[] AutoItem(long data,long game){return UseItem(data,game+0x16ed658,game+0x16ee888,game+0x3672c0,game+0x16ee578,game+0x4fb6d0);}
        internal static byte[] UseItem(long data,long canvasSlot,long fcSlot,long playerGetter,long itemSlot,long use){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x55,0x48,0x83,0xec,0x20);a.Emit(0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,1);a.J(0x85,"return");a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,8,1,0,0,0,0xc7,0x43,28,0,0,0,0);a.Emit(0x83,0x7b,12,1);a.J(0x85,"done");
            a.Rax(canvasSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x80,0xb8,1,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Rcx(fcSlot);a.Emit(0x48,0x8b,0x09,0x48,0x39,0x08);a.J(0x85,"done");
            a.Emit(0x33,0xc9);a.Call(playerGetter);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0xb0,0x28,1,0,0,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Emit(0x48,0x83,0x7e,24,0);a.J(0x8c,"invalid");a.Emit(0x48,0x81,0x7e,24,0,2,0,0);a.J(0x8f,"invalid");a.Emit(0x33,0xff);
            a.Label("next");a.Emit(0x3b,0x7e,24);a.J(0x8d,"missing");a.Emit(0x4c,0x8b,0x64,0xfe,32,0x4d,0x85,0xe4);a.J(0x84,"skip");a.Rax(itemSlot);a.Emit(0x48,0x8b,0,0x49,0x39,0x04,0x24);a.J(0x85,"skip");
            a.Emit(0x8b,0x43,4,0x41,0x39,0x44,0x24,20);a.J(0x85,"skip");a.Emit(0x41,0x83,0x7c,0x24,24,0);a.J(0x8e,"skip");
            // Use a freshly resolved bag slot, not the item template ID.
            a.Emit(0x41,0x8b,0x44,0x24,32,0x85,0xc0);a.J(0x88,"invalid");a.Emit(0x3b,0x46,24);a.J(0x83,"invalid");a.Emit(0x4c,0x39,0x64,0xc6,32);a.J(0x85,"invalid");a.Emit(0x89,0x43,16,0x83,0x7b,12,1);a.J(0x85,"done");
            // Đúng luồng hộp chọn số lượng của game (RVA 0x3e6420): gọi dùng N lần trong một lượt.
            // +24: số lượng yêu cầu (0 = dùng 1), +28: số lệnh thực sự gửi trong lượt này.
            // Giới hạn 1000/lượt, không vượt số rương trong một ô túi; mỗi vòng kiểm tra lại ô/gate.
            a.Emit(0x8b,0x6b,24,0x85,0xed);a.J(0x8f,"batchPositive");a.Emit(0xbd,1,0,0,0);
            a.Label("batchPositive");a.Emit(0xb8);a.I(1000);a.Emit(0x3b,0xe8,0x0f,0x4f,0xe8,0x41,0x8b,0x44,0x24,24,0x3b,0xe8,0x0f,0x4f,0xe8);
            a.Label("batchUse");a.Emit(0x83,0x7b,12,1);a.J(0x85,"done");
            a.Emit(0x41,0x83,0x7c,0x24,24,0);a.J(0x8e,"done");a.Emit(0x8b,0x43,16,0x3b,0x46,24);a.J(0x83,"invalid");a.Emit(0x4c,0x39,0x64,0xc6,32);a.J(0x85,"invalid");a.Emit(0x8b,0x43,4,0x41,0x39,0x44,0x24,20);a.J(0x85,"invalid");
            a.Emit(0x4c,0x89,0xe1,0x33,0xd2);a.Call(use);a.Emit(0xff,0x43,20,0xff,0x43,28,0xc7,0x43,8,2,0,0,0,0xff,0xcd);a.J(0x85,"batchUse");a.Jump("done");
            a.Label("skip");a.Emit(0xff,0xc7);a.Jump("next");a.Label("missing");a.Emit(0xc7,0x43,8,3,0,0,0);a.Jump("done");a.Label("invalid");a.Emit(0xc7,0x43,8,4,0,0,0);
            a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }

        // Run on Main.Update's existing IL2CPP thread; no keyboard, clipboard or window focus.
        // Data: state[0], UTF8 user pointer[8], UTF8 password pointer[16].
        internal static byte[] GiftCode(long data,long game){
            // Same packet and virtual calls as ff.rk's Gift Code confirmation.
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x20);a.Emit(0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,1);a.J(0x85,"done");
            Slot(a,game+0x16ed658,"done",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x30,0x48,0x85,0xf6);a.J(0x84,"done");
            Slot(a,game+0x16ee888,"done",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x78,16,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x48,0x39,0xbe,0xb8,1,0,0);a.J(0x85,"done");
            a.Emit(0xc7,0x03,0,0,0,0,0x48,0x8d,0x8b,0,4,0,0);a.Call(game+0x161150);a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x48,0x89,0xc6);
            a.Emit(0xb9,0x92,0xff,0xff,0xff,0x33,0xd2);a.Call(game+0x2e28a0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x48,0x89,0xc7,0x4c,0x8b,0x08,0x49,0x8b,0x81,0x78,2,0,0,0x4d,0x8b,0x81,0x80,2,0,0,0x48,0x89,0xf2,0x48,0x89,0xf9,0xff,0xd0);
            a.Emit(0x48,0x8b,0x07,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x8b,0x80,0x98,2,0,0,0x48,0x89,0xf9,0xff,0xd0,0xff,0x43,8,0xc7,0x43,4,2,0,0,0);a.Jump("done");
            a.Label("failed");a.Emit(0xc7,0x43,4,3,0,0,0);a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        // Cùng nội dung với nút Gởi của hd.rk: người nhận, chủ đề, nội dung,
        // bạc = 0, rồi ô túi đính kèm. Không dùng ID mẫu thay cho ô túi.
        // ig.cld (0x2e28a0) tạo envelope 0x85 và ghi thêm byte lệnh con;
        // hd.rk / nc.rk dùng ig.ctor trực tiếp với opcode 0x57 / 0x76.
        // Phải tạo đúng message ngoài, không dùng cld cho hai thao tác này.
        internal static void MailMessage(Code64 a,long game,byte opcode){
            Slot(a,game+0x16ee820,"failed",game+0x1dd900);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);
            a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x48,0x89,0xc7,0x48,0x89,0xc1,0xba,opcode,0,0,0,0x45,0x33,0xc0);a.Call(game+0x2e2aa0);
        }
        internal static byte[] Mail(long data,long game){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,1);a.J(0x85,"return");
            a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,8,1,0,0,0,0x83,0x7b,12,1);a.J(0x85,"done");
            Slot(a,game+0x16ed658,"done",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x30,0x48,0x85,0xf6);a.J(0x84,"done");Slot(a,game+0x16ee888,"done",game+0x1dd900);
            a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x78,16,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x48,0x39,0xbe,0xb8,1,0,0);a.J(0x85,"done");
            a.Emit(0x33,0xc9);a.Call(game+0x3672c0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0xf8,0x48,0x8b,0xb0,0x28,1,0,0,0x48,0x85,0xf6);a.J(0x84,"changed");
            a.Emit(0x48,0x81,0x7e,24,0,2,0,0);a.J(0x87,"changed");a.Emit(0x8b,0x43,20,0x85,0xc0);a.J(0x88,"changed");a.Emit(0x3b,0x46,24);a.J(0x83,"changed");
            a.Emit(0x4c,0x8b,0x64,0xc6,32,0x4d,0x85,0xe4);a.J(0x84,"changed");Slot(a,game+0x16ee578,"changed",game+0x1dd900);a.Emit(0x49,0x39,0x04,0x24);a.J(0x85,"changed");
            a.Emit(0x8b,0x43,16,0x41,0x39,0x44,0x24,20);a.J(0x85,"changed");a.Emit(0x8b,0x43,24,0x85,0xc0);a.J(0x8e,"changed");a.Emit(0x41,0x39,0x44,0x24,24);a.J(0x85,"changed");
            a.Emit(0x8b,0x43,20,0x41,0x39,0x44,0x24,32);a.J(0x85,"changed");a.Emit(0x41,0x80,0x7c,0x24,0x61,0);a.J(0x85,"locked");a.Emit(0x83,0x7b,4,2);a.J(0x84,"split");
            a.Emit(0x83,0x7b,4,1);a.J(0x85,"changed");a.Emit(0x48,0x83,0xbf,0xb8,0,0,0,10);a.J(0x8c,"funds");
            a.Emit(0x48,0x8d,0x8b,0,4,0,0);a.Call(game+0x161150);a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x4c,0x8b,0xe8);
            a.Emit(0x48,0x8d,0x8b,0x80,5,0,0);a.Call(game+0x161150);a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x4c,0x8b,0xf0);
            a.Emit(0x48,0x8d,0x8b,0,7,0,0);a.Call(game+0x161150);a.Emit(0x48,0x85,0xc0);a.J(0x84,"failed");a.Emit(0x4c,0x8b,0xf8);
            MailMessage(a,game,0x57);
            // hd+120: Nhập tên người nhận; hd+118: Nhập chủ đề; hd+110: Nhập nội dung.
            // hd.rk ghi recipient / title / body, không phải body / title / recipient.
            foreach(byte reg in new byte[]{0xea,0xf2,0xfa}){a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80,0x80,2,0,0,0x48,0x89,0xf9,0x4c,0x89,reg,0xff,0x90,0x78,2,0,0);}
            a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80,0x60,2,0,0,0x48,0x89,0xf9,0x33,0xd2,0xff,0x90,0x58,2,0,0);
            a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf9,0x8b,0x53,20,0xff,0x90,0x48,2,0,0);a.Jump("send");
            // nc.rk: tách chồng = packet 0x76, short ô túi, short số lượng.
            a.Label("split");a.Emit(0x8b,0x43,32,0x85,0xc0);a.J(0x8e,"invalidSplit");a.Emit(0x3d,0xff,0x7f,0,0);a.J(0x8f,"invalidSplit");a.Emit(0x3b,0x43,24);a.J(0x8d,"invalidSplit");
            MailMessage(a,game,0x76);
            foreach(byte offset in new byte[]{20,32}){a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf9,0x8b,0x53,offset,0xff,0x90,0x48,2,0,0);}
            // Đọc chính buffer đã được game mã hóa, trước khi gọi Send.
            // ig+20 -> pn+10 -> ByteArrayOutputStream (buf+10, count+18).
            a.Label("send");a.Emit(0x49,0x89,0xfd,0x48,0x8b,0x47,32,0x48,0x85,0xc0);a.J(0x84,"wireFailed");
            a.Emit(0x48,0x8b,0x40,16,0x48,0x85,0xc0);a.J(0x84,"wireFailed");
            a.Emit(0x8b,0x48,24,0x85,0xc9);a.J(0x8e,"wireFailed");a.Emit(0x81,0xf9,0,2,0,0);a.J(0x87,"wireFailed");
            a.Emit(0x89,0x4b,36,0x41,0x0f,0xb6,0x55,16,0x89,0x53,40,0x48,0x8b,0x70,16,0x48,0x85,0xf6);a.J(0x84,"wireFailed");
            a.Emit(0x48,0x3b,0x4e,24);a.J(0x87,"wireFailed");a.Emit(0x48,0x83,0xc6,32,0x48,0x8d,0xbb,0,8,0,0,0xfc,0xf3,0xa4);
            a.Emit(0x8b,0x4b,36,0x3b,0x4b,44);a.J(0x85,"wireFailed");
            a.Emit(0x48,0x8d,0xb3,0,8,0,0,0x48,0x8d,0xbb,0,10,0,0,0xf3,0xa6);a.J(0x85,"wireFailed");
            a.Emit(0x4c,0x89,0xef);a.Emit(0xc7,0x43,8,2,0,0,0,0x48,0x8b,0x07,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x89,0xf9,0xff,0x90,0x98,2,0,0);a.Jump("done");
            a.Label("wireFailed");a.Emit(0xc7,0x43,8,8,0,0,0);a.Jump("done");
            a.Label("changed");a.Emit(0xc7,0x43,8,3,0,0,0);a.Jump("done");a.Label("funds");a.Emit(0xc7,0x43,8,4,0,0,0);a.Jump("done");a.Label("locked");a.Emit(0xc7,0x43,8,7,0,0,0);a.Jump("done");a.Label("invalidSplit");a.Emit(0xc7,0x43,8,5,0,0,0);a.Jump("done");a.Label("failed");a.Emit(0xc7,0x43,8,6,0,0,0);
            a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5f,0x41,0x5e,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        // bqk là phản hồi Gởi thư thành công từ máy chủ; ghi nhận sau khi game
        // đã cập nhật túi. Không coi lời gọi nút Gởi là đã gửi thành công.
        // Chạy trên luồng Update của game. Các lệnh giống hộp thư:
        // 0x55 tải danh sách, 0x5f nhận một thư, 0x60 đánh dấu đọc,
        // 0x58 xóa đúng một ID; tuyệt đối không xóa thư còn đính kèm.
        internal static byte[] Inbox(long data,long game){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,1);a.J(0x85,"return");
            a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,8,1,0,0,0,0x83,0x7b,12,1);a.J(0x85,"done");
            a.Emit(0x83,0x7b,4,1);a.J(0x84,"refresh");a.Emit(0x83,0x7b,4,5);a.J(0x84,"all");a.Emit(0x83,0x7b,4,6);a.J(0x84,"deleteRead");
            a.Emit(0x48,0x8b,0x73,32,0x48,0x85,0xf6);a.J(0x84,"done");
            Slot(a,game+0x16ef960,"done",game+0x1dd900);a.Emit(0x48,0x39,0x06);a.J(0x85,"done");
            a.Emit(0x8b,0x46,16,0x3b,0x43,16);a.J(0x85,"done");
            a.Emit(0x83,0x7b,4,2);a.J(0x84,"claim");
            a.Emit(0x83,0x7b,4,4);a.J(0x84,"read");
            a.Emit(0x83,0x7b,4,3);a.J(0x85,"done");
            a.Emit(0x48,0x83,0x7e,48,0);a.J(0x85,"done");
            foreach(byte off in new byte[]{56,60,64,68}){a.Emit(0x83,0x7e,off,0);a.J(0x85,"done");}
            a.Emit(0x48,0x83,0x7e,72,0);a.J(0x85,"done");a.Emit(0x80,0x7e,80,0);a.J(0x84,"done");
            MailMessage(a,game,0x58);a.Emit(0xba,1,0,0,0);a.Jump("count");
            a.Label("all");a.Emit(0xb9,0xce,0,0,0,0x33,0xd2);a.Call(game+0x2e28a0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0xc7);a.Jump("send");
            // hg.cib(false) chính là Xóa đã đọc: game tự lọc đã đọc và hết đính kèm.
            // Với false, hàm không cần đối tượng hộp thư để tạo danh sách xóa.
            a.Label("deleteRead");a.Emit(0x33,0xc9,0x33,0xd2,0x45,0x33,0xc0);a.Call(game+0x2c50f0);a.Emit(0xc7,0x43,8,2,0,0,0);a.Jump("done");
            a.Label("claim");a.Emit(0x48,0x8b,0x46,48,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x8b,0x48,20,0x3b,0x4b,20);a.J(0x85,"done");a.Emit(0x8b,0x48,24,0x3b,0x4b,28);a.J(0x85,"done");
            MailMessage(a,game,0x5f);a.Jump("id");
            a.Label("read");MailMessage(a,game,0x60);a.Jump("id");
            a.Label("refresh");MailMessage(a,game,0x55);a.Jump("send");
            a.Label("count");a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf9,0xff,0x90,0x48,2,0,0);
            a.Label("id");a.Emit(0x8b,0x53,16,0x48,0x8b,0x07,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf9,0xff,0x90,0x48,2,0,0);
            a.Label("send");a.Emit(0xc7,0x43,8,2,0,0,0,0x48,0x8b,0x07,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x89,0xf9,0xff,0x90,0x98,2,0,0);a.Jump("done");
            a.Label("failed");a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        internal static bool InboxPrefixSupported(byte[] prefix){
            // 15 byte gồm cả mov rsi,rdx; không cắt ngang lệnh ở đầu hàm.
            byte[] expected={0x40,0x53,0x56,0x57,0x41,0x56,0x41,0x57,0x48,0x83,0xec,0x30,0x48,0x8b,0xf2};
            if(prefix==null||prefix.Length!=expected.Length)return false;
            for(int i=0;i<expected.Length;i++)if(prefix[i]!=expected[i])return false;return true;
        }
        internal static byte[] InboxListAck(long data,long original){
            Code64 a=new Code64();a.Emit(0x53,0x48,0x83,0xec,0x20);a.Call(original);a.Emit(0x48,0xbb);a.Q(data);a.Emit(0xff,0x43,24,0x48,0x83,0xc4,0x20,0x5b,0xc3);return a.Finish();
        }
        internal static byte[] MailAck(long data,long original){
            Code64 a=new Code64();a.Emit(0x53,0x48,0x83,0xec,0x20);a.Call(original);a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x7b,4,1);a.J(0x85,"done");a.Emit(0x83,0x7b,8,2);a.J(0x85,"done");a.Emit(0xff,0x43,28);a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x5b,0xc3);return a.Finish();
        }

        internal static byte[] Login(long data,long classSlot,long original,long stringNew,long setter,long login,long characterCode=0,long trainingCode=0,long settingsCode=0,long teamCode=0,long followCode=0,long zoneCode=0,long itemCode=0,long questCode=0,long travelItemCode=0,long travelMenuCode=0,long giftCode=0,long mailCode=0,long inboxCode=0,long farmCode=0,long chestCode=0,long cloakCode=0,long marketCode=0) {
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x40);
            // Thử skill trước Update treo; giữ nguyên tham số của Update gốc.
            a.Emit(0x48,0x89,0x4c,0x24,0x20,0x48,0x89,0x54,0x24,0x28,0x4c,0x89,0x44,0x24,0x30,0x4c,0x89,0x4c,0x24,0x38);
            if(trainingCode!=0)a.Call(trainingCode);
            a.Emit(0x48,0x8b,0x4c,0x24,0x20,0x48,0x8b,0x54,0x24,0x28,0x4c,0x8b,0x44,0x24,0x30,0x4c,0x8b,0x4c,0x24,0x38);a.Call(original);
            a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,0x01);a.J(0x85,"done");a.Phase(11);
            a.Rax(classSlot);a.Emit(0x48,0x8b,0x38,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x40,0xf6,0xc7,0x01);a.J(0x85,"done");
            a.Phase(12);a.Emit(0x48,0x8b,0x87,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Phase(13);a.Emit(0x48,0x8b,0x30,0x48,0x85,0xf6);a.J(0x84,"done");a.Phase(14);a.Emit(0x48,0x39,0x3e);a.J(0x85,"done");a.Phase(15);
            a.Emit(0x48,0x83,0xbe,0xa0,0,0,0,0);a.J(0x84,"done");a.Emit(0x48,0x83,0xbe,0xa8,0,0,0,0);a.J(0x84,"done");
            // Mark consumed before calling game code; never submit twice on reentrant Update.
            a.Emit(0xc7,0x03,0x02,0,0,0);
            a.Emit(0x48,0x8b,0x4b,0x08);a.Call(stringNew);a.Emit(0x48,0x89,0xc2,0x48,0x8b,0x8e,0xa0,0,0,0,0x45,0x33,0xc0);a.Call(setter);
            a.Emit(0x48,0x8b,0x4b,0x10);a.Call(stringNew);a.Emit(0x48,0x89,0xc2,0x48,0x8b,0x8e,0xa8,0,0,0,0x45,0x33,0xc0);a.Call(setter);
            a.Emit(0x48,0x89,0xf1,0xba,0x01,0,0,0,0x45,0x33,0xc0);a.Call(login);a.Emit(0xc7,0x03,0x03,0,0,0);
            a.Label("done");if(characterCode!=0)a.Call(characterCode);if(settingsCode!=0)a.Call(settingsCode);if(teamCode!=0)a.Call(teamCode);if(followCode!=0)a.Call(followCode);if(zoneCode!=0)a.Call(zoneCode);if(itemCode!=0)a.Call(itemCode);if(questCode!=0)a.Call(questCode);if(travelItemCode!=0)a.Call(travelItemCode);if(travelMenuCode!=0)a.Call(travelMenuCode);if(giftCode!=0)a.Call(giftCode);if(mailCode!=0)a.Call(mailCode);if(inboxCode!=0)a.Call(inboxCode);if(farmCode!=0)a.Call(farmCode);if(chestCode!=0)a.Call(chestCode);if(cloakCode!=0)a.Call(cloakCode);if(marketCode!=0)a.Call(marketCode);a.Emit(0x48,0x83,0xc4,0x40,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        // Shared data: request, generation, count, enabled, selected ID, attempts,
        // status; records at +128 (32 bytes: ID/template/MP/cast/name/level/type).
        internal static byte[] Train(long data,long canvasSlot,long fcSlot,long charSlot,long mobSlot,long templateGetter,long distance,long skillButton,long barrier=0,long attackButton=0,long directSkill=0){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);
            // +112/+120/+124: sampled Thi Quy, map and strict focus mode.
            // The worker arms this only after builtin hanging is confirmed off.
            a.Emit(0x83,0x3b,0x01);a.J(0x85,"return");a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,0x18,0,0,0,0);
            a.Rax(canvasSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x30,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0xbe,0xb8,1,0,0,0x48,0x85,0xff);a.J(0x84,"done");a.Rax(fcSlot);a.Emit(0x48,0x8b,0,0x48,0x39,0x07);a.J(0x85,"done");
            a.Emit(0xff,0x43,4,0xc7,0x43,8,0,0,0,0,0x48,0xc7,0x43,0x20,0,0,0,0,0xc7,0x43,0x18,1,0,0,0);
            a.Emit(0x4c,0x8b,0xbf,0xd0,2,0,0,0x4d,0x85,0xff);a.J(0x84,"published");a.Emit(0x45,0x33,0xf6);
            a.Label("skills");a.Emit(0x41,0x83,0xfe,0x40);a.J(0x8d,"published");
            a.Emit(0x49,0x8b,0x07,0x49,0x8b,0xcf,0x48,0x8b,0x90,0xf0,1,0,0,0xff,0x90,0xe8,1,0,0,0x44,0x3b,0xf0);a.J(0x8d,"published");
            a.Emit(0x49,0x8b,0x07,0x49,0x8b,0xcf,0x41,0x8b,0xd6,0x4c,0x8b,0x80,0x60,2,0,0,0xff,0x90,0x58,2,0,0,0x4c,0x8b,0xe0,0x48,0x85,0xc0);a.J(0x84,"nextSkill");
            a.Emit(0x49,0x8b,0xcc,0x33,0xd2);a.Call(templateGetter);a.Emit(0x48,0x85,0xc0);a.J(0x84,"nextSkill");
            a.Emit(0x48,0x8b,0xd0,0x44,0x8b,0x6b,8,0x49,0xc1,0xe5,5,0x4c,0x03,0xeb,0x49,0x81,0xc5,0x80,0,0,0);
            a.Emit(0x41,0x0f,0xbf,0x44,0x24,0x10,0x41,0x89,0x45,0,0x41,0x0f,0xbf,0x44,0x24,0x12,0x41,0x89,0x45,4,0x41,0x0f,0xbf,0x44,0x24,0x18,0x41,0x89,0x45,8,0x41,0x8b,0x44,0x24,0x1c,0x41,0x89,0x45,0x0c);
            a.Emit(0x48,0x8b,0x42,0x18,0x49,0x89,0x45,0x10,0x41,0x0f,0xbe,0x44,0x24,0x14,0x41,0x89,0x45,0x18,0x0f,0xbe,0x42,0x2c,0x41,0x89,0x45,0x1c,0xff,0x43,8);
            a.Emit(0x83,0xf8,1);a.J(0x87,"nextSkill");a.Emit(0x41,0x8b,0x45,0,0x3b,0x43,0x10);a.J(0x85,"nextSkill");a.Emit(0x41,0x8b,0x45,0x1c,0x89,0x43,0x4c,0x4c,0x89,0x63,0x20);
            a.Label("nextSkill");a.Emit(0x41,0xff,0xc6);a.Jump("skills");
            a.Label("published");a.Emit(0xff,0x43,4,0x83,0x7b,0x0c,1);a.J(0x85,"done");
            // Khi làm nhiệm vụ, đọc lại nhiệm vụ ngay trên luồng game trước mỗi lần đánh.
            // Không dùng ID cũ nếu đổi nhiệm vụ, sai map hoặc đã đủ số quái.
            a.Emit(0xc7,0x43,84,0xff,0xff,0xff,0xff,0x83,0x7b,80,0);a.J(0x84,"questGuardDone");
            a.Emit(0x83,0x7b,80,2);a.J(0x84,"beastCombatTask");a.Emit(0x48,0x8b,0x87,0x18,3,0,0);a.Jump("combatTaskReady");
            a.Label("beastCombatTask");a.Emit(0x48,0x8b,0x87,0x20,3,0,0);a.Label("combatTaskReady");a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x83,0x78,20,1);a.J(0x85,"done");a.Emit(0x8b,0x50,24,0x3b,0x50,52);a.J(0x8d,"done");
            a.Emit(0x0f,0xbf,0x97,0x34,1,0,0,0x3b,0x50,40);a.J(0x85,"done");
            a.Emit(0x8b,0x50,36,0x85,0xd2);a.J(0x88,"done");a.Emit(0x89,0x53,84);
            a.Label("questGuardDone");a.Emit(0x83,0x7b,124,2);a.J(0x84,"done");
            // Same filtering as quest combat: never fall back to another mob
            // while Thi Quy focus is active, including between worker snapshots.
            a.Emit(0x83,0x7b,80,0);a.J(0x85,"focusFilterDone");a.Emit(0x83,0x7b,124,1);a.J(0x85,"focusFilterDone");
            a.Emit(0x0f,0xbf,0x87,0x34,1,0,0,0x3b,0x43,120);a.J(0x85,"done");a.Emit(0xc7,0x43,84,0x05,0x01,0,0);a.Label("focusFilterDone");
            // Native casts are independent of visible overlays.
            a.Emit(0xc7,0x43,0x44,0xff,0xff,0xff,0xff,0xc7,0x43,0x48,0,0,0,0);
            a.Rax(charSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x4c,0x8b,0x68,8,0x4d,0x85,0xed);a.J(0x84,"done");a.Emit(0x41,0x83,0xbd,0xac,0,0,0,0);a.J(0x8e,"done");
            a.Emit(0xc7,0x43,0x18,2,0,0,0,0x48,0x83,0x7b,0x20,0);a.J(0x84,"done");
            a.Emit(0xc7,0x43,0x18,3,0,0,0,0x48,0xc7,0x43,0x38,0,0,0,0,0xc7,0x43,0x40,0xff,0xff,0xff,0x7f);a.Emit(0x83,0x7b,0x4c,1);a.J(0x84,"castGuard");
            // Danh sách +0x160 thuộc treo mặc định và rỗng khi treo tắt.
            // Nhiệm vụ dùng +0x150: toàn bộ quái trên map, lọc ID và HP bên dưới.
            a.Emit(0x83,0x7b,80,0);a.J(0x84,"ordinaryMobList");a.Emit(0x4c,0x8b,0xbf,0x50,1,0,0);a.Jump("mobListReady");
            a.Label("ordinaryMobList");a.Emit(0x83,0x7b,124,1);a.J(0x85,"priorityMobListDone");a.Emit(0x48,0x83,0x7b,112,0);a.J(0x84,"priorityMobListDone");a.Emit(0x4c,0x8b,0xbf,0x50,1,0,0);a.Jump("mobListReady");a.Label("priorityMobListDone");a.Emit(0x83,0x7b,88,1);a.J(0x85,"builtinMobList");a.Emit(0x4c,0x8b,0xbf,0x50,1,0,0);a.Jump("mobListReady");a.Label("builtinMobList");a.Emit(0x4c,0x8b,0xbf,0x60,1,0,0);a.Label("mobListReady");a.Emit(0x4d,0x85,0xff);a.J(0x84,"done");a.Emit(0x45,0x33,0xf6);
            a.Label("mobs");a.Emit(0x41,0x81,0xfe,0,2,0,0);a.J(0x8d,"target");a.Emit(0x49,0x8b,0x07,0x49,0x8b,0xcf,0x48,0x8b,0x90,0xf0,1,0,0,0xff,0x90,0xe8,1,0,0,0x44,0x3b,0xf0);a.J(0x8d,"target");
            a.Emit(0x49,0x8b,0x07,0x49,0x8b,0xcf,0x41,0x8b,0xd6,0x4c,0x8b,0x80,0x60,2,0,0,0xff,0x90,0x58,2,0,0,0x4c,0x8b,0xe0,0x48,0x85,0xc0);a.J(0x84,"nextMob");
            a.Rax(mobSlot);a.Emit(0x48,0x8b,0,0x49,0x39,0x04,0x24);a.J(0x85,"nextMob");a.Emit(0x41,0x80,0x7c,0x24,0x3d,4);a.J(0x83,"nextMob");// Thi Quy can have display HP +0x78 == 0 while real HP +0x70 is positive.
            a.Emit(0x66,0x41,0x81,0x7c,0x24,0x50,0x05,0x01);a.J(0x85,"legacyMobHp");a.Emit(0x41,0x83,0x7c,0x24,0x70,0);a.J(0x8e,"nextMob");a.Jump("mobHpReady");a.Label("legacyMobHp");a.Emit(0x41,0x83,0x7c,0x24,0x78,0);a.J(0x8e,"nextMob");a.Label("mobHpReady");
            // Chỉ chọn quái sống có đúng template nhiệm vụ; không tìm thấy thì chờ hồi sinh.
            a.Emit(0x83,0x7b,84,0);a.J(0x88,"mobFilterDone");a.Emit(0x41,0x0f,0xbf,0x44,0x24,0x50,0x3b,0x43,84);a.J(0x85,"nextMob");a.Label("mobFilterDone");
            a.Emit(0x83,0x7b,124,1);a.J(0x85,"priorityDistanceDone");a.Emit(0x83,0x7b,80,0);a.J(0x85,"priorityDistanceDone");a.Emit(0x4c,0x3b,0x63,112);a.J(0x85,"priorityDistanceDone");a.Emit(0x0f,0xbf,0x87,0x34,1,0,0,0x3b,0x43,120);a.J(0x85,"priorityDistanceDone");a.Emit(0x66,0x41,0x81,0x7c,0x24,0x50,0x05,0x01);a.J(0x85,"priorityDistanceDone");a.Emit(0x49,0x8b,0xcd,0x49,0x8b,0xd4,0x45,0x33,0xc0);a.Call(distance);a.Emit(0x89,0x43,0x40,0x4c,0x89,0x63,0x38);a.Jump("target");a.Label("priorityDistanceDone");
            a.Emit(0x49,0x8b,0xcd,0x49,0x8b,0xd4,0x45,0x33,0xc0);a.Call(distance);a.Emit(0x3b,0x43,0x40);a.J(0x8d,"nextMob");a.Emit(0x89,0x43,0x40,0x4c,0x89,0x63,0x38);
            a.Label("nextMob");a.Emit(0x41,0xff,0xc6);a.Jump("mobs");
            a.Label("target");a.Emit(0x48,0x8b,0x43,0x38,0x48,0x85,0xc0);a.J(0x84,"done");
            // Publish current target ID/HP, independently of the visible HUD target.
            a.Emit(0x8b,0x50,0x38,0x89,0x53,0x44,0x8b,0x50,0x78,0x66,0x81,0x78,0x50,0x05,0x01);a.J(0x85,"targetHpReady");a.Emit(0x8b,0x50,0x70);a.Label("targetHpReady");a.Emit(0x89,0x53,0x48);
            // Let an active attack finish its hit frames. Replacing it every request
            // prevented completion; this is not an external cooldown timer.
            a.Label("castGuard");
            if(directSkill!=0){
                // Bản Java đi thẳng qua nhân vật + skill + target. Để hàm game tự kiểm tra.
                a.Emit(0x83,0x7b,88,1);a.J(0x85,"ordinaryCastGuard");
                // Ngoài tầm skill thì để đường đánh/di chuyển gốc xử lý, không phát đòn từ xa.
                a.Emit(0x83,0x7b,76,1);a.J(0x84,"directInRange");a.Emit(0x48,0x8b,0x43,32,0x0f,0xbf,0x40,32,0x3b,0x43,64);a.J(0x8c,"ordinaryCastGuard");a.Label("directInRange");
                a.Emit(0x49,0x8b,0x85,0x68,1,0,0,0x48,0x89,0x43,96,0x49,0x8b,0x85,0x70,1,0,0,0x48,0x89,0x43,104);
                a.Emit(0xc7,0x43,24,7,0,0,0,0x49,0x8b,0xcd,0x48,0x8b,0x53,32,0x4c,0x8b,0x43,56,0x45,0x33,0xc9);a.Call(directSkill);
                // Chỉ đếm bắt đầu khi đối tượng đòn/chiêu mới xuất hiện, không đếm đòn treo cũ.
                a.Emit(0x49,0x8b,0x85,0x68,1,0,0,0x48,0x85,0xc0);a.J(0x84,"directSupportCheck");a.Emit(0x48,0x3b,0x43,96);a.J(0x85,"castStarted");
                a.Label("directSupportCheck");a.Emit(0x49,0x8b,0x85,0x70,1,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x3b,0x43,104);a.J(0x85,"castStarted");a.Jump("done");
                a.Label("ordinaryCastGuard");
            }
            a.Emit(0xc7,0x43,0x18,6,0,0,0,0x49,0x83,0xbd,0x68,1,0,0,0);a.J(0x85,"done");
            a.Emit(0x49,0x83,0xbd,0x70,1,0,0,0);a.J(0x85,"done");
            // bqw deliberately passes a null target for type 0/1. Use the UI
            // attack/chase path bmy for attacks; bqw remains appropriate for buffs.
            a.Emit(0xc7,0x43,0x18,7,0,0,0,0x83,0x7b,0x4c,1);a.J(0x84,"supportCast");
            a.Emit(0x48,0x8b,0x43,0x38,0x48,0x89,0x87,0xb0,2,0,0);
            if(barrier!=0){a.Emit(0x48,0x8d,0x8f,0xb0,2,0,0,0x48,0x89,0xc2);a.Call(barrier);}
            a.Emit(0x48,0x8b,0x43,0x20,0x48,0x89,0x87,0xe8,2,0,0);
            if(barrier!=0){a.Emit(0x48,0x8d,0x8f,0xe8,2,0,0,0x48,0x89,0xc2);a.Call(barrier);}
            a.Emit(0x48,0x89,0xf9,0x33,0xd2);a.Call(attackButton);a.Jump("checkCastStarted");
            a.Label("supportCast");a.Emit(0x48,0x89,0xf9,0x48,0x8b,0x53,0x20,0x45,0x33,0xc0);a.Call(skillButton);
            a.Label("checkCastStarted");
            a.Emit(0x49,0x83,0xbd,0x68,1,0,0,0);a.J(0x85,"castStarted");
            a.Emit(0x49,0x83,0xbd,0x70,1,0,0,0);a.J(0x84,"done");a.Label("castStarted");
            a.Emit(0xc7,0x43,0x18,4,0,0,0,0xff,0x43,0x14);
            a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5f,0x41,0x5e,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }

        // game.cg.bde(int): the same entry handler invoked by selecting a pedestal.
        // First character in the server's list is index 0, independent of screen position.
        internal static byte[] Character(long data,long canvasSlot,long selectorSlot,long enter,long npcCode=0) {
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,0x04);a.J(0x85,"done");a.Phase(21);
            a.Rax(canvasSlot);a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,0x01);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0xb0,0xb8,0x01,0,0,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Rax(selectorSlot);a.Emit(0x48,0x8b,0x38,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x40,0xf6,0xc7,0x01);a.J(0x85,"done");
            a.Emit(0x48,0x39,0x3e);a.J(0x85,"done");a.Phase(22);
            // Never enter while the character creation form is active.
            a.Emit(0x80,0xbe,0xa0,0,0,0,0);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0x8e,0x28,0x01,0,0,0x48,0x85,0xc9);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x01,0x48,0x8b,0x90,0xf0,0x01,0,0,0xff,0x90,0xe8,0x01,0,0);
            a.Emit(0x85,0xc0);a.J(0x8e,"empty");
            a.Emit(0xc7,0x03,0x05,0,0,0,0x48,0x89,0xf1,0x33,0xd2,0x45,0x33,0xc0);a.Call(enter);
            a.Emit(0xc7,0x03,0x06,0,0,0);a.Jump("done");
            a.Label("empty");a.Emit(0xc7,0x03,0x07,0,0,0);
            a.Label("done");if(npcCode!=0)a.Call(npcCode);a.Emit(0x48,0x83,0xc4,0x20,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        static void MatchMenuText(Code64 a,string text,string miss) {
            // hy renders only the first comma-separated segment. In the live
            // travel menu the Village row is ":-chatLàng,Làng Sương Mù".
            // Compare the full primary label, accepting only comma as a boundary.
            string complete="menu_label_"+a.Bytes.Count;
            a.Emit(0x48,0x85,0xc0);a.J(0x84,miss);a.Emit(0x81,0x78,0x10);a.I(text.Length);a.J(0x8c,miss);
            for(int i=0;i<text.Length;i++){a.Emit(0x66,0x81,0xb8);a.I(0x14+i*2);a.Emit((byte)text[i],(byte)(text[i]>>8));a.J(0x85,miss);}
            a.Emit(0x81,0x78,0x10);a.I(text.Length);a.J(0x84,complete);
            a.Emit(0x66,0x81,0xb8);a.I(0x14+text.Length*2);a.Emit(0x2c,0);a.J(0x85,miss);a.Label(complete);
        }
        static void MatchString(Code64 a,string text,string miss) {
            // RAX is an Il2CppString. Compare the full UTF-16 label, never a screen coordinate.
            a.Emit(0x48,0x85,0xc0);a.J(0x84,miss);a.Emit(0x81,0x78,0x10);a.I(text.Length);a.J(0x85,miss);
            for(int i=0;i<text.Length;i++){a.Emit(0x66,0x81,0xb8);a.I(0x14+i*2);a.Emit((byte)text[i],(byte)(text[i]>>8));a.J(0x85,miss);}
        }
        static void Slot(Code64 a,long address,string miss,long initialize=0) {
            // IL2CPP metadata slots are encoded tokens until the first use of that slot.
            // An object may already exist while a different reference to its class is unresolved.
            if(initialize!=0){
                string ready="slot_"+a.Bytes.Count;
                a.Rax(address);a.Emit(0x48,0x8b,0x00,0xa8,0x01);a.J(0x84,ready);
                a.Rcx(address);a.Call(initialize);a.Label(ready);
            }
            a.Rax(address);a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,miss);a.Emit(0xa8,0x01);a.J(0x85,miss);
        }
        static void VectorCount(Code64 a) {
            a.Emit(0x48,0x89,0xf9,0x48,0x8b,0x01,0x48,0x8b,0x90,0xf0,0x01,0,0,0xff,0x90,0xe8,0x01,0,0);
        }
        static void VectorElement(Code64 a) {
            a.Emit(0x48,0x89,0xf9,0x44,0x89,0xe2,0x48,0x8b,0x01,0x4c,0x8b,0x80,0x60,0x02,0,0,0xff,0x90,0x58,0x02,0,0,0x49,0x89,0xc5);
        }
        // States 8 -> 9 -> 10 -> 11: find NPC, request its menu, consume the exact key option.
        // All object reads and game calls run on Main.Update's thread, so screens cannot change mid-scan.
        // jr.rk's "Tạo nhóm" button (101) creates message 0x29. Use its
        // exact create/send path; fc.fjz (jo) is the server-populated current party.

        // One-shot team commands executed only by Main.Update, using the game's own paths.

        // Follow is ordinary client movement, not the unrelated "Tự Theo Sau" map-object mode.
        // Requests are bounded by the worker; all live object validation runs on Main.Update.
        internal static byte[] Follow(long data,long game,long initialize=0){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x48,0x83,0xec,0x28,0x48,0xbb);a.Q(data);
            a.Emit(0x8b,0x03,0x83,0xf8,1);a.J(0x84,"active");a.Emit(0x83,0xf8,2);a.J(0x85,"done");
            a.Label("active");a.Emit(0x89,0x43,56,0xc7,0x03,3,0,0,0,0xc7,0x43,36,0,0,0,0);
            Slot(a,game+0x16ee888,"reject",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x78,16,0x48,0x85,0xff);a.J(0x84,"reject");
            Slot(a,game+0x16ed658,"reject",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x3b,0xb8,0xb8,1,0,0);a.J(0x85,"reject");
            Slot(a,game+0x16ee330,"reject",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x70,8,0x48,0x85,0xf6);a.J(0x84,"reject");
            a.Emit(0x83,0x7b,56,2);a.J(0x84,"stop");
            a.Emit(0x48,0x8b,0x87,0xf0,2,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x3b,0x43,8);a.J(0x85,"reject");a.Emit(0x4c,0x8b,0x60,16,0x4d,0x85,0xe4);a.J(0x84,"reject");
            a.Emit(0x49,0x8b,0x04,0x24,0x49,0x8b,0xcc,0x48,0x8b,0x90,0xf0,1,0,0,0xff,0x90,0xe8,1,0,0,0x83,0xf8,6);a.J(0x85,"reject");
            a.Emit(0x49,0x8b,0x04,0x24,0x49,0x8b,0xcc,0x33,0xd2,0x4c,0x8b,0x80,0x60,2,0,0,0xff,0x90,0x58,2,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x40,16,0x48,0x3b,0x43,24);a.J(0x85,"reject");
            // Confirm this character remains a member even if the party object was reused.
            a.Emit(0xc7,0x43,64,0,0,0,0);
            a.Label("members");a.Emit(0x49,0x8b,0x04,0x24,0x49,0x8b,0xcc,0x8b,0x53,64,0x4c,0x8b,0x80,0x60,2,0,0,0xff,0x90,0x58,2,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x48,16,0x48,0x8b,0x96,0xf0,0,0,0,0x45,0x33,0xc0);a.Call(game+0xea7bc0);a.Emit(0x84,0xc0);a.J(0x85,"memberFound");a.Emit(0xff,0x43,64,0x83,0x7b,64,6);a.J(0x8c,"members");a.Jump("reject");
            a.Label("memberFound");a.Emit(0x48,0x89,0xf9,0x8b,0x53,32,0x45,0x33,0xc0);a.Call(game+0x4e0b50);a.Emit(0x48,0x85,0xc0);a.J(0x84,"missing");a.Emit(0x48,0x39,0xf0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x88,0xf0,0,0,0,0x48,0x3b,0x4b,16);a.J(0x85,"reject");a.Emit(0x49,0x89,0xc4);
            a.Emit(0x83,0xbe,0xac,0,0,0,0);a.J(0x8e,"reject");a.Emit(0x41,0x83,0xbc,0x24,0xac,0,0,0,0);a.J(0x8e,"missing");
            a.Emit(0x48,0x89,0xf1,0x49,0x8b,0xd4,0x45,0x33,0xc0);a.Call(game+0x3e7390);a.Emit(0x89,0x43,40,0x83,0xf8,48);a.J(0x8e,"near");
            // Don't interrupt active attack animation; training pauses while catching up.
            a.Emit(0x48,0x83,0xbe,0x68,1,0,0,0);a.J(0x85,"busy");a.Emit(0x48,0x83,0xbe,0x70,1,0,0,0);a.J(0x85,"busy");
            a.Emit(0x48,0x89,0xf1,0x41,0x0f,0xbf,0x54,0x24,16,0x45,0x0f,0xbf,0x44,0x24,18,0x45,0x0f,0xb6,0x4c,0x24,0x3c,0x48,0xc7,0x44,0x24,32,0,0,0,0);a.Call(game+0x370c00);a.Emit(0xff,0x43,52,0xc7,0x43,60,1,0,0,0,0xc7,0x43,36,1,0,0,0);a.Jump("finish");
            a.Label("stop");a.Emit(0x83,0x7b,60,1);a.J(0x85,"finish");a.Emit(0x48,0x89,0xf1,0x0f,0xbf,0x56,16,0x44,0x0f,0xbf,0x46,18,0x44,0x0f,0xb6,0x4e,0x3c,0x48,0xc7,0x44,0x24,32,0,0,0,0);a.Call(game+0x370c00);a.Emit(0xc7,0x43,60,0,0,0,0);a.Jump("finish");
            a.Label("near");a.Emit(0xc7,0x43,36,2,0,0,0);a.Jump("finish");a.Label("busy");a.Emit(0xc7,0x43,36,3,0,0,0);a.Jump("finish");
            a.Label("missing");a.Emit(0xc7,0x43,36,4,0,0,0);a.Jump("finish");a.Label("reject");a.Emit(0xc7,0x43,36,5,0,0,0);
            a.Label("finish");a.Emit(0xc7,0x03,0,0,0,0);a.Label("done");a.Emit(0x48,0x83,0xc4,0x28,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        // Main-thread zone commands. hy.rh's zone picker requests fc.bor(49);
        // change is the exact z.re(2002) packet path: ig.cld(-40), writeShort(zone), send.
        internal static byte[] Zone(long data,long game,long initialize=0){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x50,0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,1);a.J(0x84,"active");a.Emit(0x83,0x3b,2);a.J(0x85,"done");
            a.Label("active");Slot(a,game+0x16ee888,"reject",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x78,16,0x48,0x85,0xff);a.J(0x84,"reject");
            Slot(a,game+0x16ed658,"reject",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x39,0xb8,0xb8,1,0,0);a.J(0x85,"reject");
            a.Emit(0x0f,0xbf,0x87,0x34,1,0,0,0x3b,0x43,8);a.J(0x85,"reject");a.Emit(0x0f,0xbf,0x87,0x3a,1,0,0,0x3b,0x43,12);a.J(0x85,"reject");
            a.Emit(0x8b,0x03,0x89,0x43,4,0xc7,0x03,3,0,0,0,0x83,0x7b,4,1);a.J(0x85,"change");
            // Open the same jk map/zone dialog as the map-name button. Its constructor
            // requests fresh populations (message FA); fc.bub replaces jk.e8 on reply.
            Slot(a,game+0x16f02b8,"reject",initialize);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x89,0xc6,0x48,0x89,0xc1,0x48,0x89,0xfa,0x45,0x33,0xc0);a.Call(game+0x304ac0);
            a.Emit(0x48,0x89,0x73,32,0x48,0x8b,0x86,0xe8,0,0,0,0x48,0x89,0x43,24,0x48,0x8b,0x07,0x48,0x89,0xf9,0x48,0x89,0xf2,0x4c,0x8b,0x80,0xe0,4,0,0,0xff,0x90,0xd8,4,0,0);a.Jump("sent");
            a.Label("change");a.Emit(0x8b,0x43,16,0x85,0xc0);a.J(0x88,"reject");a.Emit(0x3d,0xff,0,0,0);a.J(0x8f,"reject");
            // jk.rk action 5001: ig(F9), writeByte(zone), send. Arena uses a different protocol.
            Slot(a,game+0x16ee820,"reject",initialize);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x89,0xc6,0x48,0x89,0xc1,0xba,0xf9,0,0,0,0x45,0x33,0xc0);a.Call(game+0x2e2aa0);
            a.Emit(0x48,0x8b,0x06,0x48,0x89,0xf1,0x8b,0x53,16,0x4c,0x8b,0x80,0x30,2,0,0,0xff,0x90,0x28,2,0,0);
            a.Emit(0x48,0x8b,0x06,0x48,0x89,0xf1,0x48,0x8b,0x90,0xa0,2,0,0,0xff,0x90,0x98,2,0,0);
            // Close only a jk dialog still active; never dismiss another popup.
            a.Emit(0x48,0x8b,0xb7,0x90,0,0,0,0x48,0x85,0xf6);a.J(0x84,"sent");Slot(a,game+0x16f02b8,"sent",initialize);a.Emit(0x48,0x39,0x06);a.J(0x85,"sent");a.Emit(0x48,0x8b,0x06,0x48,0x89,0xf1,0x48,0x8b,0x90,0x80,5,0,0,0xff,0x90,0x78,5,0,0);
            a.Label("sent");a.Emit(0xc7,0x03,4,0,0,0);a.Jump("done");a.Label("reject");a.Emit(0xc7,0x03,5,0,0,0);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x50,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        internal static byte[] Team(long data,long game,long initialize=0){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x50,0x48,0xbb);a.Q(data);
            a.Emit(0x8b,0x03,0x85,0xc0);a.J(0x84,"done");a.Emit(0x83,0xf8,8);a.J(0x84,"active");a.Emit(0x83,0xf8,5);a.J(0x83,"done");a.Label("active");
            Slot(a,game+0x16ee888,"done",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x70,16,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Emit(0x83,0x3b,8);a.J(0x84,"leave");a.Emit(0x83,0x3b,3);a.J(0x84,"accept");a.Emit(0x83,0x3b,4);a.J(0x84,"create");
            a.Emit(0x8b,0x03,0x89,0x43,4,0xc7,0x03,5,0,0,0,0x48,0x8d,0x4b,0x40);a.Call(game+0x161150);a.Emit(0x48,0x89,0xc1,0x33,0xd2,0x83,0x7b,4,1);a.J(0x85,"invite");// o.rk (friend-name dialog), confirm action 1001 -> fc.bsp(name).
            // fc.bst(500660) requests character information, not a friend.
            a.Call(game+0x500370);a.Jump("sent");
            a.Label("invite");a.Call(game+0x5001a0);a.Jump("sent");
            // jr.re confirmation 104 sends message 0x2a to leave the current party.
            a.Label("leave");a.Emit(0x48,0x8b,0x86,0xf0,2,0,0,0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x3b,0x43,32);a.J(0x85,"reject");a.Jump("create");
            a.Label("create");Slot(a,game+0x16ee820,"done",initialize);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0xc7,0xba,0x29,0,0,0,0x83,0x3b,8);a.J(0x85,"packetType");a.Emit(0xba,0x2a,0,0,0);a.Label("packetType");a.Emit(0xc7,0x03,5,0,0,0,0x48,0x89,0xf9,0x45,0x33,0xc0);a.Call(game+0x2e2aa0);
            a.Emit(0x48,0x8b,0x07,0x48,0x89,0xf9,0x48,0x8b,0x90,0xa0,2,0,0,0xff,0x90,0x98,2,0,0);a.Jump("sent");
            a.Label("accept");a.Emit(0x48,0x8b,0x40,24,0x48,0x3b,0x43,24);a.J(0x85,"reject");
            a.Emit(0x48,0x8b,0xbe,0x90,0,0,0,0x48,0x85,0xff);a.J(0x84,"reject");a.Emit(0x48,0x3b,0x7b,8);a.J(0x85,"reject");
            Slot(a,game+0x16eeba8,"reject",initialize);a.Emit(0x48,0x39,0x07);a.J(0x85,"reject");a.Emit(0x48,0x39,0xb7,0xd0,0,0,0);a.J(0x85,"reject");
            a.Emit(0x48,0x8b,0x8e,0x88,0,0,0,0x48,0x85,0xc9);a.J(0x84,"reject");a.Emit(0x48,0x8b,0x01,0x48,0x89,0xfa,0x4c,0x8b,0x80,0x10,2,0,0,0xff,0x90,8,2,0,0,0x84,0xc0);a.J(0x84,"reject");
            a.Emit(0x48,0x8b,0x73,16,0x48,0x85,0xf6);a.J(0x84,"reject");a.Emit(0x48,0x39,0x7e,0x48);a.J(0x85,"reject");a.Emit(0x81,0xbe,0x80,0,0,0,0xb5,0x0b,0,0);a.J(0x85,"reject");a.Emit(0x48,0x8b,0x46,0x78);MatchString(a,"Đồng ý","reject");
            // Build the same ol action event as the button, then je.rk closes the dialog
            // and routes action 2997 to fc.rk (not fc.re, which is a different dispatcher).
            Slot(a,game+0x16ee878,"reject",initialize);a.Emit(0x48,0x89,0xc1);a.Call(game+0x16f760);a.Emit(0x48,0x85,0xc0);a.J(0x84,"reject");a.Emit(0x48,0x89,0xc6,0xc7,0x03,5,0,0,0);
            a.Emit(0x48,0x89,0xf1,0xba,0xb5,0x0b,0,0,0x45,0x33,0xc0,0x45,0x33,0xc9);
            for(int n=32;n<=64;n+=8)a.Emit(0x48,0xc7,0x44,0x24,(byte)n,0,0,0,0);
            a.Call(game+0x3cce40);a.Emit(0x48,0x89,0xf9,0x48,0x89,0xf2,0x45,0x33,0xc0,0x45,0x33,0xc9,0x48,0xc7,0x44,0x24,32,0,0,0,0);a.Call(game+0x300330);
            a.Label("sent");a.Emit(0xc7,0x03,6,0,0,0);a.Jump("done");a.Label("reject");a.Emit(0xc7,0x03,7,0,0,0);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x50,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        internal static byte[] Party(long data,long gameSlot,long partySlot,long messageSlot,long allocate,long ctor,long initialize){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x20,0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,18);a.J(0x84,"active");a.Emit(0x83,0x3b,19);a.J(0x85,"done");
            a.Label("active");Slot(a,gameSlot,"done",initialize);
            a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x78,0x10,0x48,0x85,0xff);a.J(0x84,"done");Slot(a,gameSlot,"done",initialize);a.Emit(0x48,0x39,0x07);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0xb7,0xf0,2,0,0,0x48,0x85,0xf6);a.J(0x84,"create");
            Slot(a,partySlot,"done",initialize);a.Emit(0x48,0x39,0x06);a.J(0x85,"done");
            a.Emit(0x48,0x8b,0x4e,0x10,0x48,0x85,0xc9);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x01,0x48,0x8b,0x90,0xf0,1,0,0,0xff,0x90,0xe8,1,0,0);
            a.Emit(0x89,0x83);a.I(3288);a.Emit(0x85,0xc0);a.J(0x8f,"ready");
            a.Label("create");a.Emit(0x83,0x3b,18);a.J(0x85,"done");Slot(a,messageSlot,"done",initialize);
            a.Emit(0x48,0x89,0xc1);a.Call(allocate);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0xc6);
            // Consume before calling game code; repeated Update frames only poll.
            a.Emit(0xc7,0x03,19,0,0,0,0xff,0x83);a.I(3284);
            a.Emit(0x48,0x89,0xf1,0xba,0x29,0,0,0,0x45,0x33,0xc0);a.Call(ctor);
            a.Emit(0x48,0x8b,0x06,0x48,0x89,0xf1,0x48,0x8b,0x90,0xa0,2,0,0,0xff,0x90,0x98,2,0,0);a.Jump("done");
            a.Label("ready");a.Emit(0xc7,0x03,20,0,0,0);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        internal static byte[] Npc(long data,long canvasSlot,long gameSlot,long npcSlot,long menuSlot,long interact,long menuTick,long barrier,long initialize,long npcName,long partyCode=0) {
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0xbb);a.Q(data);if(partyCode!=0){a.Emit(0x83,0x3b,18);a.J(0x84,"party");a.Emit(0x83,0x3b,19);a.J(0x84,"party");}a.Emit(0x83,0x3b,0x08);a.J(0x84,"active");a.Emit(0x83,0x3b,0x09);a.J(0x84,"active");a.Emit(0x83,0x3b,0x0c);a.J(0x85,"done");
            // Read the gameplay singleton used by game.fc's own NPC handlers.
            // Canvas.activeScreen can refer to another screen while the map is still loaded.
            a.Label("active");a.Phase(34);Slot(a,gameSlot,"done",initialize);
            a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
            a.Emit(0x48,0x8b,0x70,0x10,0x48,0x85,0xf6);a.J(0x84,"done");Slot(a,gameSlot,"done",initialize);
            a.Emit(0x48,0x39,0x06);a.J(0x85,"done");a.Emit(0x83,0x3b,0x08);a.J(0x85,"menu");
            a.Phase(32);a.Emit(0x48,0x8b,0xbe,0x58,0x01,0,0,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x45,0x33,0xe4);
            a.Emit(0xff,0x43,0x20); // begin a seqlock snapshot (odd generation)
            a.Label("npc_loop");a.Emit(0x41,0x81,0xfc,0x80,0,0,0);a.J(0x83,"scan_done");VectorCount(a);a.Emit(0x89,0x43,0x18,0x41,0x39,0xc4);a.J(0x8d,"scan_done");VectorElement(a);
            NpcRecord(a);a.Emit(0x33,0xc0,0x49,0x89,0x02,0x49,0x89,0x42,0x08);
            a.Emit(0x4d,0x85,0xed);a.J(0x84,"npc_next");a.Phase(35);Slot(a,npcSlot,"scan_done",initialize);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"npc_next");
            a.Emit(0x41,0x80,0x7d,0x3d,0x06);a.J(0x84,"npc_next");
            // Use the game's display-name getter, including dynamic NPC names.
            a.Phase(36);a.Emit(0x4c,0x89,0xe9,0x33,0xd2);a.Call(npcName);
            NpcRecord(a);a.Emit(0x49,0x89,0x42,0x08,0x41,0x8b,0x45,0x38,0x41,0x89,0x02,0x41,0x0f,0xbf,0x4d,0x50,0x41,0x89,0x4a,0x04);
            // Dispatch by the map entity ID selected from a complete snapshot by the worker.
            a.Emit(0x3b,0x43,0x1c);a.J(0x85,"npc_next");a.Emit(0x3b,0x4b,0x24);a.J(0x85,"npc_next");a.Emit(0x83,0x7b,0x1c,0);a.J(0x8c,"npc_next");
            a.Emit(0xff,0x43,0x20); // publish before potentially reentrant game calls
            a.Emit(0xc7,0x03,0x0a,0,0,0); // consume before calling potentially reentrant game code
            a.Emit(0x4c,0x89,0xae,0xb0,0x02,0,0,0x48,0x8d,0x8e,0xb0,0x02,0,0,0x4c,0x89,0xea);a.Call(barrier);
            a.Emit(0x4c,0x89,0xe9,0x33,0xd2);a.Call(interact);a.Emit(0xc7,0x03,0x09,0,0,0);a.Jump("done");
            a.Label("npc_next");a.Emit(0x41,0xff,0xc4);a.Jump("npc_loop");
            a.Label("scan_done");a.Emit(0xff,0x43,0x20);a.Jump("done");
            // hk.cin (virtual slot 57) returns the active overlay at +0x90.
            // +0x88 contains ordinary child components, not the dynamic NPC menu.
            a.Label("menu");a.Phase(33);a.Emit(0x4c,0x8b,0xae,0x90,0,0,0);
            a.Emit(0x4d,0x85,0xed);a.J(0x84,"menu_next");Slot(a,menuSlot,"done",initialize);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"menu_next");
            a.Emit(0x49,0x8b,0x8d,0xa8,0,0,0,0x48,0x85,0xc9);a.J(0x84,"menu_next");
            a.Emit(0x49,0x8b,0x85,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"menu_next");
            // Publish dynamic-menu entries before selection, including non-NPC menu tags.
            a.Emit(0xff,0x83);a.I(2176);a.Emit(0x8b,0x51,0x18,0xb8,0x80,0,0,0,0x3b,0xd0,0x0f,0x47,0xd0,0x89,0x93);a.I(2180);
            a.Emit(0x41,0x8b,0x85);a.I(0xdc);a.Emit(0x89,0x83);a.I(2184);
            a.Emit(0x49,0x8b,0x85);a.I(0xb8);a.Emit(0x8b,0x40,0x60,0x89,0x83);a.I(2188);
            a.Emit(0x4c,0x89,0xab);a.I(2192);a.Emit(0x45,0x33,0xc0);
            a.Label("copy_menu");a.Emit(0x44,0x3b,0xc2);a.J(0x83,"copy_done");
            // Copy bounded UTF-16 text on the game thread before the menu is consumed/collected.
            a.Emit(0x4a,0x8b,0x44,0xc1,0x20,0x45,0x69,0xc8);a.I(1048);a.Emit(0x49,0x01,0xd9,0x49,0x81,0xc1);a.I(4096);
            a.Emit(0x4e,0x89,0x8c,0xc3);a.I(2240);a.Emit(0x45,0x33,0xdb,0x48,0x85,0xc0);a.J(0x84,"copy_length");a.Emit(0x44,0x8b,0x58,0x10,0x41,0x81,0xfb);a.I(512);a.J(0x86,"copy_length");a.Emit(0x45,0x33,0xdb);
            a.Label("copy_length");a.Emit(0x45,0x89,0x59,0x10,0x45,0x85,0xdb);a.J(0x84,"copy_next");a.Emit(0x4c,0x8d,0x50,0x14,0x49,0x83,0xc1,0x14);
            a.Label("copy_chars");a.Emit(0x41,0x0f,0xb7,0x02,0x66,0x41,0x89,0x01,0x49,0x83,0xc2,0x02,0x49,0x83,0xc1,0x02,0x41,0xff,0xcb);a.J(0x85,"copy_chars");
            a.Label("copy_next");a.Emit(0x41,0xff,0xc0);a.Jump("copy_menu");
            a.Label("copy_done");a.Emit(0xff,0x83);a.I(2176);
            a.Emit(0x83,0x3b,0x09);a.J(0x85,"menu_next");
            // Live Raikage menu carries its entity ID; legacy server menus use -2.
            a.Emit(0x41,0x8b,0x85);a.I(0xdc);a.Emit(0x83,0xf8,0xfe);a.J(0x85,"entity_menu");a.Emit(0x83,0xbb);a.I(3280);a.Emit(2);a.J(0x84,"menu_next");a.Jump("key_menu");a.Label("entity_menu");a.Emit(0x3b,0x43,0x1c);a.J(0x85,"menu_next");a.Label("key_menu");
            a.Emit(0x33,0xd2);a.Label("option_loop");a.Emit(0x81,0xfa,0x80,0,0,0);a.J(0x83,"menu_next");a.Emit(0x3b,0x51,0x18);a.J(0x83,"menu_next");a.Emit(0x48,0x8b,0x44,0xd1,0x20);
            a.Emit(0x83,0xbb);a.I(3280);a.Emit(3);a.J(0x84,"soncap_label");
            a.Emit(0x83,0xbb);a.I(3280);a.Emit(2);a.J(0x84,"izanami_label");
            a.Emit(0x83,0xbb);a.I(3280);a.Emit(1);a.J(0x84,"dungeon_label");
            MatchString(a,"Nhận chìa khóa","key_label_2");a.Jump("key_selected");a.Label("key_label_2");
            MatchString(a,"Nhận chìa khoá","key_label_3");a.Jump("key_selected");a.Label("key_label_3");
            MatchString(a,":-chatNhận chìa khóa","key_label_4");a.Jump("key_selected");a.Label("key_label_4");
            MatchString(a,":-chatNhận chìa khoá","option_next");a.Jump("key_selected");
            a.Label("soncap_label");MatchString(a,"Sơn Cáp Myoboku","soncap_label_2");a.Jump("key_selected");a.Label("soncap_label_2");MatchString(a,":-chatSơn Cáp Myoboku","option_next");a.Jump("key_selected");
            a.Label("izanami_label");MatchString(a,"Cấm thuật Izanami","izanami_label_2");a.Jump("key_selected");
            a.Label("izanami_label_2");MatchString(a,":-chatCấm thuật Izanami","option_next");a.Jump("key_selected");
            a.Label("dungeon_label");MatchString(a,"Địa cung (cao cấp)","dungeon_label_2");a.Jump("key_selected");
            a.Label("dungeon_label_2");MatchString(a,":-chatĐịa cung (cao cấp)","option_next");a.Label("key_selected");
            a.Emit(0x83,0xbb);a.I(3280);a.Emit(1);a.J(0x85,"key_authorized");
            a.Emit(0x83,0xbb);a.I(3292);a.Emit(1);a.J(0x84,"key_authorized");
            a.Emit(0xc7,0x03,21,0,0,0);a.Jump("done");
            a.Label("key_authorized");a.Emit(0xc7,0x83);a.I(3292);a.I(0);
            a.Emit(0x89,0x93);a.I(2188);a.Emit(0xc7,0x03,0x0a,0,0,0,0x49,0x8b,0x85,0xb8,0,0,0,0x89,0x50,0x60,0x41,0xc7,0x85,0xc0,0,0,0,0x01,0,0,0);
            a.Emit(0x4c,0x89,0xe9,0x33,0xd2);a.Call(menuTick);a.Emit(0xc7,0x03,0x0b,0,0,0);a.Jump("done");
            a.Label("option_next");a.Emit(0xff,0xc2);a.Jump("option_loop");
            a.Label("menu_next");a.Jump("done");
            if(partyCode!=0){a.Label("party");a.Call(partyCode);a.Jump("done");}
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
        static void NpcRecord(Code64 a){a.Emit(0x4d,0x89,0xe2,0x49,0xc1,0xe2,0x04,0x49,0x01,0xda,0x49,0x83,0xc2,0x40);}
        // Main-thread gate: map ready -> worker waits 5 seconds -> dismiss exact reminder -> NPC.
        internal static byte[] PrepareMap(long data,long canvasSlot,long gameSlot,long dialogSlot,long initialize,long next){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x48,0x83,0xec,0x30);a.Emit(0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,0x0d);a.J(0x84,"active");a.Emit(0x83,0x3b,0x0f);a.J(0x84,"active");a.Emit(0x83,0x3b,0x11);a.J(0x85,"done");
            a.Label("active");a.Phase(37);Slot(a,canvasSlot,"done",initialize);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x00,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0xb0,0xb8,0x01,0,0,0x48,0x85,0xf6);a.J(0x84,"done");
            Slot(a,gameSlot,"done",initialize);a.Emit(0x48,0x39,0x06);a.J(0x85,"done");
            a.Emit(0x83,0x3b,0x0d);a.J(0x85,"popup");a.Emit(0xc7,0x03,0x0e,0,0,0);a.Jump("done"); // Active gameplay screen is ready even on maps with no NPCs.
            a.Label("popup");a.Phase(38);a.Emit(0x48,0x8b,0xbe,0x90,0,0,0,0x48,0x85,0xff);a.J(0x84,"clear");
            a.Emit(0x83,0x3b,0x11);a.J(0x85,"membership");a.Emit(0x48,0x3b,0xbb);a.I(3272);a.J(0x85,"clear");a.Label("membership");
            // +0x90 can retain the dismissed dialog. Check membership of the live overlay stack.
            a.Emit(0x48,0x8b,0x8e,0x88,0,0,0,0x48,0x85,0xc9);a.J(0x84,"clear");a.Emit(0x48,0x8b,0x01,0x48,0x89,0xfa,0x4c,0x8b,0x80,0x10,0x02,0,0,0xff,0x90,0x08,0x02,0,0,0x84,0xc0);a.J(0x84,"clear");
            Slot(a,dialogSlot,"done",initialize);a.Emit(0x48,0x39,0x07);a.J(0x85,"clear");
            a.Emit(0x83,0x3b,0x11);a.J(0x84,"done"); // wait for actual closure; never submit twice
            a.Emit(0x48,0x8b,0x87,0xe8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"clear");a.Emit(0x83,0xb8,0x80,0,0,0,0xf9);a.J(0x85,"clear");
            a.Emit(0x48,0x8b,0x40,0x78);MatchString(a,"Xác nhận","clear");
            a.Emit(0x48,0x89,0xbb);a.I(3272);a.Emit(0xc7,0x03,0x11,0,0,0,0xff,0x83);a.I(3264); // consumed and counted before reentrant callback
            // Dispatch the same hk.cjc(-7, popup, button) action as the confirm button.
            a.Emit(0x48,0x8b,0x06,0x48,0x89,0xf1,0xba,0xf9,0xff,0xff,0xff,0x49,0x89,0xf8,0x4c,0x8b,0x8f,0xe8,0,0,0,0x4c,0x8b,0x90,0x90,0x04,0,0,0x4c,0x89,0x54,0x24,0x20,0xff,0x90,0x88,0x04,0,0);a.Jump("done");
            a.Label("clear");a.Emit(0xc7,0x03,0x10,0,0,0);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x30,0x5f,0x5e,0x5b);if(next!=0){a.Rax(next);a.Emit(0xff,0xe0);}else a.Emit(0xc3);return a.Finish();
        }
        internal static byte[] CaptureNpc(long data,long npcName,byte[] prefix,long continuation){
            Code64 a=new Code64();a.Emit(0x50,0x51,0x52,0x41,0x50,0x41,0x51,0x48,0x83,0xec,0x20);
            a.Emit(0x48,0x85,0xc9);a.J(0x84,"restore");a.Emit(0x33,0xd2);a.Call(npcName);
            a.Rdx(data);a.Emit(0x48,0x8b,0x4c,0x24,0x38,0xff,0x42,0x28,0x48,0x89,0x42,0x38,0x8b,0x41,0x38,0x89,0x42,0x2c,0x0f,0xbf,0x41,0x50,0x89,0x42,0x30,0xff,0x42,0x34,0xff,0x42,0x28);
            a.Label("restore");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x59,0x41,0x58,0x5a,0x59,0x58);a.Emit(prefix);a.Emit(0xff,0x25,0,0,0,0);a.Q(continuation);return a.Finish();
        }
        // Winsock connect IAT detour. Only stream IPv4 is supported; other traffic fails closed.
        // Original destination is paired to relay's accepted source port via a 64-slot shared table.
        internal static byte[] Connect(long slots,ushort port,long connect,long getName,long getOption,long getError,long setError) {
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x48,0x83,0xec,0x68,0x48,0x89,0xcb,0x48,0x89,0xd6);
            a.Emit(0x41,0x83,0xf8,0x10);a.J(0x82,"unsupported");a.Emit(0x66,0x83,0x3e,0x02);a.J(0x85,"unsupported");
            // Unity uses loopback TCP pairs during startup. These must stay local;
            // routing 127/8 through SOCKS would connect to the proxy's own machine.
            a.Emit(0x80,0x7e,0x04,0x7f);a.J(0x84,"local");
            a.Emit(0xc7,0x44,0x24,0x40,0x04,0,0,0,0x48,0x8d,0x44,0x24,0x40,0x48,0x89,0x44,0x24,0x20,0x4c,0x8d,0x4c,0x24,0x3c);
            a.Emit(0x41,0xb8,0x08,0x10,0,0,0xba,0xff,0xff,0,0,0x48,0x89,0xd9);a.Call(getOption);a.Emit(0x85,0xc0);a.J(0x85,"unsupported");a.Emit(0x83,0x7c,0x24,0x3c,0x01);a.J(0x85,"unsupported");
            a.Emit(0x49,0xbc);a.Q(slots);a.Emit(0xbf,0x40,0,0,0);a.Label("scan");a.Emit(0x33,0xc0,0xba,0x04,0,0,0,0xf0,0x41,0x0f,0xb1,0x14,0x24);a.J(0x84,"reserved");
            a.Emit(0x49,0x83,0xc4,0x20,0xff,0xcf);a.J(0x85,"scan");a.Jump("unsupported");
            a.Label("reserved");a.Emit(0xf3,0x0f,0x6f,0x06,0xf3,0x41,0x0f,0x7f,0x44,0x24,0x08);
            a.Emit(0xc7,0x44,0x24,0x30,0x10,0,0,0,0x48,0xb8);a.Q(0x0100007f00000002L|((long)port<<16));a.Emit(0x48,0x89,0x44,0x24,0x40,0x48,0xc7,0x44,0x24,0x48,0,0,0,0);
            a.Emit(0x48,0x89,0xd9,0x48,0x8d,0x54,0x24,0x40,0x41,0xb8,0x10,0,0,0);a.Call(connect);a.Emit(0x89,0x44,0x24,0x38);a.Call(getError);a.Emit(0x89,0x44,0x24,0x34);
            a.Emit(0x83,0x7c,0x24,0x38,0);a.J(0x84,"name");a.Emit(0x3d,0x33,0x27,0,0);a.J(0x85,"failed"); // WSAEWOULDBLOCK 10035
            a.Label("name");a.Emit(0x48,0x89,0xd9,0x48,0x8d,0x54,0x24,0x40,0x4c,0x8d,0x44,0x24,0x30);a.Call(getName);a.Emit(0x85,0xc0);a.J(0x85,"mapfailed");
            a.Emit(0x0f,0xb7,0x44,0x24,0x42,0x66,0x41,0x89,0x44,0x24,0x04,0x41,0xc7,0x04,0x24,0x01,0,0,0);a.Jump("restore");
            a.Label("mapfailed");a.Emit(0xc7,0x44,0x24,0x38,0xff,0xff,0xff,0xff,0xc7,0x44,0x24,0x34,0x37,0x27,0,0);
            a.Label("failed");a.Emit(0x41,0xc7,0x04,0x24,0,0,0,0);
            a.Label("restore");a.Emit(0x8b,0x4c,0x24,0x34);a.Call(setError);a.Emit(0x8b,0x44,0x24,0x38);a.Jump("done");
            a.Label("unsupported");a.Emit(0xb9,0x3f,0x27,0,0);a.Call(setError);a.Emit(0xb8,0xff,0xff,0xff,0xff);
            a.Jump("done");
            a.Label("local");a.Emit(0x48,0x89,0xd9,0x48,0x89,0xf2);a.Call(connect);
            a.Label("done");a.Emit(0x48,0x83,0xc4,0x68,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }
    }
    internal sealed class SocksRelay : IDisposable {
        readonly Remote remote; readonly long slots; readonly SocksConfig config; readonly TcpListener listener;
        readonly List<TcpClient> clients=new List<TcpClient>();volatile bool stopping;
        internal int Port {get{return ((IPEndPoint)listener.LocalEndpoint).Port;}}
        internal SocksRelay(Remote r,long map,SocksConfig c){remote=r;slots=map;config=c;listener=new TcpListener(IPAddress.Loopback,0);listener.Start(64);Thread t=new Thread(Accept);t.IsBackground=true;t.Start();}
        static byte[] Read(Stream s,int n){byte[] b=new byte[n];int p=0;while(p<n){int k=s.Read(b,p,n-p);if(k==0)throw new IOException("proxy_closed");p+=k;}return b;}
        internal static TcpClient Authenticate(SocksConfig c){
            TcpClient tcp=new TcpClient();try {
                var task=tcp.ConnectAsync(c.Host,c.Port);if(!task.Wait(7000))throw new IOException("proxy_timeout");tcp.ReceiveTimeout=7000;tcp.SendTimeout=7000;NetworkStream s=tcp.GetStream();
                byte method=(byte)(c.Username.Length>0?2:0);s.Write(new byte[]{5,1,method},0,3);byte[] choice=Read(s,2);if(choice[0]!=5||choice[1]!=method)throw new IOException("proxy_auth_failed");
                if(method==2){byte[] u=Encoding.UTF8.GetBytes(c.Username),p=Encoding.UTF8.GetBytes(c.Password);using(MemoryStream msg=new MemoryStream()){msg.WriteByte(1);msg.WriteByte((byte)u.Length);msg.Write(u,0,u.Length);msg.WriteByte((byte)p.Length);msg.Write(p,0,p.Length);byte[] packet=msg.ToArray();s.Write(packet,0,packet.Length);Array.Clear(packet,0,packet.Length);}byte[] answer=Read(s,2);if(answer[0]!=1||answer[1]!=0)throw new IOException("proxy_auth_failed");Array.Clear(u,0,u.Length);Array.Clear(p,0,p.Length);}
                return tcp;
            }catch{tcp.Close();throw;}
        }
        static void ConnectTarget(TcpClient tcp,byte[] destination){NetworkStream s=tcp.GetStream();byte[] request={5,1,0,1,destination[4],destination[5],destination[6],destination[7],destination[2],destination[3]};s.Write(request,0,request.Length);byte[] reply=Read(s,4);if(reply[0]!=5||reply[1]!=0)throw new IOException("proxy_connect_failed");if(reply[3]==1)Read(s,6);else if(reply[3]==4)Read(s,18);else if(reply[3]==3)Read(s,Read(s,1)[0]+2);else throw new IOException("proxy_reply_invalid");tcp.ReceiveTimeout=0;tcp.SendTimeout=0;}
        void Accept(){while(!stopping){try{TcpClient client=listener.AcceptTcpClient();lock(clients)clients.Add(client);ThreadPool.QueueUserWorkItem(delegate{Serve(client);});}catch{if(!stopping)Thread.Sleep(100);}}}
        void Serve(TcpClient game){TcpClient upstream=null;try {
            int port=((IPEndPoint)game.Client.RemoteEndPoint).Port;byte[] destination=null;Stopwatch wait=Stopwatch.StartNew();
            while(!stopping && wait.ElapsedMilliseconds<5000 && destination==null){byte[] map=remote.Read(slots,2048);for(int i=0;i<64;i++){int p=i*32;int localPort=(map[p+4]<<8)|map[p+5];if(BitConverter.ToInt32(map,p)==1&&localPort==port){destination=new byte[16];Array.Copy(map,p+8,destination,0,16);remote.Write(slots+p,new byte[4]);break;}}if(destination==null)Thread.Sleep(10);}
            if(destination==null)throw new IOException("proxy_mapping_failed");upstream=Authenticate(config);lock(clients)clients.Add(upstream);ConnectTarget(upstream,destination);
            NetworkStream gs=game.GetStream(),us=upstream.GetStream();ThreadPool.QueueUserWorkItem(delegate{try{gs.CopyTo(us);upstream.Client.Shutdown(SocketShutdown.Send);}catch{} });us.CopyTo(gs);
        }catch{}finally{game.Close();if(upstream!=null)upstream.Close();lock(clients){clients.Remove(game);if(upstream!=null)clients.Remove(upstream);}}}
        public void Dispose(){stopping=true;listener.Stop();lock(clients){foreach(TcpClient c in clients)c.Close();clients.Clear();}}
    }
    public sealed class SkillInfo { public int Id,TemplateId,Mp,CastMs,Level,Type; public string Name; public bool Attack {get{return Type==0;}} public bool Usable {get{return Type==0||Type==1;}} public override string ToString(){return Id+" — "+Name+" (Lv "+Level+")";} }

    public sealed class GameOption {
        public int Tab,Code,Kind,Value;public string Label;public string[] Choices=new string[0];
        internal long Handle;internal GameOption Copy(){return (GameOption)MemberwiseClone();}
    }
    public sealed class InventoryItem {
        public long ExpiryUtcMs;public int BagIndex,Slot,TemplateId,Quantity,Type;public string Name;public bool Equipment,Locked;
    }
    public sealed class InventorySnapshot {
        public readonly InventoryItem[] Items;public readonly int Capacity;public readonly bool Ready;public readonly string Error;public readonly DateTime ReadAt;
        internal InventorySnapshot(InventoryItem[] items,int capacity,bool ready,string error,DateTime readAt){Items=items;Capacity=capacity;Ready=ready;Error=error;ReadAt=readAt;}
    }
    internal sealed class InboxLetter {internal long Pointer;internal int Id,TemplateId=-1,Quantity;internal bool Read,HasAttachment;}
    public sealed partial class NativeJob {
        public volatile InventorySnapshot Inventory=new InventorySnapshot(new InventoryItem[0],0,false,"",DateTime.MinValue);
        public volatile bool FollowActive;public volatile string FollowStatus="";public volatile int FollowDistance=-1,FollowMoves;internal bool FollowStopPending;internal DateTime FollowPollAt=DateTime.MinValue;
        public volatile bool PersistentUp;internal bool UpRepeatDaily;internal DateTime UpDay;internal bool[] UpCompleted=new bool[5];internal string[] UpSonCapTeamIds=new string[0];internal bool UpIzanamiFull,UpSonCapFull;public volatile bool DailyManaged,DailyActive,DailyExecuting;internal bool DailyApplying;public volatile int DailyStage;public volatile string DailyStatus="";internal bool[] DailyPlan=new bool[4],DailyRunPlan=new bool[4];internal DateTime DailyLastDate;internal string[] DailyTeamIds=new string[0];internal FarmSpot DailyReturnSpot;internal bool DailyReturnQuiet;public volatile bool KeyReceiptVerified;public volatile bool CurseActive;public DateTime CurseReadAt=DateTime.MinValue;internal DateTime CursePollAt=DateTime.MinValue;public volatile int MapId=-1,ZoneId=-1;public DateTime ZoneReadAt=DateTime.MinValue;public volatile string ZoneStatus="";public volatile bool ZoneSyncActive;
        internal int PlannedZone=-1,ZonePlanMap=-1,ZoneAttempts;internal volatile bool ZonePrepared;internal DateTime ZoneActionAt=DateTime.MinValue,ZonePollAt=DateTime.MinValue;internal long ZoneOldPopup;internal bool ZoneListWaiting;
        public volatile VitalsSnapshot Vitals;internal DateTime VitalsPollAt,IdentityPollAt;public volatile bool PrioritizeThiQuy,ThiQuyCombatActive;internal int ThiQuySkillCursor,ThiQuyNativeMode;public volatile string ThiQuyPriorityStatus="Tắt";internal DateTime ThiQuyPriorityAt=DateTime.MinValue;internal bool ThiQuyPriorityArmed,ThiQuyFarmPaused,ThiQuyReadSuspended;internal DateTime ThiQuyMissingAt=DateTime.MinValue,ThiQuyScanLogAt=DateTime.MinValue;internal string ThiQuyScanLast="";public volatile string CharacterName="",TeamStatus="";public volatile string[] TeamMembers=new string[0],Friends=new string[0];
        internal string LeaderZoneJob="";internal int LeaderZoneMap=-1,LeaderZoneTarget=-1;internal bool LeaderZoneConfirmed;
        internal string[] TeamJobIds=new string[0];internal DateTime TeamReadAt=DateTime.MinValue,TeamActionAt=DateTime.MinValue;internal int TeamAttempts,TeamAttemptCommand;internal bool TeamFriendRequested;internal string TeamTarget="";internal bool TeamManaged,TeamConfirmed;internal long TeamAcceptedPopup;
        public volatile bool CloseAfterKey,KeyReceiptPending;internal int KeyReceiptBefore=-1,KeyReceiptLastCount=-1;internal DateTime KeyReceiptAt,KeyReceiptPollAt,KeyBaselineAt;internal string KeyReceiptReadError="";public volatile bool QuestQueueOnly,QuestFinished;public volatile bool QuestEnabled,QuestPauseAuto;public volatile int QuestCompleted,QuestRemaining=-1;public volatile string QuestStatus="Tắt";internal CloseBatchOperation QuestAutoCloseOperation;
        internal bool QuestFaultStopped;internal DateTime QuestSnapshotAt;internal int QuestObservedCount,QuestObservedTotal;
        internal DateTime ActivityFaultAt,ActivityVillageAt,ActivityCloseAt,ActivityHealthyAt;internal string ActivityWatchKind="",ActivityProgress="";
        internal bool ActivityRetryIssued;internal volatile bool ActivityRetryPending;
        public volatile bool ActivityTimeoutClosed;public volatile string ActivityWatchStatus="";
        internal CloseBatchOperation ActivityTimeoutCloseOperation;internal DateTime SonCapReadErrorAt;internal string SonCapReadError="";internal int QuestMobId=-1,QuestKind=-1;internal int QuestPhase,QuestBaseRemaining=-1,QuestCommand;internal bool QuestReturn,QuestHadTask,QuestWasDone,QuestInFlight;internal DateTime QuestAt,QuestRetryAt,QuestBusyAt;internal int QuestBusyRetries;
        internal long TravelData;internal bool TravelActive,TravelReady,TravelMapWaiting;internal int TravelPhase;internal DateTime TravelAt;internal string KeyNoticeBefore="",KeyNoticeLast="";
        internal HashSet<string> KeyNotificationSeen=new HashSet<string>();
        public volatile bool IzanamiTravelReady,IzanamiTravelStopped;
        public volatile bool ReconnectRequested;internal DateTime DisconnectSeenAt,DisconnectPollAt;
        public volatile bool CloakEnabled,CloakEquipAfter,CloakDone,CloakQueueOnly;internal int CloakEquipSlot;internal int[] CloakBeforeSlots=new int[0];internal long CloakWornBefore,CloakEquipExpiry;public volatile int CloakId,CloakQuantity,CloakExpectedPrice,CloakBought,CloakPhase;public volatile string CloakStatus="Chưa chạy";internal long CloakBefore;internal DateTime CloakAt,CloakPollAt;
        public volatile bool ChestEnabled,ChestUseOwned,ChestOpenAfter,ChestUseOnly;
        public volatile int ChestQuantity,ChestPhase,ChestBought,ChestUnitPrice,ChestSaleId,ChestBatch;
        public volatile string ChestStatus="Chưa chạy";
        public long ChestBefore,ChestRemaining,ChestUsed;
        internal DateTime ChestAt,ChestPollAt;
        public volatile bool AutoItemEnabled;public volatile int[] AutoItemIds=new int[0];internal int AutoItemCursor;public volatile int[] AutoItemOnceIds=new int[0];internal HashSet<int> AutoItemOnceAttempted=new HashSet<int>();public volatile int AutoItemId=-1,AutoItemIntervalMs=5000,AutoItemRequests;public volatile string AutoItemStatus="Tắt";internal DateTime AutoItemNext;
        public volatile bool GiftEnabled;public volatile int GiftRequests;public volatile string GiftStatus="Tắt",GiftResponse="";
        internal readonly object GiftGate=new object();internal string[] GiftCodes=new string[0];internal readonly HashSet<string> GiftAttempted=new HashSet<string>(StringComparer.Ordinal);
        internal bool GiftWaiting,GiftResultWaiting;internal string GiftCurrent="",GiftPopupBefore="";internal DateTime GiftAt,GiftNext;internal HashSet<string> GiftNoticeSeen=new HashSet<string>();
        public volatile bool InboxEnabled,InboxBusy,InboxSupported;public volatile string InboxStatus="Tắt nhận thư";public volatile int InboxReceived,InboxDeleted;public DateTime InboxObservedAt=DateTime.MinValue;public volatile int InboxPendingAttachments=-1;
        internal InboxLetter[] InboxLoaded,InboxBatch=new InboxLetter[0];internal int[] InboxDeleteIds=new int[0];internal Dictionary<int,long> InboxBatchTotals=new Dictionary<int,long>();internal int InboxPhase,InboxCommand,InboxListBefore;internal bool InboxNeedsRefresh;internal InboxLetter InboxTarget;internal DateTime InboxPollAt=DateTime.MinValue;internal DateTime InboxNext=DateTime.MinValue,InboxAt=DateTime.MinValue;
        internal readonly object MailGate=new object();public volatile bool MailEnabled,MailBusy,MailDone;public volatile int MailLetters;public long MailQuantity;public volatile string MailStatus="Tắt",MailRecipient="";internal bool MailWireLogged;internal string MailPopupBefore="";
        internal int[] MailIds=new int[0],MailQuantities=new int[0];internal int MailIntervalMs=5000,MailIndex,MailSlot,MailExpected,MailAmount,MailCommand,MailAckBefore,MailChanges;internal long MailRemaining=-1,MailBeforeTotal,MailBeforeSilver;
        internal DateTime MailAt,MailNext,MailPollAt;internal Dictionary<int,int> MailBeforeSlots=new Dictionary<int,int>();
        public volatile bool InventoryScanning;internal int InventoryRequested;
        internal ProcessStartInfo ManualLaunch;public volatile int StartAttempts;
        public readonly string Id=Guid.NewGuid().ToString();public readonly string Username;public readonly DateTime Created=DateTime.UtcNow;
        public volatile string State="queued",Error="",NpcDetails="",Stage="",CapturedNpcName="",DynamicMenuJson="",LogError="";
        public volatile int ProcessId,ReadyPhase,NpcCount,NpcId=-1,NpcTemplateId=-1,CapturedNpcId=-1,CapturedTemplateId=-1,MenuTag,MenuSelectedIndex=-1;
        public volatile string[] MenuOptions=new string[0];public volatile bool CancelRequested,CloseRequested,ReplayRequested;public volatile bool ReceiveKeyEnabled=true,ReceiveKeyRequested;
        public volatile bool MailQueueOnly;public volatile bool IzanamiQueueOnly;public volatile bool IzanamiEnabled,IzanamiRequested;public volatile int IzanamiStage;internal HashSet<string> IzanamiStageSeen=new HashSet<string>();public volatile bool IzanamiMapAutoActive;internal DateTime IzanamiMapSeenAt,IzanamiMapPollAt;public volatile bool IzanamiNoAttempts;public volatile string IzanamiLimitNotice="";internal bool IzanamiLimitArmed;internal string IzanamiPopupBefore="";internal HashSet<string> IzanamiNoticeSeen=new HashSet<string>();internal DateTime IzanamiNoticePollAt,IzanamiNoticeAt;public volatile int IzanamiEntries,IzanamiRoundsCompleted,IzanamiRoundsTarget=1;public bool IzanamiRunFinished{get{return IzanamiNoAttempts||IzanamiRoundComplete&&IzanamiRoundsCompleted>=IzanamiRoundsTarget;}}public volatile bool IzanamiRoundEntered,IzanamiRoundComplete;internal DateTime IzanamiReturnAt=DateTime.MinValue;public volatile bool DungeonHighEnabled;public volatile bool PartyReady;public volatile int PartyMembers,PartyCreateAttempts;internal int NpcAction;internal DateTime DungeonStartAt=DateTime.MinValue;
        internal CloseBatchOperation DungeonAutoCloseOperation;public volatile int DungeonKeyCount,DungeonEntries;public volatile bool DungeonStopped,DungeonFinished,DungeonQueueOnly,DungeonReturnConfirmed;
        internal bool DungeonProbeRefused;internal string DungeonProbePopupBefore="";internal HashSet<string> DungeonProbeNotices=new HashSet<string>();internal bool DungeonResumeProbe,DungeonResumeChecked;internal bool DungeonKeyChecked,DungeonCycleWaiting,DungeonLeftOrigin,DungeonRepeatRequested;
        internal int DungeonEntryRetries;internal int DungeonOriginMap=-1;internal DateTime DungeonCycleAt,DungeonReturnAt,DungeonPollAt=DateTime.MinValue;
        internal CloseBatchOperation SonCapLimitCloseOperation;public volatile bool SonCapQueueOnly,SonCapNoAttempts;internal DateTime SonCapReturnAt=DateTime.MinValue;public volatile bool SonCapEnabled,SonCapFinished,SonCapMoving,SonCapEntrySubmitted;public volatile int SonCapStage=-1,SonCapAlive=-1;public volatile string SonCapStatus="",SonCapFailure="";internal bool SonCapRegroupAfterReturn;internal int SonCapPendingMap=-1;internal int SonCapPortalWatchX=Int32.MinValue,SonCapPortalWatchY=Int32.MinValue;internal DateTime SonCapPortalStillAt;internal int SonCapRouteRetries,SonCapRouteLastX=Int32.MinValue,SonCapRouteLastY=Int32.MinValue;internal DateTime SonCapRouteRetryAt,SonCapRouteStillAt;
        internal PortalRightOperation SonCapRightOperation;internal int SonCapRightAttempts;internal DateTime SonCapRightAt,SonCapArrivalAt;internal string SonCapRightError="";
        internal PortalRightOperation SonCapReleaseOperation;internal bool SonCapReleaseAttempted;internal DateTime SonCapAutoRetryAt;internal int SonCapAutoRetries;internal bool SonCapAutoConfirmed,SonCapMapAutoActive;internal int SonCapAutoMap=-1;internal DateTime SonCapAutoSeenAt,SonCapAutoPollAt;
        internal int SonCapClearedStage=-1;internal bool SonCapApplying,SonCapStopPending,SonCapRebound;internal DateTime SonCapPollAt,SonCapMapAt,SonCapEmptyAt,SonCapRouteAt;
        public volatile bool UpRequested;public volatile bool FarmEnabled,FarmArrived,FarmQuietZone;internal bool FarmZoneWaiting;internal volatile bool FarmStopPending;public volatile int CharacterLevel;public volatile string FarmStatus="";public volatile FarmSpot[] FarmObserved=new FarmSpot[0];public volatile int FarmPositionMap=-1,FarmPositionX,FarmPositionY;public DateTime FarmPositionReadAt=DateTime.MinValue;internal FarmSpot[] FarmChoices=new FarmSpot[0];internal FarmSpot FarmTarget;internal bool FarmCharacterMode;internal int FarmLevel;internal DateTime FarmPollAt,FarmRouteAt,FarmStableAt,FarmZoneCheckAt;internal bool FarmRouteSent;
        public volatile SkillInfo[] Skills=new SkillInfo[0];public volatile bool TrainEnabled,SkillScanRequested=true,TrainReady;public volatile int TrainSkillId=-1,TrainStatus,TrainAttempts,TrainTargetId=-1,TrainTargetHp;public volatile int[] HangingSkillIds=new[]{367,271,285,1};public int HangingSkillCursor;internal bool HangingAwaiting;internal DateTime HangingRequestLogAt,HangingResultLogAt;public int HangingRequests;internal DateTime LevelOneNextAt;internal int SkillGeneration,TrainCursor;internal bool TrainIdleApplied;internal bool ReceiveFlowActive;public volatile int[] TrainSkillIds=new int[0];

        public volatile bool GameSettingsReady,GameAutoEnabled,GameAutoWanted;public volatile string GameAutoStatus="Chờ game sẵn sàng";public volatile int EntryAutoActivations;internal int EntryAutoOrigin=-1,EntryAutoKind,EntryCombatMap=-1;internal bool EntryAutoWaiting;internal DateTime EntryAutoAt;public volatile string SettingsError="";
        public volatile GameOption[] GameOptions=new GameOption[0];
        internal bool GameAutoManaged,SettingsInFlight;internal int SettingsAutoSent=-1;internal string LastAutoDiagnostic="";internal DateTime AutoDiagnosticAt,WorkerErrorAt,SettingsAutoAt;internal int SettingsAutoValue=-1;
        internal DateTime SettingsReadAt=DateTime.MinValue;internal readonly ConcurrentQueue<GameOption> SettingsQueue=new ConcurrentQueue<GameOption>();
        public string LogFile;internal int CaptureGeneration;internal string LastMenu="",LastSkills="";
        internal string Target,Password;internal SocksConfig Proxy;internal bool Auto;public bool AutoLoginEnabled {get{return Auto;}}
        internal NativeJob(string target,string user,string pass,SocksConfig proxy,bool auto){Target=Path.GetFullPath(target);Username=user;Password=pass;Proxy=proxy;Auto=auto;}
    }
    public static partial class NativeBackend {
        public static volatile bool AutoReconnectEnabled=true;
        public static void ScanInventory(string id){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.ProcessId==0||j.State=="failed"||j.State=="closed"||!ManagedGames.IsRunning(j.ProcessId))throw new Exception("inventory_session_unavailable");
            j.InventoryScanning=true;Interlocked.Exchange(ref j.InventoryRequested,1);
        }
        static bool SameBytes(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        public static void SetAutoQuest(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested)throw new Exception("quest_session_unavailable");
            if(j.SonCapEnabled&&!j.SonCapApplying)return;
            if(j.DailyManaged&&!j.DailyApplying){j.DailyPlan[3]=enabled;return;}
            if((j.MailQueueOnly||j.CloakQueueOnly)||j.DungeonQueueOnly||j.IzanamiQueueOnly||(!j.QuestQueueOnly&&(j.ReceiveKeyEnabled||j.DungeonHighEnabled||j.IzanamiEnabled)))enabled=false;
            if(enabled&&!j.QuestEnabled){j.QuestFinished=false;j.QuestPhase=0;j.QuestCompleted=0;j.QuestBaseRemaining=-1;j.QuestAt=DateTime.UtcNow;j.QuestRetryAt=DateTime.MinValue;j.QuestInFlight=false;j.QuestBusyAt=DateTime.MinValue;j.QuestBusyRetries=0;}
            j.QuestFaultStopped=false;bool wasEnabled=j.QuestEnabled;j.QuestEnabled=enabled;j.QuestPauseAuto=enabled;j.QuestStatus=enabled?"Đợi dữ liệu nhiệm vụ":"Đã dừng";
            if(!enabled&&wasEnabled){j.GameAutoWanted=false;j.TrainEnabled=false;}
        }
        public static string QuestDisplayStatus(NativeJob j){
            if(j==null||j.CloseRequested||j.CancelRequested||j.State=="closed"||j.State=="failed"||j.State=="cancelled"||(!j.QuestEnabled&&!j.QuestFinished)||j.FarmEnabled||j.SonCapEnabled||j.IzanamiEnabled||j.DungeonHighEnabled||j.DailyActive&&j.DailyStage!=4)return "";
            string title=j.BeastQuestEnabled?"Linh thú":"Nhiệm vụ tuần hoàn";string progress=j.QuestFinished?"Đã xong "+title:j.QuestRemaining>=0?title+": "+j.QuestRemaining+"/"+(j.BeastQuestEnabled?3:10):title+": chờ đọc số lượt";
            return progress+" • "+j.QuestStatus;
        }
        static void StopQuest(NativeJob j,string reason){j.QuestFaultStopped=!j.BeastQuestEnabled&&!j.QuestFinished;j.QuestEnabled=false;j.QuestPauseAuto=false;j.GameAutoManaged=true;j.GameAutoWanted=false;j.TrainEnabled=false;j.QuestStatus=reason;Log(j,"quest_stop",",\"reason\":"+Quote(reason)+",\"completed\":"+j.QuestCompleted);}
        internal static void QuestSend(Remote r,long data,NativeJob j,int command){r.Write(data,BitConverter.GetBytes(command));j.QuestCommand=command;j.QuestInFlight=true;j.QuestRetryAt=DateTime.UtcNow.AddMilliseconds(command==1||command==2?3000:750);Log(j,"quest_command",",\"command\":"+command+",\"phase\":"+j.QuestPhase+",\"remaining\":"+j.QuestRemaining);}
        internal static void PumpQuest(Remote r,long data,NativeJob j){
            if(data==0)return;r.Write(data+116,BitConverter.GetBytes(j.BeastQuestEnabled?1:0));bool soloReady=!j.BeastQuestEnabled||j.BeastSoloReady&&(DateTime.UtcNow-j.TeamReadAt).TotalSeconds<4;bool canRun=soloReady&&(!j.BeastQuestEnabled||j.BeastStartupSandReady&&j.BeastStartupSandPid==j.ProcessId)&&j.QuestEnabled&&!j.CloseRequested&&!j.CancelRequested&&!j.ReceiveFlowActive&&j.GameSettingsReady&&(j.QuestQueueOnly||(!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled&&!j.IzanamiEnabled));
            r.Write(data+4,BitConverter.GetBytes(canRun?1:0));if(!canRun){if(j.BeastQuestEnabled&&j.QuestEnabled&&!j.BeastStartupSandReady){j.TrainEnabled=false;j.QuestPauseAuto=true;if(!j.BeastStartupSandStopped)j.QuestStatus="Linh thú: phù 168 về Làng Cát lần đầu, đợi xác nhận tới làng";}else if(j.BeastQuestEnabled&&j.QuestEnabled&&!soloReady){j.TrainEnabled=false;j.QuestPauseAuto=true;j.QuestAt=DateTime.UtcNow;j.QuestStatus="Linh thú: đợi xác nhận đã rời nhóm cũ để chạy riêng";}if(!j.QuestEnabled&&BitConverter.ToInt32(r.Read(data,4),0)!=99){r.Write(data,BitConverter.GetBytes(r.Ptr(data+72)!=0?6:0));}return;}
            int request=BitConverter.ToInt32(r.Read(data,4),0);
            if(request==99){if(j.QuestBusyAt==DateTime.MinValue)j.QuestBusyAt=DateTime.UtcNow;if((DateTime.UtcNow-j.QuestBusyAt).TotalSeconds>10){r.Write(data,new byte[4]);j.QuestBusyAt=DateTime.MinValue;j.QuestInFlight=false;j.QuestPhase=0;j.QuestRetryAt=DateTime.MinValue;j.QuestBusyRetries++;Log(j,"quest_busy_recovered",",\"retry\":"+j.QuestBusyRetries);if(j.QuestBusyRetries>=3)StopQuest(j,"Lệnh nhiệm vụ lỗi 3 lần; kiểm tra game");else j.QuestStatus="Lệnh bị kẹt; đang thử lại nhiệm vụ";}return;}
            j.QuestBusyAt=DateTime.MinValue;if(request!=0)return;
            int generation=BitConverter.ToInt32(r.Read(data+64,4),0);if((generation&1)!=0||BitConverter.ToInt32(r.Read(data+120,4),0)!=(j.BeastQuestEnabled?1:0))return;byte[] b=r.Read(data+8,108);if(generation!=BitConverter.ToInt32(r.Read(data+64,4),0)||BitConverter.ToInt32(b,0)!=1)return;
            int remaining=BitConverter.ToInt32(b,4),result=BitConverter.ToInt32(b,28);bool present=BitConverter.ToInt32(b,8)!=0,done=BitConverter.ToInt32(b,12)!=0,route=BitConverter.ToInt32(b,24)!=0;
            if(remaining<0||remaining>(j.BeastQuestEnabled?3:10)){j.QuestStatus="Chưa đọc được số lượt nhiệm vụ";return;}if(j.QuestRemaining!=remaining)Log(j,"quest_progress",",\"display\":"+Quote(remaining+"/"+(j.BeastQuestEnabled?3:10))+",\"remaining\":"+remaining);j.QuestObservedCount=BitConverter.ToInt32(b,16);j.QuestObservedTotal=BitConverter.ToInt32(b,20);j.QuestSnapshotAt=DateTime.UtcNow;j.QuestRemaining=remaining;j.QuestMobId=BitConverter.ToInt32(b,100);j.QuestKind=BitConverter.ToInt32(b,104);
            if(j.QuestBaseRemaining<0){j.QuestBaseRemaining=remaining;j.QuestAt=DateTime.UtcNow;}j.QuestCompleted=Math.Max(j.QuestCompleted,j.QuestBaseRemaining-remaining);
            if(remaining==0&&!present){if(j.BeastQuestEnabled)ResetBeastCombatZone(j);j.QuestFinished=true;StopQuest(j,j.BeastQuestEnabled?"Đã hết nhiệm vụ linh thú; server xác nhận acc đã trả xong":"Đã hết nhiệm vụ tuần hoàn; server đã xác nhận trả xong");return;}
            if(j.BeastQuestEnabled&&ShouldCloseBeastRasaMenu(j)&&j.BeastRasaMenu!=0&&DateTime.UtcNow>=j.QuestRetryAt){r.Write(data+128,BitConverter.GetBytes(j.BeastRasaMenu));r.Write(data+56,BitConverter.GetBytes(j.BeastRasaTag));QuestSend(r,data,j,9);j.BeastRasaMenu=0;return;}
            DateTime now=DateTime.UtcNow;bool ack=j.QuestInFlight;
            if((now-j.QuestAt).TotalSeconds>240&&j.QuestPhase!=6){StopQuest(j,"Quá thời gian đi/nhận nhiệm vụ; kiểm tra game");return;}
            if(now<j.QuestRetryAt&&j.QuestPhase!=1)return;j.QuestInFlight=false;
            if(j.QuestPhase==0){j.QuestHadTask=present;j.QuestWasDone=done;j.QuestReturn=!present||done;if(j.BeastQuestEnabled&&j.QuestReturn)ResetBeastCombatZone(j);j.GameAutoManaged=true;j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestStatus=j.QuestReturn?(j.BeastQuestEnabled?"Linh thú: về Rasa nhận/trả nhiệm vụ riêng":"Tắt treo; quay về Mei Terumi"):"Đi tới quái của nhiệm vụ";j.QuestAt=now;if(!(j.BeastQuestEnabled&&j.QuestReturn&&j.BeastNearRasa))QuestSend(r,data,j,j.QuestReturn?1:2);j.QuestPhase=1;return;}
            if(j.QuestPhase==1){if(route)return;if(j.QuestReturn&&!(j.BeastQuestEnabled&&j.BeastNearRasa)&&BitConverter.ToInt32(b,80)>=0&&BitConverter.ToInt32(b,72)!=BitConverter.ToInt32(b,80)){if(now>=j.QuestRetryAt)QuestSend(r,data,j,1);return;}if(!j.QuestReturn&&present&&BitConverter.ToInt32(b,76)>=0&&BitConverter.ToInt32(b,72)!=BitConverter.ToInt32(b,76)){if(now>=j.QuestRetryAt)QuestSend(r,data,j,2);return;}if(ack&&result!=1){j.QuestPhase=0;return;}if(j.QuestReturn){QuestSend(r,data,j,3);j.QuestPhase=2;j.QuestStatus=j.BeastQuestEnabled?"Linh thú: mở menu Rasa":"Mở menu Mei Terumi";}else{if(j.BeastQuestEnabled&&!PrepareBeastCombatZone(j,BitConverter.ToInt32(b,76))){j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestAt=now;j.QuestStatus=j.ZoneStatus;return;}j.QuestPauseAuto=false;j.GameAutoManaged=true;j.GameAutoWanted=false;j.QuestPhase=6;ArmQuestCombat(j);j.QuestRetryAt=DateTime.MinValue;j.QuestStatus="Treo đánh quái; đợi nhiệm vụ hoàn thành";}return;}
            if(j.QuestPhase==2){if(ack&&result==1){QuestSend(r,data,j,4);j.QuestPhase=3;}else{QuestSend(r,data,j,1);j.QuestPhase=1;j.QuestStatus=j.BeastQuestEnabled?"Chưa mở được menu Rasa; thử lại":"Chưa gặp Mei; thử lại đường về trả nhiệm vụ";}return;}
            if(j.QuestPhase==3){if(j.BeastQuestEnabled&&present&&!done){j.QuestPhase=0;j.QuestAt=now;j.QuestStatus="Đã có nhiệm vụ linh thú; tiếp tục nhiệm vụ riêng";return;}bool turnIn=present&&done;QuestSend(r,data,j,turnIn?7:5);j.QuestPhase=4;j.QuestStatus=j.BeastQuestEnabled?(turnIn?"Rasa → Nhiệm vụ thu phục mãnh thú → Hoàn Thành":"Rasa → Nhiệm vụ thu phục mãnh thú → Nhận"):(turnIn?"Chọn Hoàn Thành để trả nhiệm vụ":"Chọn Nhận nhiệm vụ tuần hoàn");return;}
            if(j.QuestPhase==4){if(ack&&result==1){j.QuestPhase=j.QuestCommand==7?7:5;j.QuestAt=now;j.QuestStatus=j.QuestCommand==7?"Đợi server xác nhận trả nhiệm vụ":"Đợi server cập nhật nhiệm vụ";}else{QuestSend(r,data,j,4);j.QuestPhase=3;}return;}
            if(j.QuestPhase==5){if((present&&!done&&(!j.QuestHadTask||j.QuestWasDone))){j.QuestPhase=0;j.QuestAt=now;return;}if(!present&&remaining<j.QuestBaseRemaining){j.QuestPhase=0;j.QuestAt=now;return;}if((now-j.QuestAt).TotalSeconds>30)StopQuest(j,"Đã chọn Nhận nhưng server chưa đổi nhiệm vụ; kiểm tra menu NPC");return;}
            if(j.QuestPhase==7){if(!present||(present&&!done)){if(present){j.QuestPhase=0;}else{QuestSend(r,data,j,3);j.QuestPhase=2;j.QuestStatus="Đã trả nhiệm vụ; mở menu nhận lượt tiếp theo";}j.QuestAt=now;return;}if((now-j.QuestAt).TotalSeconds>30)StopQuest(j,"Đã chọn Hoàn Thành nhưng server chưa xác nhận trả nhiệm vụ; kiểm tra menu NPC");return;}
            if(j.QuestPhase==6){
                if(!present||done){if(j.BeastQuestEnabled)ResetBeastCombatZone(j);j.QuestPhase=0;j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestAt=now;}
                else if(route||BitConverter.ToInt32(b,72)!=BitConverter.ToInt32(b,76)){j.QuestPhase=0;j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestAt=now;j.QuestStatus="Sai map nhiệm vụ; đang đi lại bãi yêu cầu";}
                else {if(j.BeastQuestEnabled&&!PrepareBeastCombatZone(j,BitConverter.ToInt32(b,76))){j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestAt=now;j.QuestStatus=j.ZoneStatus;return;}j.QuestPauseAuto=false;ArmQuestCombat(j);j.QuestStatus=j.QuestKind!=1||j.QuestMobId<0?"Chưa xác định được quái nhiệm vụ; tạm dừng đánh":!j.TrainEnabled?"Đợi kỹ năng tấn công để đánh đúng quái nhiệm vụ":"Quái nhiệm vụ ID "+j.QuestMobId+": "+BitConverter.ToInt32(b,16)+"/"+BitConverter.ToInt32(b,20)+" • Đã trả: "+j.QuestCompleted+" • Còn: "+remaining+(j.BeastQuestEnabled?" • Khu "+j.ZoneId:"");}
            }
        }
        // Dùng bộ đánh có lọc mục tiêu, tắt treo mặc định để tránh tự chọn nhầm quái.
        internal static void ArmQuestCombat(NativeJob j){
            j.GameAutoManaged=true;j.GameAutoWanted=false;
            if(j.QuestKind!=1||j.QuestMobId<0){j.TrainEnabled=false;return;}
            SkillInfo attack=null;
            foreach(SkillInfo skill in j.Skills)if(skill.Attack){if(attack==null||skill.Id==295)attack=skill;if(skill.Id==295)break;}
            if(attack==null){j.TrainEnabled=false;j.SkillScanRequested=true;return;}
            if(j.TrainSkillIds.Length!=1||j.TrainSkillIds[0]!=attack.Id){j.TrainSkillIds=new int[]{attack.Id};j.TrainCursor=0;j.TrainSkillId=attack.Id;Log(j,"quest_target",",\"mob_template_id\":"+j.QuestMobId+",\"skill_id\":"+attack.Id);}
            j.TrainEnabled=true;
        }
        public static string[] NormalizeGiftCodes(string[] codes){
            List<string> clean=new List<string>();HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string value in codes??new string[0]){string code=(value??"").Trim();if(code.Length==0)continue;if(code.Length>30)throw new ArgumentException("Mỗi gift code tối đa 30 ký tự.");foreach(char c in code)if(Char.IsControl(c))throw new ArgumentException("Gift code có ký tự điều khiển.");if(seen.Add(code))clean.Add(code);if(clean.Count>1000)throw new ArgumentException("Tối đa 1000 gift code.");}
            return clean.ToArray();
        }
        public static void ConfigureGiftCodes(string id,bool enabled,string[] codes){
            string[] clean=NormalizeGiftCodes(codes);if(enabled&&clean.Length==0)throw new ArgumentException("Nhập ít nhất một gift code.");NativeJob j=Get(id);if(j==null)throw new Exception("game_session_unavailable");
            if(j.CloakQueueOnly)enabled=false;lock(j.GiftGate){j.GiftCodes=clean;j.GiftEnabled=enabled;j.GiftStatus=enabled?"Đợi game sẵn sàng":"Tắt";}
        }
        internal static bool IsGiftResponse(string text){string s=(text??"").ToLowerInvariant();return !s.Contains("bảo vệ")&&(s.Contains("code")||s.Contains("quà")||s.Contains("mã không")||s.Contains("mã đã")||s.Contains("mã này")||s.Contains("phần thưởng"));}
        internal static void PumpGiftCodes(Remote r,long data,long game,NativeJob j){
            lock(j.GiftGate){
                if(j.CloseRequested||j.CancelRequested){j.GiftEnabled=false;r.Write(data,new byte[4]);return;}
                if(!j.GiftEnabled&&j.GiftWaiting&&BitConverter.ToInt32(r.Read(data,4),0)==1){r.Write(data,new byte[4]);j.GiftWaiting=false;j.GiftStatus="Tắt";return;}
                DateTime now=DateTime.UtcNow;
                if(j.GiftWaiting){
                    int result=BitConverter.ToInt32(r.Read(data+4,4),0);
                    if(result==2){j.GiftWaiting=false;j.GiftResultWaiting=true;j.GiftAt=now;j.GiftNext=now.AddSeconds(3);j.GiftRequests++;j.GiftStatus="Đã gửi "+j.GiftCurrent+"; chờ phản hồi";Log(j,"gift_code_submitted",",\"code\":"+Quote(j.GiftCurrent));}
                    else if(result==3||(now-j.GiftAt).TotalSeconds>15){j.GiftWaiting=false;r.Write(data,new byte[4]);j.GiftStatus="Không gửi được "+j.GiftCurrent;j.GiftResponse=j.GiftStatus;j.GiftNext=now.AddSeconds(3);Log(j,"gift_code_send_failed",",\"code\":"+Quote(j.GiftCurrent));}
                    else return;
                }
                if(j.GiftResultWaiting){
                    string response="";foreach(var entry in ReadKeyNotifications(r,game))if(j.GiftNoticeSeen.Add(entry.Key)&&IsGiftResponse(entry.Value))response=entry.Value;
                    string popup=ReadKeyNotice(r,game);if(popup!=j.GiftPopupBefore&&IsGiftResponse(popup))response=popup;
                    if(response.Length>0){j.GiftResponse=j.GiftCurrent+": "+response;j.GiftStatus="Đã có phản hồi";j.GiftResultWaiting=false;Log(j,"gift_code_response",",\"code\":"+Quote(j.GiftCurrent)+",\"message\":"+Quote(response));}
                    else if((now-j.GiftAt).TotalSeconds>=7){j.GiftResponse=j.GiftCurrent+": Đã gửi, chưa đọc được phản hồi";j.GiftResultWaiting=false;Log(j,"gift_code_response_timeout",",\"code\":"+Quote(j.GiftCurrent));}
                    else return;
                }
                if(!j.GiftEnabled){r.Write(data,new byte[4]);j.GiftStatus="Tắt";return;}
                if(now<j.GiftNext)return;
                string next=null;foreach(string code in j.GiftCodes)if(!j.GiftAttempted.Contains(code)){next=code;break;}
                if(next==null){j.GiftStatus="Đã xử lý hết "+j.GiftCodes.Length+" mã trong phiên";return;}
                if(!j.TrainReady||AuxiliaryActionBlocked(j)||j.IzanamiRequested||(j.MailQueueOnly||j.CloakQueueOnly)||j.MailEnabled||j.MailBusy){j.GiftStatus="Đợi game / thao tác NPC hoàn tất";return;}
                j.GiftNoticeSeen=new HashSet<string>(ReadKeyNotifications(r,game).Keys);j.GiftPopupBefore=ReadKeyNotice(r,game);j.GiftCurrent=next;j.GiftAttempted.Add(next);j.GiftWaiting=true;j.GiftAt=now;j.GiftResponse="";
                byte[] raw=Encoding.UTF8.GetBytes(next+"\0");r.Write(data+1024,raw);r.Write(data+4,new byte[4]);r.Write(data,BitConverter.GetBytes(1));j.GiftStatus="Đang gửi "+next;
            }
        }
        public static void ConfigureInbox(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||!j.Auto)throw new Exception("game_session_unavailable");
            lock(j.MailGate){if(enabled&&!j.InboxSupported)throw new Exception("Nhận thư tự động chưa hỗ trợ bản game này; đăng nhập vẫn dùng được.");if(enabled&&j.InboxBusy)throw new Exception("Đợi lượt nhận thư hiện tại.");j.InboxEnabled=enabled;if(enabled){j.InboxObservedAt=DateTime.MinValue;j.InboxPendingAttachments=-1;j.InboxPhase=0;j.InboxLoaded=null;j.InboxNeedsRefresh=false;j.InboxTarget=null;j.InboxNext=DateTime.MinValue;j.InboxStatus="Đợi nhận thư";}else j.InboxStatus="Đã dừng nhận thư";Log(j,"inbox_configured",",\"enabled\":"+(enabled?"true":"false"));}
        }
        internal static InboxLetter[] ReadInbox(Remote r,long game){
            long fcClass=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(fcClass+0xb8)+16);
            if(fc==0||r.Ptr(fc)!=fcClass)throw new Exception("inbox_not_ready");
            long mailClass=ResolvedClass(r,game+0x16ef960);List<InboxLetter> result=new List<InboxLetter>();
            foreach(long ptr in ReadVector(r,r.Ptr(fc+0xf0))){
                if(ptr==0||r.Ptr(ptr)!=mailClass)throw new Exception("inbox_type_changed");
                byte[] b=r.Read(ptr+16,72);InboxLetter m=new InboxLetter();m.Pointer=ptr;m.Id=BitConverter.ToInt32(b,0);m.Read=b[64]!=0;
                long item=BitConverter.ToInt64(b,32);m.HasAttachment=item!=0||BitConverter.ToInt32(b,40)>0||BitConverter.ToInt32(b,44)>0||BitConverter.ToInt32(b,48)>0||BitConverter.ToInt32(b,52)>0||BitConverter.ToInt64(b,56)>0;
                if(item!=0){m.TemplateId=BitConverter.ToInt32(r.Read(item+20,4),0);m.Quantity=BitConverter.ToInt32(r.Read(item+24,4),0);}
                result.Add(m);
            }return result.ToArray();
        }
        internal static bool InboxReceived(InboxLetter current,InboxLetter target,InventorySnapshot bag,long before){
            return current!=null&&current.Id==target.Id&&!current.HasAttachment&&target.Quantity>0&&MailItemTotal(bag,target.TemplateId)==before+target.Quantity;
        }
        // ID thư có thể được server dùng lại khi thư mới đến ngay sau Xóa đã đọc.
        // Thư mới còn đính kèm phải được giữ để nhận, không coi là thư rỗng cũ.
        internal static bool InboxDeletionConfirmed(int[] deletedIds,InboxLetter[] current){
            foreach(int id in deletedIds)if(Array.Exists(current,x=>x.Id==id&&!x.HasAttachment))return false;
            return true;
        }
        internal static bool InboxCanDelete(InboxLetter m){return m!=null&&m.Read&&!m.HasAttachment;}
        static void HoldInbox(NativeJob j,string reason){j.InboxEnabled=false;j.InboxBusy=false;j.InboxStatus=reason;Log(j,"inbox_stopped",",\"reason\":"+Quote(reason));}
        static void QueueInbox(Remote r,long data,NativeJob j,int command,InboxLetter letter){
            j.InboxCommand=command;j.InboxPollAt=DateTime.MinValue;j.InboxBusy=true;j.InboxAt=DateTime.UtcNow;j.InboxListBefore=BitConverter.ToInt32(r.Read(data+24,4),0);
            r.Write(data+4,BitConverter.GetBytes(command));r.Write(data+8,new byte[4]);r.Write(data+12,BitConverter.GetBytes(1));
            if(letter!=null){r.Write(data+16,BitConverter.GetBytes(letter.Id));r.Write(data+20,BitConverter.GetBytes(letter.TemplateId));r.Write(data+28,BitConverter.GetBytes(letter.Quantity));r.Write(data+32,BitConverter.GetBytes(letter.Pointer));}
            r.Write(data,BitConverter.GetBytes(1));Log(j,"inbox_action_queued",",\"command\":"+command+",\"mail_id\":"+(letter==null?-1:letter.Id));
        }
        internal static InboxLetter[] ReceivedInboxBatch(InboxLetter[] before,InboxLetter[] current,InventorySnapshot bag,Dictionary<int,long> totals){
            List<InboxLetter> done=new List<InboxLetter>();Dictionary<int,long> expected=new Dictionary<int,long>();
            foreach(InboxLetter old in before){InboxLetter m=Array.Find(current,delegate(InboxLetter x){return x.Id==old.Id;});if(m==null||m.HasAttachment)continue;done.Add(m);if(old.TemplateId>=0&&old.Quantity>0){if(!expected.ContainsKey(old.TemplateId))expected[old.TemplateId]=0;expected[old.TemplateId]+=old.Quantity;}}
            foreach(KeyValuePair<int,long> e in expected)if(!totals.ContainsKey(e.Key)||MailItemTotal(bag,e.Key)!=totals[e.Key]+e.Value)return new InboxLetter[0];
            return done.ToArray();
        }
        internal static void PumpInbox(Remote r,long data,long game,NativeJob j){
            lock(j.MailGate){
                DateTime now=DateTime.UtcNow;
                if(!j.InboxEnabled||j.CloseRequested||j.CancelRequested||j.ReconnectRequested){r.Write(data+12,new byte[4]);if(BitConverter.ToInt32(r.Read(data,4),0)==1)r.Write(data,new byte[4]);j.InboxBusy=false;return;}
                if(j.InboxBusy){
                    int state=BitConverter.ToInt32(r.Read(data,4),0),result=BitConverter.ToInt32(r.Read(data+8,4),0);
                    if(state!=0){if((now-j.InboxAt).TotalSeconds>15)HoldInbox(j,"Dừng: game chưa xử lý nhận thư");return;}
                    if(result!=2){HoldInbox(j,"Dừng: thao tác thư chưa thực hiện; chưa tự gửi lại");return;}
                    if(j.InboxCommand!=1){j.InboxBusy=false;if(j.InboxCommand==5)j.InboxPhase=2;else if(j.InboxCommand==6)j.InboxPhase=4;else if(j.InboxCommand==4)j.InboxPhase=5;j.InboxNeedsRefresh=true;j.InboxNext=now.AddMilliseconds(350);return;}
                    if(now<j.InboxPollAt)return;j.InboxPollAt=now.AddMilliseconds(250);
                    bool ack=BitConverter.ToInt32(r.Read(data+24,4),0)!=j.InboxListBefore;if(!ack&&(now-j.InboxAt).TotalMilliseconds<250)return;
                    InboxLetter[] letters=ReadInbox(r,game);
                    if(j.InboxPhase==2){
                        InboxLetter[] received=ReceivedInboxBatch(j.InboxBatch,letters,ReadInventorySnapshot(r,game),j.InboxBatchTotals);
                        if(received.Length==0||(received.Length<j.InboxBatch.Length&&(now-j.InboxAt).TotalSeconds<2)){if((now-j.InboxAt).TotalSeconds>=30)HoldInbox(j,"Dừng: chưa xác nhận Nhận tất cả; giữ thư còn đồ");return;}
                        j.InboxReceived+=received.Length;j.InboxBatch=received;j.InboxPhase=3;Log(j,"inbox_receive_all_confirmed",",\"letters\":"+received.Length);
                    }else if(j.InboxPhase==4){
                        if(!InboxDeletionConfirmed(j.InboxDeleteIds,letters)){if((now-j.InboxAt).TotalSeconds>=30)HoldInbox(j,"Dừng: chưa xác nhận Xóa đã đọc; chưa tự xóa lại");return;}
                        int reused=Array.FindAll(letters,x=>x.HasAttachment&&Array.IndexOf(j.InboxDeleteIds,x.Id)>=0).Length;if(reused>0)Log(j,"inbox_deleted_ids_reused",",\"new_attached_letters\":"+reused);
                        j.InboxDeleted+=j.InboxDeleteIds.Length;Log(j,"inbox_delete_read_confirmed",",\"letters\":"+j.InboxDeleteIds.Length);j.InboxPhase=0;j.InboxBatch=new InboxLetter[0];
                    }else if(j.InboxPhase==5){
                        InboxLetter marked=Array.Find(letters,delegate(InboxLetter x){return j.InboxTarget!=null&&x.Id==j.InboxTarget.Id;});
                        if(marked==null||marked.HasAttachment||!marked.Read){if((now-j.InboxAt).TotalSeconds>=30)HoldInbox(j,"Dừng: thư chưa đọc / còn đồ; giữ nguyên thư");return;}j.InboxPhase=3;
                    }
                    j.InboxObservedAt=now;j.InboxPendingAttachments=Array.FindAll(letters,x=>x.HasAttachment).Length;j.InboxLoaded=letters;j.InboxBusy=false;j.InboxNext=now.AddMilliseconds(200);Log(j,"inbox_list_observed",",\"count\":"+letters.Length+",\"phase\":"+j.InboxPhase+",\"callback_changed\":"+(ack?"true":"false"));return;
                }
                if(now<j.InboxNext||!j.GameSettingsReady||AuxiliaryActionBlocked(j)||j.MailBusy)return;
                if(j.InboxNeedsRefresh||j.InboxLoaded==null){j.InboxNeedsRefresh=false;QueueInbox(r,data,j,1,null);return;}
                InboxLetter[] snapshot=ReadInbox(r,game);
                if(j.InboxPhase==3){
                    InboxLetter unread=Array.Find(snapshot,delegate(InboxLetter m){return !m.Read&&!m.HasAttachment&&Array.Exists(j.InboxBatch,delegate(InboxLetter x){return x.Id==m.Id;});});
                    if(unread!=null){j.InboxTarget=unread;QueueInbox(r,data,j,4,unread);j.InboxStatus="Đánh dấu thư đã nhận là đã đọc";return;}
                }
                if(j.InboxPhase==0){
                    InboxLetter[] attached=Array.FindAll(snapshot,delegate(InboxLetter m){return m.HasAttachment;});
                    if(attached.Length>0){
                        InventorySnapshot bag=ReadInventorySnapshot(r,game);if(bag.Items.Length>=bag.Capacity){j.InboxNext=now.AddSeconds(3);j.InboxNeedsRefresh=true;j.InboxStatus="Túi đầy; giữ thư, đợi có ô trống";return;}
                        j.InboxBatch=attached;j.InboxBatchTotals=new Dictionary<int,long>();foreach(InboxLetter m in attached)if(m.TemplateId>=0&&!j.InboxBatchTotals.ContainsKey(m.TemplateId))j.InboxBatchTotals[m.TemplateId]=MailItemTotal(bag,m.TemplateId);
                        QueueInbox(r,data,j,5,null);j.InboxStatus="Đang Nhận tất cả • "+attached.Length+" thư";return;
                    }
                }
                InboxLetter[] read=Array.FindAll(snapshot,delegate(InboxLetter m){return InboxCanDelete(m);});
                if(read.Length>0){List<int> ids=new List<int>();foreach(InboxLetter m in read)ids.Add(m.Id);j.InboxDeleteIds=ids.ToArray();QueueInbox(r,data,j,6,null);j.InboxStatus="Đang Xóa đã đọc • "+read.Length+" thư rỗng";return;}
                j.InboxPhase=0;j.InboxNext=now.AddSeconds(3);j.InboxNeedsRefresh=true;j.InboxStatus="Đợi thư mới • Nhận tất cả / Xóa đã đọc";
            }
        }
        public static void ConfigureMail(string id,string recipient,int[] ids,int[] quantities,int intervalMs){
            recipient=(recipient??"").Trim();
            if(recipient.Length==0||recipient.Length>50||recipient.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new ArgumentException("Tên nhân vật nhận thư không hợp lệ.");
            if(ids==null||quantities==null||ids.Length==0||ids.Length!=quantities.Length||ids.Length>1000||intervalMs<1000||intervalMs>3600000)throw new ArgumentException("Danh sách hoặc thời gian gửi thư không hợp lệ.");
            HashSet<int> seen=new HashSet<int>();for(int i=0;i<ids.Length;i++)if(ids[i]<0||ids[i]>65535||quantities[i]!=-1&&quantities[i]<=0||!seen.Add(ids[i]))throw new ArgumentException("ID phải khác nhau; số lượng là -1 hoặc lớn hơn 0.");
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested)throw new Exception("game_session_unavailable");
            lock(j.MailGate){
                if(j.MailBusy||j.MailEnabled)throw new Exception("Dừng lượt gom đồ hiện tại trước khi bắt đầu lượt mới.");
                if(String.Equals(recipient,j.CharacterName,StringComparison.OrdinalIgnoreCase))throw new Exception("Không gửi đồ cho chính nhân vật đang mở.");
                j.MailRecipient=recipient;j.MailIds=(int[])ids.Clone();j.MailQuantities=(int[])quantities.Clone();j.MailIntervalMs=intervalMs;j.MailIndex=0;j.MailRemaining=-1;j.MailLetters=0;j.MailQuantity=0;j.MailChanges=0;j.MailPollAt=DateTime.MinValue;j.MailNext=DateTime.MinValue;j.MailDone=false;j.MailEnabled=true;j.MailStatus="Đợi game sẵn sàng";
                Log(j,"mail_collection_started",",\"recipient\":"+Quote(recipient)+",\"id_count\":"+ids.Length+",\"interval_ms\":"+intervalMs);
            }
        }
        public static void StopMail(string id){NativeJob j=Get(id);if(j==null)return;lock(j.MailGate){j.InboxEnabled=false;j.InboxBusy=false;j.InboxStatus="Game đã thoát";j.MailEnabled=false;j.MailStatus=j.MailBusy?"Đã dừng; đợi xác nhận thư đang gửi":"Đã dừng";}}
        static void HoldMail(NativeJob j,string reason){j.MailEnabled=false;j.MailBusy=false;j.MailStatus=reason;Log(j,"mail_collection_stopped",",\"reason\":"+Quote(reason));}
        internal static long MailItemTotal(InventorySnapshot bag,int id){long n=0;foreach(InventoryItem item in bag.Items)if(item.TemplateId==id&&item.Quantity>0)n+=item.Quantity;return n;}
        internal static long MailSendableTotal(InventorySnapshot bag,int id){long n=0;foreach(InventoryItem item in bag.Items)if(item.TemplateId==id&&item.Quantity>0&&!item.Locked)n+=item.Quantity;return n;}
        internal static InventoryItem PlanMailItem(InventorySnapshot bag,int id,long remaining){
            InventoryItem large=null,best=null;foreach(InventoryItem item in bag.Items){if(item.TemplateId!=id||item.Quantity<=0||item.Locked||item.Slot!=item.BagIndex)continue;
                if(item.Quantity<=remaining&&(best==null||item.Quantity>best.Quantity))best=item;else if(large==null||item.Quantity<large.Quantity)large=item;}
            return best??large;
        }
        internal static InventoryItem ObserveMailSplit(NativeJob j,InventorySnapshot bag){
            int id=j.MailIds[j.MailIndex];if(MailItemTotal(bag,id)!=j.MailBeforeTotal)return null;
            InventoryItem source=null;foreach(InventoryItem item in bag.Items)if(item.TemplateId==id&&item.Slot==j.MailSlot)source=item;
            if(source==null||source.Locked||source.Quantity!=j.MailExpected-j.MailAmount)return null;
            foreach(InventoryItem item in bag.Items)if(item.TemplateId==id&&!item.Locked&&item.Quantity==j.MailAmount&&!j.MailBeforeSlots.ContainsKey(item.Slot))return item;
            return null;
        }
        internal static bool ObserveMailSent(NativeJob j,InventorySnapshot bag,int acknowledgement){
            if(acknowledgement==j.MailAckBefore)return false;
            int id=j.MailIds[j.MailIndex];foreach(InventoryItem item in bag.Items)if(item.Slot==j.MailSlot&&item.TemplateId==id)return false;
            return MailItemTotal(bag,id)==j.MailBeforeTotal-j.MailAmount;
        }
        // Chỉ xác nhận khi túi giảm đúng chồng đồ và có phản hồi máy chủ.
        // Một số đường phản hồi không đi qua bqk: phí thư giảm đúng 10 bạc
        // là bằng chứng bổ sung; không dựa riêng vào việc đã gọi Send.
        internal static long ReadMailSilver(Remote r,long game){
            long cls=ResolvedClass(r,game+0x16ee330),player=r.Ptr(r.Ptr(cls+0xb8)+8);
            if(player==0)throw new Exception("mail_player_unavailable");
            return BitConverter.ToInt64(r.Read(player+0xb8,8),0);
        }
        internal static bool ObserveMailDelivery(NativeJob j,InventorySnapshot bag,int acknowledgement,long silver){
            if(acknowledgement!=j.MailAckBefore)return ObserveMailSent(j,bag,acknowledgement);
            if(j.MailBeforeSilver<10||silver!=j.MailBeforeSilver-10)return false;
            return ObserveMailSent(j,bag,unchecked(j.MailAckBefore+1));
        }
        // Giống pn.dsc: một byte độ dài và chỉ số ký tự trong bảng riêng của game.
        // Không dùng UTF-8 để kiểm tra buffer gửi đi (UTF-8 chỉ dùng tạo System.String).
        internal static byte[] ExpectedMailWire(string alphabet,string body,string title,string recipient,int slot,int command,int amount){
            List<byte> bytes=new List<byte>();
            if(command==1){foreach(string text in new[]{recipient,title,body}){
                if(String.IsNullOrEmpty(text)||text.Length>255)throw new Exception("mail_text_not_encodable");
                bytes.Add((byte)text.Length);foreach(char ch in text){int index=alphabet.IndexOf(ch);if(index<0||index>255)throw new Exception("mail_character_not_encodable");bytes.Add((byte)index);}
            }bytes.AddRange(new byte[4]);}
            bytes.Add((byte)(slot>>8));bytes.Add((byte)slot);
            if(command==2){bytes.Add((byte)(amount>>8));bytes.Add((byte)amount);}
            return bytes.ToArray();
        }
        internal static bool IsMailRecipientFailure(string text){return text.IndexOf("không tìm thấy người chơi",StringComparison.OrdinalIgnoreCase)>=0&&text.IndexOf("offline",StringComparison.OrdinalIgnoreCase)>=0;}
        static void LogMailWire(Remote r,long data,NativeJob j){
            if(j.MailWireLogged)return;j.MailWireLogged=true;
            try{int size=BitConverter.ToInt32(r.Read(data+36,4),0),opcode=BitConverter.ToInt32(r.Read(data+40,4),0);
                if(size<1||size>512){Log(j,"mail_wire_unavailable","");return;}
                int expectedSize=BitConverter.ToInt32(r.Read(data+44,4),0);if(expectedSize<1||expectedSize>512)throw new Exception("mail_expected_size_invalid");byte[] actual=r.Read(data+2048,size),expected=r.Read(data+2560,expectedSize);
                bool same=actual.Length==expected.Length;for(int i=0;same&&i<actual.Length;i++)same=actual[i]==expected[i];
                Log(j,"mail_wire_checked",",\"opcode\":"+opcode+",\"recipient\":"+Quote(j.MailRecipient)+",\"slot\":"+j.MailSlot+",\"matches_expected\":"+(same?"true":"false")+",\"payload_hex\":"+Quote(BitConverter.ToString(actual)));
            }catch(Exception e){Log(j,"mail_wire_read_error",",\"reason\":"+Quote(e.Message));}
        }
        static void QueueMail(Remote r,long data,long game,NativeJob j,InventorySnapshot bag,InventoryItem item,int command,int amount){
            j.MailBeforeSlots=new Dictionary<int,int>();foreach(InventoryItem entry in bag.Items)if(entry.TemplateId==item.TemplateId)j.MailBeforeSlots[entry.Slot]=entry.Quantity;
            j.MailBeforeTotal=MailItemTotal(bag,item.TemplateId);j.MailSlot=item.Slot;j.MailExpected=item.Quantity;j.MailAmount=amount;j.MailCommand=command;j.MailAckBefore=BitConverter.ToInt32(r.Read(data+28,4),0);
            r.Write(data+1024,Encoding.UTF8.GetBytes(j.MailRecipient+"\0"));r.Write(data+1408,Encoding.UTF8.GetBytes("Gom do\0"));r.Write(data+1792,Encoding.UTF8.GetBytes("Vat pham ID "+item.TemplateId+"\0"));
            byte[] expected;
            try{string alphabet=ReadLabel(r,r.Ptr(r.Ptr(ResolvedClass(r,game+0x16ee880)+0xb8)+0x48));
                expected=ExpectedMailWire(alphabet,"Vat pham ID "+item.TemplateId,"Gom do",j.MailRecipient,item.Slot,command,amount);
            }catch(Exception e){HoldMail(j,"Dừng: không kiểm tra được dữ liệu thư • "+e.Message);return;}
            j.MailBeforeSilver=ReadMailSilver(r,game);j.MailPopupBefore=ReadKeyNotice(r,game);
            r.Write(data+36,new byte[8]);r.Write(data+44,BitConverter.GetBytes(expected.Length));r.Write(data+2560,expected);
            r.Write(data+4,BitConverter.GetBytes(command));r.Write(data+8,new byte[4]);r.Write(data+16,BitConverter.GetBytes(item.TemplateId));r.Write(data+20,BitConverter.GetBytes(item.Slot));r.Write(data+24,BitConverter.GetBytes(item.Quantity));r.Write(data+32,BitConverter.GetBytes(amount));
            j.MailWireLogged=false;j.MailBusy=true;j.MailAt=DateTime.UtcNow;j.MailPollAt=DateTime.MinValue;r.Write(data+12,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(1));
            j.MailStatus=(command==2?"Đang tách ":"Đang gửi ")+amount+" món • ID "+item.TemplateId+" → "+j.MailRecipient;
            Log(j,command==2?"mail_split_queued":"mail_attachment_queued",",\"template_id\":"+item.TemplateId+",\"slot\":"+item.Slot+",\"quantity\":"+amount+",\"recipient\":"+Quote(j.MailRecipient)+",\"money\":0");
        }
        internal static void PumpMail(Remote r,long data,long game,NativeJob j){
            lock(j.MailGate){
                DateTime now=DateTime.UtcNow;bool stopping=!j.MailEnabled||j.CloseRequested||j.CancelRequested||j.ReconnectRequested;
                if(stopping)r.Write(data+12,new byte[4]);
                if(j.MailBusy){
                    int state=BitConverter.ToInt32(r.Read(data,4),0),result=BitConverter.ToInt32(r.Read(data+8,4),0);
                    if(stopping&&state==1){r.Write(data,new byte[4]);j.MailBusy=false;j.MailStatus="Đã dừng";return;}
                    if(state==0&&(result==2||result==8))LogMailWire(r,data,j);
                    if(result!=2){
                        if(state!=0){if((now-j.MailAt).TotalSeconds<15)return;r.Write(data,new byte[4]);HoldMail(j,"Dừng: game chưa xử lý lượt gửi; kiểm tra thư và túi trước khi thử lại");return;}
                        j.MailBusy=false;
                        if(!stopping&&(result==1||result==3)&&++j.MailChanges<=3){j.MailStatus="Ô túi / màn chơi đã thay đổi; đợi đọc lại";j.MailNext=now.AddSeconds(1);return;}
                        HoldMail(j,result==8?"Dừng: dữ liệu thư không khớp; chưa gửi lên máy chủ, xem mail_wire_checked":result==7?"Dừng: món đồ đã bị khóa trước khi gửi; chưa tách hoặc gửi thư":result==4?"Dừng: không đủ 10 bạc phí gửi thư":result==5?"Dừng: chưa tách được số lượng này":"Dừng: chưa gửi được thư; kiểm tra game");return;
                    }
                    if(now<j.MailPollAt)return;j.MailPollAt=now.AddSeconds(1);
                    if(j.MailCommand==1){string popup=ReadKeyNotice(r,game);if(popup!=j.MailPopupBefore&&IsMailRecipientFailure(popup)){Log(j,"mail_server_rejected",",\"recipient\":"+Quote(j.MailRecipient)+",\"message\":"+Quote(popup));HoldMail(j,"Dừng: "+popup+" • người nhận: "+j.MailRecipient+" • chưa tự gửi lại");return;}}
                    InventorySnapshot current;long silver;try{current=ReadInventorySnapshot(r,game);silver=ReadMailSilver(r,game);}catch{if((now-j.MailAt).TotalSeconds>=30)HoldMail(j,"Dừng: không đọc được túi sau khi gửi; chưa tự gửi lại");return;}
                    j.Inventory=current;
                    if(j.MailCommand==2){
                        InventoryItem split=ObserveMailSplit(j,current);
                        if(split!=null){j.MailBusy=false;if(stopping){j.MailStatus="Đã tách đồ và dừng; chưa gửi thư";return;}QueueMail(r,data,game,j,current,split,1,split.Quantity);return;}
                    }else if(ObserveMailDelivery(j,current,BitConverter.ToInt32(r.Read(data+28,4),0),silver)){
                        j.MailBusy=false;j.MailLetters++;j.MailQuantity+=j.MailAmount;j.MailRemaining-=j.MailAmount;j.MailChanges=0;j.MailNext=now.AddMilliseconds(j.MailIntervalMs);
                        Log(j,"mail_attachment_confirmed",",\"template_id\":"+j.MailIds[j.MailIndex]+",\"quantity\":"+j.MailAmount+",\"recipient\":"+Quote(j.MailRecipient));
                        j.MailStatus=stopping?"Đã dừng • thư cuối đã xác nhận":"Đã gửi thành công • chờ lượt tiếp theo";return;
                    }
                    if((now-j.MailAt).TotalSeconds>=30)HoldMail(j,j.MailCommand==2?"Dừng: chưa xác nhận tách đồ; kiểm tra túi":"Dừng: chưa xác nhận gửi thư; kiểm tra hộp thư / đồ khóa / tên người nhận. Chưa tự gửi lại");
                    return;
                }
                if(stopping||j.MailDone)return;
                if(String.Equals(j.CharacterName,j.MailRecipient,StringComparison.OrdinalIgnoreCase)){HoldMail(j,"Dừng: người nhận trùng chính nhân vật gửi");return;}
                if(now<j.MailNext||now<j.MailPollAt)return;
                if(!j.GameSettingsReady||AuxiliaryActionBlocked(j)||j.ZoneSyncActive||j.GiftWaiting||j.GiftResultWaiting){j.MailStatus="Đợi game / thao tác NPC hoàn tất";return;}
                j.MailPollAt=now.AddSeconds(1);
                InventorySnapshot bag;try{bag=ReadInventorySnapshot(r,game);}catch{j.MailStatus="Đợi đọc hành trang";return;}j.Inventory=bag;
                while(j.MailIndex<j.MailIds.Length){
                    int id=j.MailIds[j.MailIndex];long available=MailSendableTotal(bag,id);
                    if(j.MailRemaining<0){j.MailRemaining=j.MailQuantities[j.MailIndex]==-1?available:Math.Min(available,j.MailQuantities[j.MailIndex]);Log(j,"mail_item_scan",",\"template_id\":"+id+",\"requested\":"+j.MailQuantities[j.MailIndex]+",\"total\":"+MailItemTotal(bag,id)+",\"unlocked\":"+available+",\"planned\":"+j.MailRemaining);}
                    if(j.MailRemaining==0){Log(j,"mail_item_completed",",\"template_id\":"+id+",\"next_index\":"+(j.MailIndex+1));j.MailIndex++;j.MailRemaining=-1;continue;}
                    if(available<j.MailRemaining){HoldMail(j,"Dừng: số lượng trong túi thay đổi ngoài lượt gửi; kiểm tra ID "+id);return;}
                    InventoryItem item=PlanMailItem(bag,id,j.MailRemaining);if(item==null){HoldMail(j,"Dừng: chưa tìm được ô túi hợp lệ cho ID "+id);return;}
                    if(item.Quantity>j.MailRemaining){
                        if(bag.Items.Length>=bag.Capacity){HoldMail(j,"Dừng: túi đầy, không có ô để tách chồng đồ");return;}
                        QueueMail(r,data,game,j,bag,item,2,(int)Math.Min(32767,j.MailRemaining));
                    }else QueueMail(r,data,game,j,bag,item,1,item.Quantity);
                    return;
                }
                j.MailDone=true;j.MailEnabled=false;j.MailStatus="Đã gom xong đồ không khóa • "+j.MailLetters+" thư / "+j.MailQuantity+" món → "+j.MailRecipient;Log(j,"mail_collection_completed",",\"letters\":"+j.MailLetters+",\"quantity\":"+j.MailQuantity);
            }
        }

        public static void ConfigureAutoItemPlan(string id,bool enabled,int[] repeat,int[] once,int intervalMs){
            if(repeat==null||once==null||repeat.Length+once.Length==0||intervalMs<1000||intervalMs>3600000)throw new ArgumentException("auto_item_invalid");
            foreach(int value in repeat)if(value<0||value>65535)throw new ArgumentException("invalid_item_id");
            foreach(int value in once)if(value<0||value>65535)throw new ArgumentException("invalid_item_id");
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested)throw new Exception("auto_item_session_unavailable");
            j.AutoItemEnabled=false;j.AutoItemIds=(int[])repeat.Clone();j.AutoItemOnceIds=(int[])once.Clone();j.AutoItemCursor=0;j.AutoItemIntervalMs=intervalMs;j.AutoItemEnabled=enabled&&!j.CloakQueueOnly;
        }
        internal static int NextAutoItem(NativeJob j){
            foreach(int value in j.AutoItemOnceIds)if(j.AutoItemOnceAttempted.Add(value))return value;
            int[] ids=j.AutoItemIds;for(int i=0;i<ids.Length;i++){int value=ids[j.AutoItemCursor%ids.Length];j.AutoItemCursor=(j.AutoItemCursor+1)%ids.Length;if(Array.IndexOf(j.AutoItemOnceIds,value)<0)return value;}return -1;
        }
        public static void ConfigureAutoItems(string id,bool enabled,int[] ids,int intervalMs){
            if(ids==null||ids.Length==0||ids.Length>65536)throw new ArgumentException("invalid_item_ids");
            foreach(int value in ids)if(value<0||value>65535)throw new ArgumentException("invalid_item_id");
            ConfigureAutoItem(id,enabled,ids[0],intervalMs);NativeJob j=Get(id);j.AutoItemIds=(int[])ids.Clone();j.AutoItemCursor=0;
        }
        public static void ConfigureAutoItem(string id,bool enabled,int templateId,int intervalMs){
            if(templateId<0||templateId>65535||intervalMs<1000||intervalMs>3600000)throw new Exception("auto_item_invalid");
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested)throw new Exception("auto_item_session_unavailable");
            if(j.CloakQueueOnly)enabled=false;j.AutoItemEnabled=false;j.AutoItemIds=new[]{templateId};j.AutoItemCursor=0;j.AutoItemId=templateId;j.AutoItemIntervalMs=intervalMs;j.AutoItemNext=DateTime.MinValue;j.AutoItemEnabled=enabled;j.AutoItemStatus=enabled?"Đợi game sẵn sàng":"Tắt";
        }
        // Nhường popup cho tìm đường/menu nhiệm vụ và chuẩn bị tổ đội.
        internal static bool AuxiliaryActionBlocked(NativeJob j){
            return j.CloakEnabled||j.ReceiveFlowActive||j.TravelActive||j.KeyReceiptPending||j.ReplayRequested||
                j.IzanamiEnabled&&!j.IzanamiRoundEntered&&!j.IzanamiRoundComplete||
                j.QuestEnabled&&(j.QuestPauseAuto||j.QuestPhase!=6);
        }
        internal static void PumpAutoItem(Remote r,long data,NativeJob j){
            if(j.ChestEnabled||j.ChestUseOwned)return;
            bool wanted=j.AutoItemEnabled&&!j.CloseRequested&&!j.CancelRequested;
            bool ready=wanted&&!j.MarketEnabled&&j.GameSettingsReady&&!j.QuestPauseAuto&&!AuxiliaryActionBlocked(j)&&!j.ZoneSyncActive&&!j.GiftWaiting&&!j.GiftResultWaiting&&!j.MailQueueOnly&&!j.CloakQueueOnly&&!j.MailEnabled&&!j.MailBusy;
            r.Write(data+12,BitConverter.GetBytes(ready?1:0));
            if(BitConverter.ToInt32(r.Read(data,4),0)!=0)return;
            int requests=BitConverter.ToInt32(r.Read(data+20,4),0),result=BitConverter.ToInt32(r.Read(data+8,4),0);
            if(requests!=j.AutoItemRequests){j.AutoItemRequests=requests;Log(j,"item_use_requested",",\"template_id\":"+BitConverter.ToInt32(r.Read(data+4,4),0)+",\"slot\":"+BitConverter.ToInt32(r.Read(data+16,4),0));}
            j.AutoItemStatus=!wanted?"Tắt":!ready?"Đợi game / thao tác NPC hoàn tất":result==2?"Đã gửi sử dụng; đợi lượt tiếp theo":result==3?"Không có ID này trong túi; đợi lượt tiếp theo":result==4?"Ô túi đã thay đổi; đợi đọc lại":"Đợi game sẵn sàng";
            if(!ready||DateTime.UtcNow<j.AutoItemNext)return;
            int next=NextAutoItem(j);if(next<0){j.AutoItemStatus="Đã dùng hết danh sách một lần; đợi phiên game mới";return;}j.AutoItemId=next;r.Write(data+4,BitConverter.GetBytes(j.AutoItemId));r.Write(data+24,BitConverter.GetBytes(1));r.Write(data+8,new byte[4]);r.Write(data,BitConverter.GetBytes(1));j.AutoItemNext=DateTime.UtcNow.AddMilliseconds(j.AutoItemIntervalMs);
        }
        static long ResolvedClass(Remote r,long slot){long p=r.Ptr(slot);if(p==0||(p&1)!=0)throw new Exception("inventory_not_ready");return p;}
        static byte[] InventoryArray(Remote r,long p,int max,out int count){
            if(p==0)throw new Exception("inventory_not_ready");long n=r.Ptr(p+24);if(n<0||n>max)throw new Exception("inventory_invalid_array");count=(int)n;return r.Read(p+32,count*8);
        }
        // Read only: no item-use calls, packets or UI changes. Verify the bag and
        // relevant item/template fields again before publishing a complete view.
        // gb.fvi (+0x61) là cờ khóa: iq.cov hiển thị (khóa) khi khác 0,
        // (k.khóa) khi bằng 0. Chỉ so các trường dùng cho snapshot, tránh
        // báo túi thay đổi do thuộc tính phụ không liên quan ở giữa cấu trúc.
        internal static bool SameInventoryFields(byte[] before,byte[] after){
            if(before.Length!=78||after.Length!=78||before[77]!=after[77])return false;
            for(int i=0;i<16;i++)if(before[i]!=after[i])return false;for(int i=28;i<36;i++)if(before[i]!=after[i])return false;return true;
        }
        internal static InventorySnapshot ReadInventorySnapshot(Remote r,long game){
            long canvasClass=ResolvedClass(r,game+0x16ed658),playerClass=ResolvedClass(r,game+0x16ee330),itemClass=ResolvedClass(r,game+0x16ee578);
            long cs=r.Ptr(canvasClass+0xb8),ps=r.Ptr(playerClass+0xb8);if(cs==0||ps==0)throw new Exception("inventory_not_ready");
            long canvas=r.Ptr(cs),player=r.Ptr(ps+8);if(canvas==0||player==0)throw new Exception("inventory_not_ready");
            long bag=r.Ptr(player+0x128),templates=r.Ptr(canvas+0x118),types=r.Ptr(canvas+0x158);
            int capacity,templateCount,typeCount;byte[] slots=InventoryArray(r,bag,512,out capacity),catalog=InventoryArray(r,templates,65536,out templateCount),typeSlots=InventoryArray(r,types,128,out typeCount);
            HashSet<int> equipmentTypes=new HashSet<int>();for(int i=0;i<typeCount;i++){long t=BitConverter.ToInt64(typeSlots,i*8);if(t!=0)equipmentTypes.Add(BitConverter.ToInt32(r.Read(t+16,4),0));}
            List<InventoryItem> items=new List<InventoryItem>();List<long> addresses=new List<long>();List<byte[]> values=new List<byte[]>();
            for(int i=0;i<capacity;i++){
                long item=BitConverter.ToInt64(slots,i*8);if(item==0)continue;
                if(r.Ptr(item)!=itemClass)throw new Exception("inventory_changed");
                byte[] fields=r.Read(item+20,78);int id=BitConverter.ToInt32(fields,0);if(id<0||id>=templateCount)throw new Exception("inventory_template_missing");
                long template=BitConverter.ToInt64(catalog,id*8);if(template==0)throw new Exception("inventory_template_missing");
                byte[] description=r.Read(template+24,20);long label=BitConverter.ToInt64(description,0);string name="";
                if(label!=0){int n=BitConverter.ToInt32(r.Read(label+16,4),0);if(n<0||n>512)throw new Exception("inventory_invalid_name");name=Encoding.Unicode.GetString(r.Read(label+20,n*2));}
                int type=unchecked((sbyte)description[18]);
                items.Add(new InventoryItem{BagIndex=i,Slot=BitConverter.ToInt32(fields,12),TemplateId=id,Quantity=BitConverter.ToInt32(fields,4),Locked=fields[77]!=0,ExpiryUtcMs=BitConverter.ToInt64(fields,28),Type=type,Name=name.Length==0?"Vật phẩm #"+id:name,Equipment=equipmentTypes.Contains(type)||type==17||type==18||(type>=40&&type<=43)});
                addresses.Add(item+20);values.Add(fields);addresses.Add(template+24);values.Add(description);
            }
            for(int i=0;i<addresses.Count;i++)if(!(values[i].Length==78?SameInventoryFields(values[i],r.Read(addresses[i],78)):SameBytes(values[i],r.Read(addresses[i],values[i].Length))))throw new Exception("inventory_changed");
            if(r.Ptr(cs)!=canvas||r.Ptr(ps+8)!=player||r.Ptr(player+0x128)!=bag||r.Ptr(canvas+0x118)!=templates||r.Ptr(canvas+0x158)!=types||r.Ptr(bag+24)!=capacity||r.Ptr(templates+24)!=templateCount||r.Ptr(types+24)!=typeCount||!SameBytes(slots,r.Read(bag+32,slots.Length))||!SameBytes(catalog,r.Read(templates+32,catalog.Length))||!SameBytes(typeSlots,r.Read(types+32,typeSlots.Length)))throw new Exception("inventory_changed");
            return new InventorySnapshot(items.ToArray(),capacity,true,"",DateTime.UtcNow);
        }
        internal static void PumpInventory(Remote r,long game,NativeJob j){
            if(Interlocked.Exchange(ref j.InventoryRequested,0)==0)return;
            try{
                Exception last=null;for(int i=0;i<3;i++){try{j.Inventory=ReadInventorySnapshot(r,game);return;}catch(Exception e){last=e;}}
                InventorySnapshot old=j.Inventory;j.Inventory=new InventorySnapshot(old.Items,old.Capacity,false,last.Message,old.ReadAt);
            }finally{j.InventoryScanning=Volatile.Read(ref j.InventoryRequested)!=0;}
        }
        public static string LogDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");
        internal static bool IsRaikage(string name,int template){name=(name??"").Normalize(NormalizationForm.FormC).Trim();return String.Equals(name,"Raikage Đệ Tam",StringComparison.OrdinalIgnoreCase)||(template==59&&String.Equals(name,"Raikage",StringComparison.OrdinalIgnoreCase));}
        static string Quote(string value){StringBuilder b=new StringBuilder("\"");foreach(char c in value??""){if(c=='\"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}return b.Append('"').ToString();}
        internal static void Log(NativeJob j,string evt,string fields){try{if(j.LogFile==null)j.LogFile=Path.Combine(LogDirectory,j.Id+".jsonl");Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));File.AppendAllText(j.LogFile,"{\"time\":"+Quote(DateTime.UtcNow.ToString("o"))+",\"event\":"+Quote(evt)+fields+"}\n",new UTF8Encoding(false));}catch{j.LogError="log_write_failed";}}
        static string ReadLabel(Remote r,long p){try{if(p==0)return "";int length=BitConverter.ToInt32(r.Read(p+16,4),0);if(length<0||length>256)return "";return Encoding.Unicode.GetString(r.Read(p+20,length*2));}catch{return "";}}
        internal static void ReadDiagnostics(Remote r,long data,NativeJob j){
            byte[] captured=r.Read(data+40,24);int gen=BitConverter.ToInt32(captured,0);
            if(gen!=0&&(gen&1)==0&&gen!=j.CaptureGeneration&&BitConverter.ToInt32(r.Read(data+40,4),0)==gen){
                string name=ReadLabel(r,BitConverter.ToInt64(captured,16));j.CapturedNpcId=BitConverter.ToInt32(captured,4);j.CapturedTemplateId=BitConverter.ToInt32(captured,8);j.CapturedNpcName=name;j.CaptureGeneration=gen;
                Log(j,"npc_interact",",\"npc_id\":"+j.CapturedNpcId+",\"template_id\":"+j.CapturedTemplateId+",\"name\":"+Quote(name));
            }
            byte[] menu=r.Read(data+2176,24);int mg=BitConverter.ToInt32(menu,0),count=BitConverter.ToInt32(menu,4);
            if(mg==0||(mg&1)!=0||count<0||count>128)return;
            byte[] entries=r.Read(data+2240,count*8);if(BitConverter.ToInt32(r.Read(data+2176,4),0)!=mg)return;
            string[] options=new string[count];List<string> quoted=new List<string>();for(int i=0;i<count;i++){options[i]=ReadLabel(r,BitConverter.ToInt64(entries,i*8));quoted.Add(Quote(options[i]));}
            int tag=BitConverter.ToInt32(menu,8),selected=BitConverter.ToInt32(menu,12);
            string fields=",\"tag\":"+tag+",\"options\":["+String.Join(",",quoted.ToArray())+"]";
            j.MenuTag=tag;j.MenuSelectedIndex=selected;j.MenuOptions=options;j.DynamicMenuJson="{"+fields.Substring(1)+",\"selected_index\":"+selected+"}";
            if(fields!=j.LastMenu){j.LastMenu=fields;Log(j,"dynamic_menu",fields+",\"npc_id\":"+j.CapturedNpcId+",\"selected_index\":"+selected);}
        }

        public static void SetGameAuto(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||j.CloseRequested||j.State=="failed"||j.State=="closed")throw new Exception("settings_session_unavailable");
            if(enabled&&j.DailyActive)return;
            if(j.IzanamiMapAutoActive&&j.IzanamiEnabled&&!enabled)return;
            if(j.SonCapEnabled){if(enabled)return;SetSonCap(id,false);}
            if(!enabled&&!j.PersistentUp)j.UpRequested=false;if(!enabled&&j.DailyManaged&&!j.PersistentUp)CancelDailyPriority(j);if(!enabled&&j.FarmEnabled)StopFarm(j);if(enabled)j.TrainEnabled=false;j.GameAutoManaged=true;j.GameAutoWanted=enabled;j.GameAutoStatus=enabled?"Đang yêu cầu bật treo":"Đang yêu cầu tắt treo";Log(j,"game_auto_wanted",",\"enabled\":"+(enabled?"true":"false"));
        }
        public static void SetGameOption(string id,int code,int value){
            NativeJob j=Get(id);if(j==null||!j.GameSettingsReady||j.CloseRequested)throw new Exception("settings_session_unavailable");
            GameOption found=null;foreach(GameOption o in j.GameOptions)if(o.Code==code)found=o;
            if(found==null||value<0||value>=(found.Kind==0?2:found.Choices.Length))throw new Exception("settings_value_invalid");
            if(j.SettingsQueue.Count>=128)throw new Exception("settings_queue_full");GameOption command=found.Copy();command.Value=value;j.SettingsQueue.Enqueue(command);
        }
        static long[] ReadArray(Remote r,long p){if(p==0)return new long[0];int n=BitConverter.ToInt32(r.Read(p+24,4),0);if(n<0||n>128)throw new Exception("settings_array_invalid");byte[] b=r.Read(p+32,n*8);long[] a=new long[n];for(int i=0;i<n;i++)a[i]=BitConverter.ToInt64(b,i*8);return a;}
        static void AddCheck(Remote r,List<GameOption> list,long root,long p,int tab){
            if(p==0)return;long owner=r.Ptr(p+0x48),container=r.Ptr(root+0xe0);if(owner!=root&&(container==0||owner!=container))return;int code=BitConverter.ToInt32(r.Read(p+0x80,4),0);if(code<201||code>4000)return;
            foreach(GameOption o in list)if(o.Code==code)return;
            string label=ReadLabel(r,r.Ptr(p+0x78));if(label.Length==0)return;
            list.Add(new GameOption{Tab=tab,Code=code,Kind=0,Label=label,Value=r.Read(p+0x84,1)[0]!=0?1:0,Handle=p});
        }
        static void AddDrop(Remote r,List<GameOption> list,long root,long p,int tab,string label){
            if(p==0)return;long owner=r.Ptr(p+0x48),container=r.Ptr(root+0xe0);if(owner!=root&&(container==0||owner!=container))return;long model=r.Ptr(p+0x88);if(model==0)return;
            int code=BitConverter.ToInt32(r.Read(model+0x10,4),0);if(code<200||code>1006)return;
            long[] strings=ReadArray(r,r.Ptr(model+24));string[] choices=new string[strings.Length];for(int i=0;i<strings.Length;i++)choices[i]=ReadLabel(r,strings[i]);if(choices.Length==0)return;
            list.Add(new GameOption{Tab=tab,Code=code,Kind=1,Label=label,Value=BitConverter.ToInt32(r.Read(model+20,4),0),Choices=choices,Handle=p});
        }
        internal static void ReadGameSettings(Remote r,long data,NativeJob j){
            j.GameSettingsReady=BitConverter.ToInt32(r.Read(data+4,4),0)==1;if(!j.GameSettingsReady)return;
            long root=r.Ptr(data+8);if(root==0)return;j.GameAutoEnabled=BitConverter.ToInt32(r.Read(data+16,4),0)!=0;
            if((DateTime.UtcNow-j.SettingsReadAt).TotalMilliseconds<750)return;j.SettingsReadAt=DateTime.UtcNow;
            List<GameOption> options=new List<GameOption>();long[] tabs=ReadArray(r,r.Ptr(root+0xa8));
            for(int t=0;t<Math.Min(4,tabs.Length);t++)foreach(long p in ReadArray(r,tabs[t]))AddCheck(r,options,root,p,t);
            string[] autoLabels={"Phạm vi đánh","Bơm HP (%)","Bơm MP (%)","Bình HP","Bình MP","Tự ghép đá","Tự dọn rác"};
            long[] drops=ReadArray(r,r.Ptr(root+0xb0));for(int i=0;i<drops.Length;i++)AddDrop(r,options,root,drops[i],0,i<autoLabels.Length?autoLabels[i]:"Tự động "+(i+1));
            drops=ReadArray(r,r.Ptr(root+0xb8));for(int i=0;i<drops.Length;i++)AddDrop(r,options,root,drops[i],1,"Loại đá nhặt");
            drops=ReadArray(r,r.Ptr(root+0xc0));for(int i=0;i<drops.Length;i++)AddDrop(r,options,root,drops[i],2,"Chế độ treo máy");
            for(int i=0;i<5;i++)AddCheck(r,options,root,r.Ptr(root+0x100+i*8),2);
            long keyboard=r.Ptr(root+0x140),movement=r.Ptr(root+0x148);
            if(keyboard!=0)options.Add(new GameOption{Tab=2,Code=1000,Kind=2,Handle=keyboard,Label="Bàn phím",Choices=new string[]{"Bàn phím ảo","Bàn phím hệ thống","Gõ phím trực tiếp"},Value=BitConverter.ToInt32(r.Read(keyboard+20,4),0)});
            if(movement!=0)options.Add(new GameOption{Tab=2,Code=1005,Kind=2,Handle=movement,Label="Di chuyển",Choices=new string[]{"Di chuyển cố định","Di chuyển tùy biến"},Value=BitConverter.ToInt32(r.Read(movement+20,4),0)});
            j.GameOptions=options.ToArray();
        }
        internal static void PrepareEntryAuto(NativeJob j,int kind,int origin){
            j.EntryAutoWaiting=false;j.EntryAutoOrigin=origin;j.EntryAutoKind=kind;
            if(kind==2){j.IzanamiRoundEntered=false;j.IzanamiRoundComplete=false;j.IzanamiReturnAt=DateTime.MinValue;}
        }
        internal static void SubmitEntryAuto(NativeJob j){
            j.EntryAutoWaiting=j.EntryAutoOrigin>=0;j.EntryAutoAt=DateTime.UtcNow;
        }
        internal static void ObserveEntryAuto(NativeJob j,int map,DateTime now){
            ObserveIzanamiMapAuto(j,map,now);ObserveIzanamiReturn(j,map,now);
            if(j.IzanamiEnabled&&!j.SonCapEnabled&&map==89)return;
            if(!j.EntryAutoWaiting)return;
            if(j.CloseRequested||j.CancelRequested||j.EntryAutoKind==2&&!j.IzanamiEnabled||j.EntryAutoKind==1&&!j.DungeonHighEnabled||(now-j.EntryAutoAt).TotalSeconds>60){j.EntryAutoWaiting=false;return;}
            if(map<0||map==j.EntryAutoOrigin)return;
            if(j.EntryAutoKind==3&&!SonCapMaps.Contains(map))return;
            j.EntryAutoWaiting=false;j.EntryCombatMap=map;j.GameAutoManaged=true;j.GameAutoWanted=true;j.EntryAutoActivations++;
            if(j.EntryAutoKind==2){j.IzanamiRoundEntered=true;j.IzanamiRoundComplete=false;j.IzanamiReturnAt=DateTime.MinValue;}
            j.GameAutoStatus="Đã vào "+(j.EntryAutoKind==2?"cấm thuật":"địa cung")+"; đang bật treo";
            Log(j,"entry_auto_enabled",",\"kind\":"+j.EntryAutoKind+",\"origin_map\":"+j.EntryAutoOrigin+",\"map\":"+map);
        }
        public static int IzanamiRoundLimit=1;
        public static void PulseIzanamiRounds(){PulseIzanamiRounds(DateTime.UtcNow);}
        internal static void PulseIzanamiRounds(DateTime now){
            foreach(NativeJob leader in jobs.Values){
                if(leader.PersistentUp&&!UpTeamReady(leader,false))continue;
                if(!leader.IzanamiEnabled||leader.SonCapEnabled||!IsIzanamiLeader(leader)||!leader.IzanamiRoundComplete||leader.IzanamiRunFinished||leader.CloseRequested||leader.CancelRequested)continue;
                NativeJob[] peers=leader.TeamJobIds.Select(id=>Get(id??"")).ToArray();
                if(peers.Any(p=>p==null||!p.IzanamiEnabled||p.SonCapEnabled||p.CloseRequested||p.CancelRequested||p.ReconnectRequested||!p.IzanamiRoundComplete||p.IzanamiNoAttempts||(now-p.TeamReadAt).TotalSeconds>=4)){
                    leader.TeamStatus="Đợi thành viên kết thúc lượt hiện tại trước khi vào lượt tiếp";continue;
                }
                foreach(NativeJob p in peers){
                    p.IzanamiRoundsTarget=leader.IzanamiRoundsTarget;p.IzanamiRoundComplete=false;p.IzanamiRoundEntered=false;p.IzanamiReturnAt=DateTime.MinValue;p.IzanamiLimitArmed=false;p.EntryAutoWaiting=false;p.IzanamiMapAutoActive=false;p.EntryCombatMap=-1;p.GameAutoWanted=false;p.TrainEnabled=false;
                    p.ZonePrepared=false;p.ZonePlanMap=-1;p.PlannedZone=-1;p.ZoneAttempts=0;p.ZoneListWaiting=false;p.ZoneActionAt=DateTime.MinValue;p.ZonePollAt=DateTime.MinValue;p.ZoneSyncActive=false;
                    p.LeaderZoneJob="";p.LeaderZoneMap=-1;p.LeaderZoneTarget=-1;p.LeaderZoneConfirmed=false;
                    p.TeamConfirmed=false;p.TeamReadAt=DateTime.MinValue;p.TeamAttempts=0;p.TeamAttemptCommand=0;p.TeamTarget="";p.TeamFriendRequested=false;p.TeamAcceptedPopup=0;
                    p.IzanamiRequested=p==leader;p.IzanamiTravelReady=p.MapId==59;p.TravelReady=p.MapId==59;p.IzanamiTravelStopped=false;p.State="opened";p.TeamStatus="Kiểm tra lại nhóm trước lượt "+(p.IzanamiRoundsCompleted+1)+"/"+p.IzanamiRoundsTarget;
                    Log(p,"izanami_round_recheck",",\"completed\":"+p.IzanamiRoundsCompleted+",\"target\":"+p.IzanamiRoundsTarget);
                }
            }
        }
        internal static void ObserveIzanamiReturn(NativeJob j,int map,DateTime now){
            if(j.SonCapEnabled||j.EntryAutoKind!=2||!j.IzanamiRoundEntered||j.IzanamiRoundComplete||j.CloseRequested||j.CancelRequested)return;
            if(map<0||map!=j.EntryAutoOrigin){j.IzanamiReturnAt=DateTime.MinValue;return;}
            if(j.IzanamiReturnAt==DateTime.MinValue){j.IzanamiReturnAt=now;return;}
            if((now-j.IzanamiReturnAt).TotalSeconds<3)return;
            j.IzanamiRoundComplete=true;j.IzanamiRoundsCompleted++;j.GameAutoWanted=false;j.GameAutoStatus="Đã kết thúc lượt cấm thuật; trở về bản đồ ngoài";
            Log(j,"izanami_round_returned",",\"map\":"+map);
        }
        public static volatile bool SonCapCloseAfterEnabled;
        internal static bool IsSonCapAttemptLimit(string text){
            if(!System.Text.RegularExpressions.Regex.IsMatch(text??"",@"sơn\s+cáp",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return false;
            return IsIzanamiAttemptLimit(System.Text.RegularExpressions.Regex.Replace(text,@"sơn\s+cáp","cấm thuật",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        public static void PulseSonCapLimitClose(){PulseSonCapLimitClose(null);}
        internal static void PulseSonCapLimitClose(Action<NativeJob> close){foreach(NativeJob j in jobs.Values){if(!ShouldCloseSonCapLimit(j))continue;Log(j,"soncap_limit_close_requested","");if(close!=null){j.SonCapLimitCloseOperation=new CloseBatchOperation{Completed=true};close(j);}else j.SonCapLimitCloseOperation=BeginCloseBatch(new string[]{j.Id});}}
        internal static bool ShouldCloseSonCapLimit(NativeJob j){return !KeepGameForUp(j)&&j.SonCapNoAttempts&&!j.SonCapQueueOnly&&j.ProcessId>0&&!j.CloseRequested&&!j.CancelRequested&&j.SonCapLimitCloseOperation==null;}
        // Ra khỏi hang đủ 3 giây mới lập lại lượt vào, tránh nhầm khi đang tải map.
        internal static bool ObserveSonCapReturn(NativeJob j,int map,DateTime now){
            if(!j.SonCapEnabled||j.SonCapFinished||j.SonCapStage<0||j.CloseRequested||j.CancelRequested||map<0)return false;
            if(Array.IndexOf(SonCapMaps,map)>=0){j.SonCapReturnAt=DateTime.MinValue;return false;}
            j.GameAutoWanted=false;j.SonCapMapAutoActive=false;
            if(j.SonCapReturnAt==DateTime.MinValue)j.SonCapReturnAt=now;
            if((now-j.SonCapReturnAt).TotalSeconds<3){j.SonCapStatus="Sơn Cáp: xác nhận đã ra khỏi hang";return true;}
            j.SonCapRegroupAfterReturn=true;j.TeamAcceptedPopup=0;j.TeamFriendRequested=false;j.TeamActionAt=DateTime.MinValue;j.SonCapStage=-1;j.SonCapAlive=-1;j.SonCapClearedStage=-1;j.SonCapMoving=false;j.SonCapStopPending=true;j.SonCapEntrySubmitted=false;j.SonCapFailure="";j.SonCapEmptyAt=DateTime.MinValue;
            j.IzanamiRoundEntered=false;j.IzanamiRoundComplete=false;j.IzanamiLimitArmed=false;j.IzanamiNoAttempts=false;j.EntryCombatMap=-1;j.EntryAutoWaiting=false;j.ReceiveFlowActive=false;
            j.IzanamiTravelReady=map==59;j.TravelReady=map==59;j.IzanamiTravelStopped=false;j.IzanamiRequested=IsIzanamiLeader(j);j.TeamConfirmed=false;j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamTarget="";j.State="opened";j.Error="";
            j.SonCapStatus="Sơn Cáp: hết giờ/rời hang, chuẩn bị vào lại";Log(j,"soncap_return_retry",",\"map\":"+map);return true;
        }
        internal static bool IsIzanamiAttemptLimit(string text){
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD)){if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark)continue;b.Append(c=='đ'?'d':Char.IsLetterOrDigit(c)?c:' ');}string n=System.Text.RegularExpressions.Regex.Replace(b.ToString(),@"\s+"," ");return n.Contains("khong du luot tham gia cam thuat")||n.Contains("het luot tham gia cam thuat");
        }
        internal static bool ObserveIzanamiAttemptLimit(NativeJob leader,string text){
            if(!leader.IzanamiEnabled||!leader.IzanamiLimitArmed||leader.IzanamiRoundEntered||leader.IzanamiNoAttempts||!(leader.SonCapEnabled?IsSonCapAttemptLimit(text):IsIzanamiAttemptLimit(text)))return false;
            leader.IzanamiLimitArmed=false;string[] ids=leader.TeamJobIds.Length>0?leader.TeamJobIds:new string[]{leader.Id};
            foreach(string id in ids){NativeJob peer=Get(id);if(peer==null||!peer.IzanamiEnabled||peer.IzanamiRoundEntered)continue;
                if(leader.SonCapEnabled){if(!peer.SonCapEnabled)continue;peer.SonCapNoAttempts=true;peer.SonCapFinished=true;peer.SonCapMoving=false;peer.SonCapMapAutoActive=false;peer.SonCapFailure="";peer.SonCapStatus="Hết lượt tham gia Sơn Cáp";Log(peer,"soncap_attempt_limit",",\"message\":"+Quote(text));}
                peer.IzanamiNoAttempts=true;peer.IzanamiLimitNotice=text;peer.IzanamiRequested=false;peer.EntryAutoWaiting=false;peer.GameAutoWanted=false;peer.TrainEnabled=false;peer.State="izanami_no_attempts";
                Log(peer,"izanami_attempt_limit",",\"message\":"+Quote(text)+",\"leader_job\":"+Quote(leader.Id));
            }return true;
        }
        // Chỉ tắt khi người dùng bật tùy chọn; chu trình hằng ngày giữ game để chuyển bước.
        public static volatile bool QuestCloseAfterEnabled;
        internal static bool ShouldCloseFinishedQuest(NativeJob j){
            return !j.BeastQuestEnabled&&QuestCloseAfterEnabled&&j.QuestFinished&&!j.QuestEnabled&&!j.QuestQueueOnly&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&!KeepGameForUp(j)&&j.ProcessId>0&&j.State!="closed"&&j.State!="failed"&&j.State!="cancelled"&&j.QuestAutoCloseOperation==null;
        }
        public static void PulseQuestAutoClose(){PulseQuestAutoClose(null);}
        internal static void PulseQuestAutoClose(Action<NativeJob> close){
            foreach(NativeJob j in jobs.Values){if(!ShouldCloseFinishedQuest(j))continue;
                Log(j,"quest_auto_close_requested",",\"remaining\":"+j.QuestRemaining);
                if(close!=null){j.QuestAutoCloseOperation=new CloseBatchOperation{Completed=true};close(j);}
                else j.QuestAutoCloseOperation=BeginAutoCloseBatch(new string[]{j.Id});
            }
        }
        public static volatile bool DungeonCloseAfterEnabled;
        public static void PulseDungeonAutoClose(){PulseDungeonAutoClose(null);}
        internal static bool ShouldCloseFinishedDungeon(NativeJob j){return DungeonCloseAfterEnabled&&j.DungeonFinished&&j.DungeonStopped&&!j.DungeonCycleWaiting&&j.Error=="dungeon_no_keys"&&!j.DungeonQueueOnly&&!j.CloseRequested&&!j.CancelRequested&&!KeepGameForUp(j)&&j.ProcessId>0&&j.DungeonAutoCloseOperation==null;}
        internal static void PulseDungeonAutoClose(Action<NativeJob> close){
            foreach(NativeJob j in jobs.Values){if(!ShouldCloseFinishedDungeon(j))continue;
                Log(j,"dungeon_auto_close_requested","");
                if(close!=null){j.DungeonAutoCloseOperation=new CloseBatchOperation{Completed=true};close(j);}
                else j.DungeonAutoCloseOperation=BeginAutoCloseBatch(new string[]{j.Id});
            }
        }
        public static volatile bool IzanamiCloseAfterEnabled;
        public static bool KeepGameForUp(NativeJob j){return j!=null&&(j.UpRequested||j.FarmEnabled||j.DailyManaged);}
        public static bool CanAutoCloseBatch(string[] ids){return !(ids??new string[0]).Any(id=>KeepGameForUp(Get(id)));}
        public static CloseBatchOperation BeginAutoCloseBatch(string[] ids){if(!CanAutoCloseBatch(ids))return new CloseBatchOperation{Completed=true,Error="up_keeps_game_open"};return BeginCloseBatch(ids);}
        internal static bool ShouldCloseIzanamiLimit(NativeJob j){return !KeepGameForUp(j)&&!j.IzanamiQueueOnly&&!j.SonCapEnabled&&!j.CloseRequested&&!j.CancelRequested;}
        static void PumpIzanamiAttemptLimit(Remote r,long game,NativeJob j){
            if(!j.IzanamiLimitArmed||j.IzanamiRoundEntered||j.CloseRequested||j.CancelRequested)return;
            DateTime now=DateTime.UtcNow;if((now-j.IzanamiNoticeAt).TotalSeconds>90){j.IzanamiLimitArmed=false;return;}
            if((now-j.IzanamiNoticePollAt).TotalSeconds<1)return;j.IzanamiNoticePollAt=now;
            string text=ReadKeyNotice(r,game);bool rejected=text!=j.IzanamiPopupBefore&&ObserveIzanamiAttemptLimit(j,text);
            if(!rejected)foreach(var entry in ReadKeyNotifications(r,game)){if(j.IzanamiNoticeSeen.Add(entry.Key)&&ObserveIzanamiAttemptLimit(j,entry.Value)){rejected=true;break;}}
            if(rejected&&!j.SonCapEnabled&&ShouldCloseIzanamiLimit(j))BeginCloseBatch(j.TeamJobIds.Length>0?j.TeamJobIds:new string[]{j.Id});
        }
        static void PrepareIzanamiAuto(NativeJob leader){
            foreach(string id in leader.TeamJobIds){NativeJob peer=Get(id);if(peer!=null&&peer.IzanamiEnabled&&!peer.CloseRequested&&!peer.CancelRequested)PrepareEntryAuto(peer,leader.SonCapEnabled?3:2,leader.MapId);}
        }
        static void SubmitIzanamiAuto(NativeJob leader){
            foreach(string id in leader.TeamJobIds){NativeJob peer=Get(id);if(peer!=null&&peer.IzanamiEnabled&&(peer.EntryAutoKind==2||peer.EntryAutoKind==3))SubmitEntryAuto(peer);}
        }
        public static bool UsesInstanceGameCombat(NativeJob j){
            return j.SonCapEnabled||j.FarmEnabled&&!j.QuestEnabled||j.GameAutoManaged&&(j.EntryAutoKind==2&&j.IzanamiEnabled&&j.IzanamiRoundEntered&&!j.IzanamiRoundComplete||j.EntryAutoKind==1&&j.DungeonHighEnabled&&j.DungeonCycleWaiting&&j.DungeonLeftOrigin);
        }
        internal static bool CanSubmitGameAuto(NativeJob j,bool desired,DateTime now){
            if(j.GameAutoEnabled==desired)return false;
            if(j.SettingsAutoValue==(desired?1:0)&&(now-j.SettingsAutoAt).TotalSeconds<(desired&&j.SonCapEnabled?3:1))return false;
            j.SettingsAutoAt=now;j.SettingsAutoValue=desired?1:0;return true;
        }
        public static bool DungeonEntryBlocked(NativeJob j){return j.DungeonHighEnabled&&(j.DungeonStopped||j.DungeonCycleWaiting&&!j.DungeonLeftOrigin);}
        internal static bool GameAutoDesired(NativeJob j){
            if(j.CloakEnabled||j.QuestEnabled||j.ThiQuyCombatActive&&ThiQuyPriorityAllowed(j))return false;
            if(j.SonCapEnabled)return j.SonCapMapAutoActive&&j.GameAutoWanted&&!j.SonCapMoving&&!j.SonCapStopPending&&!j.SonCapFinished&&j.SonCapFailure.Length==0&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&CurseDisplayStatus(j).Length==0;
            if(j.IzanamiMapAutoActive&&j.IzanamiEnabled&&!j.SonCapEnabled)return !j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested;
            // Follow/zone synchronization and queued NPC requests must not silently
            // override the Auto checkbox. Only an active login/NPC action pauses idle.
            return (j.SonCapEnabled?j.GameAutoWanted&&!j.SonCapMoving&&!j.SonCapFinished&&j.SonCapFailure.Length==0:j.FarmEnabled?j.FarmArrived:j.GameAutoWanted)&&!DungeonEntryBlocked(j)&&!j.QuestPauseAuto&&!j.ReceiveFlowActive&&j.State!="waiting_popup"&&j.State!="waiting_map"&&j.State!="confirming_popup";
        }
        static void PumpGameSettings(Remote r,long data,NativeJob j){
            if(data==0)return;
            // State 3 is held until after the game calls finish; the worker cannot overwrite it.
            if(BitConverter.ToInt32(r.Read(data,4),0)!=0)return;
            if(j.SettingsInFlight){int status=BitConverter.ToInt32(r.Read(data+48,4),0);j.SettingsError=status==-2?"game_auto_not_confirmed":status<0?"settings_apply_rejected":"";j.SettingsInFlight=false;}
            try{ReadGameSettings(r,data,j);}catch{j.GameSettingsReady=false;j.SettingsError="settings_read_failed";return;}if(!j.GameSettingsReady){j.GameAutoStatus="Chờ game sẵn sàng để bật/tắt treo";return;}
            bool desired=GameAutoDesired(j);
            j.GameAutoStatus=j.ThiQuyCombatActive&&ThiQuyPriorityAllowed(j)?(j.GameAutoEnabled?"Tắt treo để tập trung Thi Quỷ":"Đang tập trung Thi Quỷ; treo tạm tắt"):!j.GameAutoWanted?(j.GameAutoEnabled?"Đang tắt treo":"Treo đã tắt"):!desired?"Tạm dừng treo: đang đăng nhập/thao tác NPC":j.GameAutoEnabled?"Game: treo đang bật":"Đang bật treo; chờ game xác nhận";
            if(j.GameAutoManaged){
                string diagnostic=j.GameAutoStatus+"|"+j.State+"|"+j.SettingsError;
                if(diagnostic!=j.LastAutoDiagnostic&&(DateTime.UtcNow-j.AutoDiagnosticAt).TotalSeconds>=5){j.LastAutoDiagnostic=diagnostic;j.AutoDiagnosticAt=DateTime.UtcNow;
                    Log(j,"game_auto_observed",",\"wanted\":"+(j.GameAutoWanted?"true":"false")+",\"desired\":"+(desired?"true":"false")+",\"enabled\":"+(j.GameAutoEnabled?"true":"false")+",\"state\":"+Quote(j.State)+",\"npc_active\":"+(j.ReceiveFlowActive?"true":"false")+",\"error\":"+Quote(j.SettingsError));}
            }
            if(j.GameAutoManaged&&j.GameAutoEnabled!=desired){if(!CanSubmitGameAuto(j,desired,DateTime.UtcNow))return;r.Write(data+32,BitConverter.GetBytes(desired?1:0));r.Write(data,BitConverter.GetBytes(1));j.SettingsInFlight=true;return;}
            if(j.GameAutoManaged)j.SettingsAutoSent=desired?1:0;
            GameOption command;if(j.SettingsQueue.TryDequeue(out command)){
                r.Write(data+24,BitConverter.GetBytes(command.Code));r.Write(data+28,BitConverter.GetBytes(command.Kind));r.Write(data+32,BitConverter.GetBytes(command.Value));r.Write(data+40,BitConverter.GetBytes(command.Handle));r.Write(data,BitConverter.GetBytes(2));j.SettingsInFlight=true;j.SettingsReadAt=DateTime.MinValue;
            }
        }

        // Phi Lôi Thần Thuật luôn đứng đầu bộ train nếu nhân vật đã học.
        public static int[] DefaultTrainingSkills(SkillInfo[] skills,int[] selected){
            List<int> result=new List<int>();
            foreach(SkillInfo skill in skills??new SkillInfo[0])if(skill.Id==295&&skill.Usable){result.Add(295);break;}
            foreach(int id in selected??new int[0])if(!result.Contains(id)&&result.Count<4)result.Add(id);
            // Fresh/copied accounts can have an empty saved selection and a different
            // Phi level/ID. Resolve the learned templates instead of silently disabling combat.
            if(selected==null||selected.Length==0){
                foreach(int template in new[]{26,28}){
                    SkillInfo learned=(skills??new SkillInfo[0]).Where(x=>x.Usable&&x.TemplateId==template).OrderByDescending(x=>x.Level).FirstOrDefault();
                    if(learned!=null&&!result.Contains(learned.Id)&&result.Count<4)result.Add(learned.Id);
                }
                SkillInfo basic=(skills??new SkillInfo[0]).FirstOrDefault(x=>x.Usable&&x.Id==1&&x.Level==1);
                if(basic!=null&&!result.Contains(basic.Id)&&result.Count<4)result.Add(basic.Id);
            }
            return result.ToArray();
        }
        public static void ScanSkills(string id){NativeJob j=Get(id);if(j==null||!j.TrainReady||j.CloseRequested)throw new Exception("train_session_unavailable");j.SkillScanRequested=true;}
        public static void StartTraining(string id,int skillId){StartTraining(id,new int[]{skillId});}
        public static void StartTraining(string id,int[] skillIds){
            NativeJob j=Get(id);if(j==null||!j.TrainReady||j.CloseRequested||j.ReplayRequested)throw new Exception("train_session_unavailable");
            if(j.FarmEnabled)throw new Exception("Dừng Auto bãi quái trước khi bật train riêng.");
            if(skillIds==null||skillIds.Length<1||skillIds.Length>4)throw new Exception("train_select_1_to_4_skills");
            HashSet<int> unique=new HashSet<int>();foreach(int idValue in skillIds){bool found=false;foreach(SkillInfo skill in j.Skills)if(skill.Id==idValue&&skill.Usable)found=true;if(!found||!unique.Add(idValue))throw new Exception("train_skill_invalid");}
            skillIds=DefaultTrainingSkills(j.Skills,skillIds);j.TrainSkillIds=(int[])skillIds.Clone();j.TrainCursor=0;j.TrainSkillId=skillIds[0];j.TrainEnabled=true;Log(j,"train_start",",\"skill_ids\":["+String.Join(",",Array.ConvertAll(skillIds,delegate(int n){return n.ToString();}))+ "]");
        }
        public static void StopTraining(string id){NativeJob j=Get(id);if(j!=null){j.TrainEnabled=false;Log(j,"train_stop","");}}
        // iu là quái thường; 0x16f0198 là gd và không khớp quái trong map.
        internal const long TrainingMobSlotRva=0x16ee310;
        internal static SkillInfo HangingLevelOne(NativeJob j){
            if(j.ThiQuyCombatActive||j.CurseActive||j.FarmEnabled&&!j.FarmArrived||j.SonCapEnabled&&(!j.SonCapMapAutoActive||j.SonCapMoving||j.SonCapStopPending||j.SonCapFinished))return null;
            if(!j.GameAutoEnabled||!(j.GameAutoWanted||j.FarmEnabled&&j.FarmArrived||j.IzanamiMapAutoActive&&j.IzanamiEnabled)||!j.GameSettingsReady||j.ReceiveFlowActive||j.ReceiveKeyRequested||j.ReplayRequested||j.CloseRequested||j.CancelRequested||j.ReconnectRequested||j.QuestPauseAuto||j.CloakEnabled||j.MailEnabled||j.MailBusy||j.MailQueueOnly||j.CloakQueueOnly||DungeonEntryBlocked(j)||AuxiliaryActionBlocked(j)||j.ZoneSyncActive||j.FollowActive&&(j.FollowDistance<0||j.FollowDistance>48))return null;
            // Treo mặc định xử lý chiêu còn lại; chỉ bổ sung chiêu cấp 1 đã chọn.
            int[] ids=j.HangingSkillIds;var extra=new List<SkillInfo>();
            SkillInfo basic=j.Skills.Where(x=>x.Level==1&&x.Usable&&Array.IndexOf(ids,x.Id)>=0).OrderBy(x=>x.Id).FirstOrDefault();if(basic!=null)extra.Add(basic);
            return extra.Count==0?null:extra[j.HangingSkillCursor%extra.Count];
        }
        internal static int TrainingPollMs(NativeJob j){
            if(j.ThiQuyCombatActive)return 150;if(HangingLevelOne(j)!=null)return 10;if(j.QuestEnabled)return 150;
            if(j.TrainEnabled&&!j.ReceiveFlowActive&&!j.ReceiveKeyRequested&&!j.ReplayRequested){
                foreach(SkillInfo skill in j.Skills)if(skill.Level==1&&skill.Usable&&Array.IndexOf(j.TrainSkillIds,skill.Id)>=0)return 50;
            }
            return 150;
        }
        public static void SetThiQuyPriority(string id,bool enabled){NativeJob j=Get(id);if(j==null)throw new Exception("train_session_unavailable");j.PrioritizeThiQuy=enabled;j.ThiQuyPriorityAt=DateTime.MinValue;}
        internal static bool ThiQuyPriorityAllowed(NativeJob j){return j!=null&&j.PrioritizeThiQuy&&j.Auto&&UpSessionReady(j)&&(j.TrainEnabled||j.GameAutoWanted||j.FarmEnabled)&&!j.QuestEnabled&&!j.QuestPauseAuto&&!j.DailyActive&&!j.IzanamiEnabled&&!j.SonCapEnabled&&!j.DungeonHighEnabled&&!j.ReceiveFlowActive&&!j.ReceiveKeyRequested&&!j.ReplayRequested&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&!j.CurseActive&&!j.CloakEnabled&&!j.ChestEnabled&&!j.MailEnabled&&!j.MailBusy&&!j.MailQueueOnly&&!j.CloakQueueOnly&&!j.ZoneSyncActive&&!j.FollowActive;}
        internal static bool IsThiQuyName(string name){return String.Equals((name??"").Normalize(NormalizationForm.FormC).Trim(),"Thi Quỷ",StringComparison.OrdinalIgnoreCase);}
        internal static long ReadThiQuyTarget(Remote r,long game,out int map){string scan;return ReadThiQuyTarget(r,game,out map,out scan);}
        internal static long ReadThiQuyTarget(Remote r,long game,out int map,out string scan){
            scan="world_unavailable";
            long fcClass=ResolvedClass(r,game+0x16ee888),canvasClass=ResolvedClass(r,game+0x16ed658),charClass=ResolvedClass(r,game+0x16ee330),mobClass=ResolvedClass(r,game+TrainingMobSlotRva);
            long fc=r.Ptr(r.Ptr(fcClass+0xb8)+16),canvas=r.Ptr(r.Ptr(canvasClass+0xb8)),player=r.Ptr(r.Ptr(charClass+0xb8)+8);map=-1;
            if(fc==0||canvas==0||player==0||r.Ptr(fc)!=fcClass||r.Ptr(player)!=charClass||r.Ptr(canvas+0x1b8)!=fc)throw new Exception("thi_quy_world_not_ready");
            map=BitConverter.ToInt16(r.Read(fc+0x134,2),0);long catalog=r.Ptr(canvas+0xd8);if(catalog==0||BitConverter.ToInt32(r.Read(catalog+24,4),0)<=261)throw new Exception("thi_quy_catalog_not_ready");long template=r.Ptr(catalog+32+261*8);
            if(template==0||!IsThiQuyName(ReadLabel(r,r.Ptr(template+48))))throw new Exception("thi_quy_template_unverified");
            byte[] position=r.Read(player+16,4);int x=BitConverter.ToInt16(position,0),y=BitConverter.ToInt16(position,2),bestDistance=Int32.MaxValue;long best=0,current=r.Ptr(fc+0x2b0);
            long vector=r.Ptr(fc+0x150);if(vector==0)throw new Exception("thi_quy_vector_not_ready");long[] monsters=ReadVector(r,vector).Take(512).ToArray();int seen=0,alive=0;var details=new List<string>();
            foreach(long mob in monsters){
                if(mob==0||r.Ptr(mob)!=mobClass)continue;byte[] b=r.Read(mob+0x38,68);if(BitConverter.ToInt16(b,24)!=261)continue;seen++;int hp=BitConverter.ToInt32(b,56);if(details.Count<8)details.Add(BitConverter.ToInt32(b,0)+":"+hp+":"+b[5]);if(b[5]>=4||hp<=0)continue;alive++;
                if(mob==current){best=mob;bestDistance=-1;continue;}int distance=Math.Abs(x-BitConverter.ToInt16(b,32))+Math.Abs(y-BitConverter.ToInt16(b,34));if(distance<bestDistance){best=mob;bestDistance=distance;}
            }
            if(r.Ptr(canvas+0x1b8)!=fc||BitConverter.ToInt16(r.Read(fc+0x134,2),0)!=map)throw new Exception("thi_quy_map_changed");scan="map="+map+" total="+monsters.Length+" thi_seen="+seen+" thi_alive="+alive+" uid:hp:state="+String.Join(";",details.ToArray());return best;
        }
        internal static SkillInfo[] ThiQuySkills(NativeJob j){
            int[] ids=DefaultTrainingSkills(j.Skills,j.HangingSkillIds);
            return ids.Select(id=>j.Skills.FirstOrDefault(x=>x.Id==id&&x.Usable)).Where(x=>x!=null).ToArray();
        }
        internal static void ClearThiQuyFocus(Remote r,long data,NativeJob j,string reason="disabled"){
            if(j.ThiQuyCombatActive||j.ThiQuyPriorityArmed){Log(j,"thi_quy_focus_end",",\"pid\":"+j.ProcessId+",\"character\":"+Quote(j.CharacterName)+",\"reason\":"+Quote(reason));r.Write(data+12,new byte[4]);r.Write(data+124,new byte[4]);r.Write(data+112,new byte[8]);j.TrainIdleApplied=false;}
            j.ThiQuyCombatActive=false;j.ThiQuyPriorityArmed=false;j.ThiQuyNativeMode=0;j.ThiQuyReadSuspended=false;j.ThiQuyMissingAt=DateTime.MinValue;
        }
        internal static void ArmThiQuyFocus(Remote r,long data,NativeJob j){
            bool armed=j.ThiQuyCombatActive&&!j.GameAutoEnabled&&!j.ThiQuyReadSuspended;
            int mode=j.ThiQuyCombatActive?(armed?1:2):0;if(mode!=j.ThiQuyNativeMode){r.Write(data+124,BitConverter.GetBytes(mode));j.ThiQuyNativeMode=mode;}j.ThiQuyPriorityArmed=armed;
            if(j.ThiQuyCombatActive)j.ThiQuyPriorityStatus=j.ThiQuyReadSuspended?"Đợi xác nhận dữ liệu Thi Quỷ; giữ focus":j.GameAutoEnabled?"Đã gặp Thi Quỷ: đợi tắt treo":"Treo tạm tắt: tập trung đánh Thi Quỷ (ID 261)";
        }
        internal static void LogThiQuyScan(NativeJob j,string scan){
            DateTime now=DateTime.UtcNow;if(scan==j.ThiQuyScanLast&&(now-j.ThiQuyScanLogAt).TotalSeconds<5)return;
            j.ThiQuyScanLast=scan;j.ThiQuyScanLogAt=now;Log(j,"thi_quy_scan",",\"pid\":"+j.ProcessId+",\"character\":"+Quote(j.CharacterName)+",\"scan\":"+Quote(scan)+",\"focus\":"+(j.ThiQuyCombatActive?"true":"false"));
        }
        internal static void SuspendThiQuyFocus(Remote r,long data,NativeJob j){j.ThiQuyReadSuspended=true;r.Write(data+12,new byte[4]);ArmThiQuyFocus(r,data,j);}
        internal static void PumpThiQuyPriority(Remote r,long game,long data,NativeJob j){
            if(data==0)return;bool allowed=ThiQuyPriorityAllowed(j);DateTime now=DateTime.UtcNow;
            if(!allowed){ClearThiQuyFocus(r,data,j);j.ThiQuyPriorityStatus=j.PrioritizeThiQuy?"Chờ train quái sẵn sàng":"Tắt";return;}
            if((now-j.ThiQuyPriorityAt).TotalMilliseconds<(j.ThiQuyCombatActive?150:250)){ArmThiQuyFocus(r,data,j);return;}j.ThiQuyPriorityAt=now;
            try{
                int map;string scan;long target=ReadThiQuyTarget(r,game,out map,out scan);LogThiQuyScan(j,scan);
                if(target==0){
                    if(j.ThiQuyCombatActive){if(j.ThiQuyMissingAt==DateTime.MinValue)j.ThiQuyMissingAt=now;if((now-j.ThiQuyMissingAt).TotalMilliseconds<300){SuspendThiQuyFocus(r,data,j);return;}}
                    ClearThiQuyFocus(r,data,j,"confirmed_no_living_target");j.ThiQuyPriorityStatus="Chưa thấy Thi Quỷ sống trong dữ liệu map";return;
                }
                j.ThiQuyMissingAt=DateTime.MinValue;j.ThiQuyReadSuspended=false;
                if(!ThiQuySkills(j).Any(x=>x.Type==0)){ClearThiQuyFocus(r,data,j);j.SkillScanRequested=true;j.ThiQuyPriorityStatus="Chọn chiêu tấn công để đánh Thi Quỷ";return;}
                r.Write(data+112,BitConverter.GetBytes(target));r.Write(data+120,BitConverter.GetBytes(map));
                if(!j.ThiQuyCombatActive){Log(j,"thi_quy_focus_start",",\"pid\":"+j.ProcessId+",\"character\":"+Quote(j.CharacterName)+",\"map\":"+map+",\"farm_arrived\":"+(j.FarmArrived?"true":"false"));j.ThiQuySkillCursor=0;j.TrainIdleApplied=false;j.LevelOneNextAt=DateTime.MinValue;}
                j.ThiQuyCombatActive=true;j.GameAutoManaged=true;ArmThiQuyFocus(r,data,j);
            }catch(Exception e){LogThiQuyScan(j,"read_error:"+e.GetType().Name+":"+e.Message);if(j.ThiQuyCombatActive)SuspendThiQuyFocus(r,data,j);else j.ThiQuyPriorityStatus="Chờ dữ liệu quái mới";}
        }
        static void PumpTraining(Remote r,long data,NativeJob j){
            if(data==0)return;
            if(j.HangingAwaiting&&!j.GameAutoEnabled)r.Write(data+12,new byte[4]);
            bool focus=j.ThiQuyCombatActive&&ThiQuyPriorityAllowed(j);SkillInfo hanging=HangingLevelOne(j);bool overlay=hanging!=null;
            if(!focus&&!overlay&&!j.TrainEnabled&&!j.SkillScanRequested&&j.TrainIdleApplied)return;
            if(focus||j.TrainEnabled||j.SkillScanRequested)j.TrainIdleApplied=false;
            if(BitConverter.ToInt32(r.Read(data,4),0)!=0)return;
            j.TrainReady=true;ReadTraining(r,data,j);
            if(j.HangingAwaiting){
                if(j.TrainStatus==4||(DateTime.UtcNow-j.HangingResultLogAt).TotalSeconds>=1){j.HangingResultLogAt=DateTime.UtcNow;Log(j,"hanging_skill_result",",\"skill_id\":"+j.TrainSkillId+",\"status\":"+j.TrainStatus+",\"started\":"+j.TrainAttempts);}
                if(j.TrainStatus!=6)j.HangingSkillCursor++;
                j.HangingAwaiting=false;hanging=HangingLevelOne(j);overlay=hanging!=null;
            }
            if(j.SkillScanRequested||j.TrainEnabled||overlay||focus){
                int[] ids=j.TrainSkillIds;if(j.TrainEnabled&&ids.Length>0){if(j.TrainStatus==4||j.TrainStatus==7||j.TrainStatus==3)j.TrainCursor=(j.TrainCursor+1)%ids.Length;j.TrainSkillId=ids[j.TrainCursor%ids.Length];}
                if(overlay)j.TrainSkillId=hanging.Id;
                if(focus){SkillInfo[] chosen=ThiQuySkills(j);if(chosen.Length==0){r.Write(data+12,new byte[4]);j.SkillScanRequested=true;return;}if(j.TrainStatus==4||j.TrainStatus==7)j.ThiQuySkillCursor=(j.ThiQuySkillCursor+1)%chosen.Length;j.TrainSkillId=chosen[j.ThiQuySkillCursor%chosen.Length].Id;}
                bool levelOne=overlay||j.TrainEnabled&&Array.Exists(j.Skills,x=>x.Id==j.TrainSkillId&&x.Level==1);
                if(levelOne&&!focus&&DateTime.UtcNow<j.LevelOneNextAt)return;
                if(levelOne)j.LevelOneNextAt=DateTime.UtcNow.AddMilliseconds(overlay?10:500);
                if(overlay){j.HangingAwaiting=true;j.HangingRequests++;if((DateTime.UtcNow-j.HangingRequestLogAt).TotalSeconds>=1){j.HangingRequestLogAt=DateTime.UtcNow;Log(j,"hanging_skill_requested",",\"skill_id\":"+j.TrainSkillId+",\"interval_ms\":10,\"requests\":"+j.HangingRequests);}}
                r.Write(data+88,BitConverter.GetBytes(overlay?1:0));
                r.Write(data+80,BitConverter.GetBytes(j.QuestEnabled?(j.BeastQuestEnabled?2:1):0));r.Write(data+16,BitConverter.GetBytes(j.TrainSkillId));r.Write(data+12,BitConverter.GetBytes((focus?!j.GameAutoEnabled&&j.ThiQuyPriorityArmed&&(!j.FarmEnabled||j.FarmArrived||j.ThiQuyFarmPaused):(overlay||j.TrainEnabled&&(!j.QuestEnabled||j.QuestPhase==6&&!j.GameAutoEnabled)))&&!j.ReceiveFlowActive&&!j.ReconnectRequested&&!j.CloseRequested&&!j.CancelRequested&&!j.CloakEnabled&&(!UsesInstanceGameCombat(j)||overlay||focus)&&!DungeonEntryBlocked(j)&&!j.QuestPauseAuto&&!(j.ZoneSyncActive||j.FollowActive&&(j.FollowDistance<0||j.FollowDistance>48))?1:0));r.Write(data,BitConverter.GetBytes(1));j.SkillScanRequested=j.Skills.Length==0;
            }
            else {r.Write(data+12,new byte[4]);j.TrainIdleApplied=true;}
        }
        static void ReadTraining(Remote r,long data,NativeJob j){
            byte[] header=r.Read(data+4,24);int generation=BitConverter.ToInt32(header,0),count=BitConverter.ToInt32(header,4);j.TrainAttempts=BitConverter.ToInt32(header,16);j.TrainStatus=BitConverter.ToInt32(header,20);byte[] target=r.Read(data+68,8);j.TrainTargetId=BitConverter.ToInt32(target,0);j.TrainTargetHp=BitConverter.ToInt32(target,4);
            if(generation==0||(generation&1)!=0||generation==j.SkillGeneration||count<0||count>64)return;
            byte[] records=r.Read(data+128,count*32);if(BitConverter.ToInt32(r.Read(data+4,4),0)!=generation)return;
            List<SkillInfo> skills=new List<SkillInfo>();for(int i=0;i<count;i++){int off=i*32;skills.Add(new SkillInfo{Id=BitConverter.ToInt32(records,off),TemplateId=BitConverter.ToInt32(records,off+4),Mp=BitConverter.ToInt32(records,off+8),CastMs=BitConverter.ToInt32(records,off+12),Name=ReadLabel(r,BitConverter.ToInt64(records,off+16)),Level=BitConverter.ToInt32(records,off+24),Type=BitConverter.ToInt32(records,off+28)});}
            j.Skills=skills.ToArray();j.SkillGeneration=generation;
            List<string> fields=new List<string>();foreach(SkillInfo skill in skills)fields.Add("{\"id\":"+skill.Id+",\"template_id\":"+skill.TemplateId+",\"name\":"+Quote(skill.Name)+",\"type\":"+skill.Type+"}");string detail=String.Join(",",fields.ToArray());if(detail!=j.LastSkills){j.LastSkills=detail;Log(j,"skills",",\"items\":["+detail+"]");}
        }
        public static void SetReceiveKey(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.State=="failed"||j.State=="closed"||j.State=="cancelled")throw new Exception("npc_session_unavailable");
            if(j.SonCapEnabled&&!j.SonCapApplying)return;
            if(j.DailyManaged&&!j.DailyApplying){j.DailyPlan[0]=enabled;return;}
            if((j.MailQueueOnly||j.CloakQueueOnly)||j.IzanamiQueueOnly||j.QuestQueueOnly)enabled=false;
            if(enabled&&!j.ReceiveKeyEnabled)j.ReceiveKeyRequested=true;
            j.ReceiveKeyEnabled=enabled;if(!enabled){j.ReceiveKeyRequested=j.DungeonHighEnabled&&(j.ReceiveKeyRequested||(j.ReceiveFlowActive&&j.NpcAction==0));}
            if(enabled&&j.QuestEnabled)SetAutoQuest(id,false);
        }
        internal static int CountHighDungeonKeys(InventorySnapshot snapshot){
            if(snapshot==null||!snapshot.Ready)throw new Exception("dungeon_inventory_unavailable");
            long count=0;foreach(InventoryItem item in snapshot.Items){
                string name=(item.Name??"").Normalize(NormalizationForm.FormC).Trim().Replace("khoá","khóa").Replace("Khoá","Khóa");
                if(item.Quantity>0&&String.Equals(name,"Chìa khóa địa cung (cao cấp)",StringComparison.OrdinalIgnoreCase))count+=item.Quantity;
            }return (int)Math.Min(Int32.MaxValue,count);
        }
        internal static int ReadDungeonMap(Remote r,long game){
            long fcClass=ResolvedClass(r,game+0x16ee888),stat=r.Ptr(fcClass+0xb8);if(stat==0)throw new Exception("dungeon_map_unavailable");
            long fc=r.Ptr(stat+0x10);if(fc==0||r.Ptr(fc)!=fcClass)throw new Exception("dungeon_map_unavailable");
            // fc.fhe: map ID from the server's map-load packet (readShort).
            int map=BitConverter.ToInt16(r.Read(fc+0x134,2),0);if(map<0)throw new Exception("dungeon_map_unavailable");return map;
        }
        static void StopDungeon(Remote r,long data,NativeJob j,Stopwatch timer,string error){
            j.DungeonFinished=error=="dungeon_no_keys"&&!j.DungeonCycleWaiting&&(j.DungeonEntries==0||j.DungeonReturnConfirmed||j.DungeonProbeRefused);j.DungeonStopped=true;j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=false;j.DungeonKeyChecked=false;j.ReceiveKeyRequested=false;j.ReceiveFlowActive=false;
            j.DungeonStartAt=DateTime.MinValue;j.Error=error;j.State=error=="dungeon_no_keys"?"dungeon_no_keys":"dungeon_stopped";j.Stage="dungeon_stopped";
            r.Write(data+3292,new byte[4]);r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"dungeon_stopped",",\"reason\":"+Quote(error)+",\"keys\":"+j.DungeonKeyCount);
        }
        static bool CheckDungeonKeys(Remote r,long data,NativeJob j,Stopwatch timer,long game,Func<InventorySnapshot> reader){
            try{
                InventorySnapshot snapshot=reader==null?ReadInventorySnapshot(r,game):reader();j.Inventory=snapshot;j.DungeonKeyCount=CountHighDungeonKeys(snapshot);Log(j,"dungeon_keys_checked",",\"keys\":"+j.DungeonKeyCount);
                if(j.DungeonKeyCount==0){
                    if(j.DungeonEntries==0&&!j.DungeonResumeChecked){j.DungeonResumeProbe=true;j.DungeonKeyChecked=true;return true;}
                    StopDungeon(r,data,j,timer,"dungeon_no_keys");return false;
                }
                j.DungeonKeyChecked=true;return true;
            }catch(Exception e){Log(j,"dungeon_inventory_error",",\"detail\":"+Quote(e.Message));StopDungeon(r,data,j,timer,"dungeon_inventory_unavailable");return false;}
        }
        internal static void ObserveDungeonCycle(Remote r,long data,NativeJob j,Stopwatch timer,int map,DateTime now){
            if(!j.DungeonCycleWaiting||!j.DungeonHighEnabled||j.DungeonStopped)return;
            if(map!=j.DungeonOriginMap){j.DungeonResumeProbe=false;j.DungeonReturnConfirmed=false;j.DungeonFinished=false;j.DungeonEntryRetries=0;ObserveEntryAuto(j,map,now);j.DungeonLeftOrigin=true;j.DungeonReturnAt=DateTime.MinValue;j.State="dungeon_running";j.Stage="dungeon_wait_return";return;}
            if(!j.DungeonLeftOrigin){
                double elapsed=(now-j.DungeonCycleAt).TotalSeconds;if(elapsed<30)return;
                if(j.DungeonResumeProbe||j.DungeonEntryRetries>=2){StopDungeon(r,data,j,timer,"dungeon_entry_not_confirmed");return;}
                j.State="dungeon_retry_wait";j.TrainEnabled=false;
                if(elapsed<45)return;
                j.DungeonEntryRetries++;j.EntryAutoWaiting=false;j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=true;j.ReceiveKeyRequested=true;j.PartyReady=false;j.DungeonKeyChecked=false;
                Log(j,"dungeon_entry_retry",",\"retry\":"+j.DungeonEntryRetries+",\"map\":"+map+",\"origin_map\":"+j.DungeonOriginMap+",\"wait_seconds\":15");return;
            }
            if(j.DungeonReturnAt==DateTime.MinValue){j.DungeonReturnAt=now;return;}
            if((now-j.DungeonReturnAt).TotalSeconds<3)return;
            j.DungeonReturnConfirmed=true;j.GameAutoWanted=false;j.TrainEnabled=false;
            j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=true;j.ReceiveKeyRequested=true;j.PartyReady=false;j.DungeonKeyChecked=false;j.State="waiting_dungeon";
            Log(j,"dungeon_returned",",\"map_id\":"+map);
        }
        internal static bool ObserveDungeonProbeRefusal(NativeJob j,int map,string text){
            if(!j.DungeonHighEnabled||!j.DungeonCycleWaiting||!j.DungeonResumeChecked||j.DungeonEntries<1||j.DungeonLeftOrigin||map!=j.DungeonOriginMap)return false;
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD))if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark)b.Append(c=='đ'?'d':c);
            string n=System.Text.RegularExpressions.Regex.Replace(b.ToString(),@"\s+"," ");
            if(!n.Contains("khong co chia khoa dia cung")&&!n.Contains("khong du chia khoa dia cung"))return false;
            j.DungeonKeyCount=0;j.DungeonProbeRefused=true;j.DungeonCycleWaiting=false;Log(j,"dungeon_last_run_refused",",\"notice\":"+Quote(text));return true;
        }
        internal static void TrackDungeonEntrySubmission(NativeJob j,DateTime now){
            j.State="dungeon_submitted";j.DungeonResumeChecked=true;j.DungeonEntries++;j.DungeonFinished=false;j.DungeonCycleWaiting=true;j.DungeonLeftOrigin=false;j.DungeonReturnAt=DateTime.MinValue;j.DungeonCycleAt=now;j.DungeonPollAt=DateTime.MinValue;j.Stage="dungeon_wait_enter";
        }
        // A new NPC submission is not proof of entering another round. Keep the prior
        // confirmed return until a real map transition, then refresh keys before timeout.
        internal static bool RecheckReturnedDungeonKeys(NativeJob j,int map,Func<InventorySnapshot> reader){
            if(!j.DungeonHighEnabled||!j.DungeonCycleWaiting||!j.DungeonReturnConfirmed||j.DungeonLeftOrigin||map!=j.DungeonOriginMap)return false;
            try{InventorySnapshot snapshot=reader();int count=CountHighDungeonKeys(snapshot);int before=j.DungeonKeyCount;j.Inventory=snapshot;j.DungeonKeyCount=count;
                Log(j,"dungeon_entry_keys_rechecked",",\"before\":"+before+",\"keys\":"+count);return count==0;
            }catch(Exception e){Log(j,"dungeon_entry_keys_unavailable",",\"detail\":"+Quote(e.Message));return false;}
        }
        static void PumpDungeonCycle(Remote r,long data,long game,NativeJob j,Stopwatch timer){
            if(j.QuestEnabled)return;
            if(!j.DungeonHighEnabled){j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=false;return;}
            if(!j.DungeonCycleWaiting||j.DungeonStopped||j.ReceiveFlowActive)return;
            DateTime now=DateTime.UtcNow;if((now-j.DungeonPollAt).TotalSeconds<1)return;j.DungeonPollAt=now;
            try{
                int map=ReadDungeonMap(r,game);
                if(!j.DungeonLeftOrigin){
                    string popup=ReadKeyNotice(r,game);bool refused=popup!=j.DungeonProbePopupBefore&&ObserveDungeonProbeRefusal(j,map,popup);
                    if(!refused)foreach(var notice in ReadKeyNotifications(r,game))if(j.DungeonProbeNotices.Add(notice.Key)&&ObserveDungeonProbeRefusal(j,map,notice.Value)){refused=true;break;}
                    if(refused){StopDungeon(r,data,j,timer,"dungeon_no_keys");return;}
                }
                if((now-j.DungeonCycleAt).TotalSeconds>=30&&RecheckReturnedDungeonKeys(j,map,delegate{return ReadInventorySnapshot(r,game);})){
                    // The map can change while reading the bag. Never close on the origin snapshot alone.
                    map=ReadDungeonMap(r,game);
                    if(map==j.DungeonOriginMap){j.DungeonCycleWaiting=false;StopDungeon(r,data,j,timer,"dungeon_no_keys");return;}
                }
                ObserveDungeonCycle(r,data,j,timer,map,now);
            }catch{if((now-j.DungeonCycleAt).TotalSeconds>=30&&!j.DungeonLeftOrigin)StopDungeon(r,data,j,timer,"dungeon_map_unavailable");}
        }

        // Preserve every row, including unopened rows; never compact offline accounts.
        static int izanamiTeamSize=6;
        public static int IzanamiTeamSize{get{return izanamiTeamSize;}set{if(value<1||value>6)throw new ArgumentOutOfRangeException("value");izanamiTeamSize=value;}}
        public static void ConfigureIzanamiTeams(string[] ids){
            if(ids==null)return;
            ids=ids.Select(id=>{NativeJob p=Get(id??"");return p!=null&&(p.SonCapEnabled||p.BeastQuestEnabled)?"":id;}).ToArray();
            int size=IzanamiTeamSize;for(int start=0;start<ids.Length;start+=size){string[] block=new string[Math.Min(size,ids.Length-start)];Array.Copy(ids,start,block,0,block.Length);
                ConfigureIzanamiTeam(block);
            }
        }
        public static void ConfigureIzanamiTeam(string[] block){
            if(block==null||block.Length<1||block.Length>6)return;
                foreach(string id in block){NativeJob j=Get(id??"");if(j==null||j.BeastQuestEnabled||j.DailyManaged&&!j.DailyApplying)continue;
                    string key=String.Join("|",block);if(String.Join("|",j.TeamJobIds)!=key){j.TeamJobIds=block;j.TeamConfirmed=false;j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamFriendRequested=false;j.TeamTarget="";j.TeamAcceptedPopup=0;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;}
                    j.TeamManaged=true;if(id!=block[0])j.IzanamiRequested=false;
                }
        }
        // Đổi job sau mất kết nối vẫn giữ đúng vị trí thành viên trong tổ.
        public static void RebindIzanamiJob(string oldId,string newId){
            NativeJob old=Get(oldId),next=Get(newId);if(old==null||next==null||!old.IzanamiEnabled||old.SonCapEnabled||!old.TeamManaged)return;
            string[] team=old.TeamJobIds.Select(id=>id==oldId?newId:id).ToArray();next.IzanamiRoundEntered=old.IzanamiRoundEntered;next.IzanamiRoundComplete=old.IzanamiRoundComplete;next.EntryAutoKind=old.EntryAutoKind;next.EntryAutoOrigin=old.EntryAutoOrigin;next.EntryCombatMap=old.EntryCombatMap;next.IzanamiRoundsCompleted=old.IzanamiRoundsCompleted;next.IzanamiRoundsTarget=old.IzanamiRoundsTarget;next.TeamJobIds=(string[])team.Clone();next.TeamManaged=true;next.TeamConfirmed=false;next.IzanamiRequested=team.Length>0&&team[0]==next.Id;
            foreach(NativeJob peer in jobs.Values)if(peer!=old&&peer.TeamJobIds.Contains(oldId)){peer.TeamJobIds=peer.TeamJobIds.Select(id=>id==oldId?newId:id).ToArray();peer.TeamConfirmed=false;peer.LeaderZoneJob="";peer.LeaderZoneConfirmed=false;}
            old.IzanamiEnabled=false;old.IzanamiRequested=false;Log(next,"team_job_rebound",",\"previous_job\":"+Quote(oldId));
        }
        internal static long[] ReadVector(Remote r,long vector){
            if(vector==0)return new long[0];long list=r.Ptr(vector+16);if(list==0)throw new Exception("team_vector_unavailable");
            int count=BitConverter.ToInt32(r.Read(list+24,4),0);if(count<0||count>4096)throw new Exception("team_vector_invalid");long array=r.Ptr(list+16);if(array==0&&count>0)throw new Exception("team_vector_invalid");int capacity=array==0?0:BitConverter.ToInt32(r.Read(array+24,4),0);if(capacity<count||capacity>8192)throw new Exception("team_vector_invalid");byte[] raw=count==0?new byte[0]:r.Read(array+32,count*8);long[] items=new long[count];for(int n=0;n<count;n++)items[n]=BitConverter.ToInt64(raw,n*8);
            if(r.Ptr(vector+16)!=list||BitConverter.ToInt32(r.Read(list+24,4),0)!=count||r.Ptr(list+16)!=array)throw new Exception("team_snapshot_changed");
            Array.Resize(ref items,count);return items;
        }
        static string[] Names(Remote r,long vector,int nameOffset){List<string> names=new List<string>();foreach(long item in ReadVector(r,vector)){if(item==0)throw new Exception("team_member_unavailable");string name=ReadLabel(r,r.Ptr(item+nameOffset));if(name.Length==0)throw new Exception("team_name_unavailable");names.Add(name);}return names.ToArray();}
        internal static bool SameParty(string[] actual,string[] expected){if(actual.Length!=expected.Length||actual.Length==0||actual[0]!=expected[0])return false;HashSet<string> set=new HashSet<string>(actual,StringComparer.Ordinal);return set.Count==actual.Length&&set.SetEquals(expected);}
        static bool TeamLive(NativeJob j){return j!=null&&j.Auto&&!j.CloseRequested&&!j.CancelRequested&&j.CharacterName.Length>0&&(DateTime.UtcNow-j.TeamReadAt).TotalSeconds<4;}
        static void TeamCommand(Remote r,long data,NativeJob j,int command,string name,long popup=0,long button=0,long inviter=0){
            byte[] text=Encoding.UTF8.GetBytes((name??"")+"\0");if(text.Length>512)throw new Exception("team_name_too_long");
            r.Write(data+64,text);r.Write(data+8,BitConverter.GetBytes(popup));r.Write(data+16,BitConverter.GetBytes(button));r.Write(data+24,BitConverter.GetBytes(inviter));r.Write(data,BitConverter.GetBytes(command));j.TeamActionAt=DateTime.UtcNow;
            Log(j,"team_command",",\"command\":"+command+",\"character\":"+Quote(name??""));
        }
        // Kết bạn một lần; không phụ thuộc danh sách bạn đồng bộ mới gửi mời theo tên.
        // Mỗi loại lệnh có số lần thử riêng, chờ phản hồi trước khi gửi lại.
        internal static int PlanTeamPreparation(NativeJob j,string target,bool isFriend,DateTime now){
            if(j.TeamTarget!=target){j.TeamTarget=target;j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamFriendRequested=false;}
            double elapsed=(now-j.TeamActionAt).TotalSeconds;
            if(elapsed<5)return 0;
            if(target!="@create"&&!isFriend&&!j.TeamFriendRequested)return 1;
            int command=target=="@create"?4:2;
            if(j.TeamAttemptCommand==command){
                if(j.TeamAttempts>=3)return elapsed<30?0:-1;
                if(elapsed<15)return 0;
            }
            return command;
        }
        internal static void RecordTeamPreparation(NativeJob j,int command){
            if(command==1){j.TeamFriendRequested=true;return;}
            if(j.TeamAttemptCommand!=command){j.TeamAttemptCommand=command;j.TeamAttempts=0;}
            j.TeamAttempts++;
        }
        internal static bool NeedsTeamFriends(NativeJob j){
            return (j.BeastQuestEnabled||j.IzanamiEnabled&&j.IzanamiRequested)&&j.TeamManaged&&j.TeamJobIds.Length>0&&j.TeamJobIds[0]==j.Id;
        }
        internal static VitalsSnapshot ParseVitals(byte[] b,DateTime at){
            if(b==null||b.Length!=16)return null;int hp=BitConverter.ToInt32(b,0),mp=BitConverter.ToInt32(b,4),maxHp=BitConverter.ToInt32(b,8),maxMp=BitConverter.ToInt32(b,12);
            if(hp<0||mp<0||maxHp<=0||maxMp<=0)return null;return new VitalsSnapshot(hp,mp,maxHp,maxMp,at);
        }
        public static string VitalsDisplay(NativeJob j){
            VitalsSnapshot v=j==null?null:j.Vitals;
            if(v==null||j.CloseRequested||j.CancelRequested||(DateTime.UtcNow-v.CapturedAt).TotalSeconds>3)return "HP: — / —\r\nMP: — / —";
            return "HP: "+v.HP+" / "+v.MaxHP+"\r\nMP: "+v.MP+" / "+v.MaxMP;
        }
        static void PumpVitals(Remote r,long game,NativeJob j){
            DateTime now=DateTime.UtcNow;if((now-j.VitalsPollAt).TotalSeconds<1)return;j.VitalsPollAt=now;
            if(j.CloseRequested||j.CancelRequested||!j.TrainReady){j.Vitals=null;return;}
            try{
                long cls=ResolvedClass(r,game+0x16ee330),stat=r.Ptr(cls+0xb8),player=r.Ptr(stat+8);
                if(player==0||r.Ptr(player)!=cls){j.Vitals=null;return;}
                VitalsSnapshot snapshot=ParseVitals(r.Read(player+0xa8,16),now);
                j.Vitals=r.Ptr(stat+8)==player?snapshot:null;
            }catch{j.Vitals=null;}
        }
        internal static bool CanRepairSonCapParty(NativeJob j,NativeJob[] peers,string[] names){
            return j.SonCapEnabled&&j.SonCapRegroupAfterReturn&&j.SonCapStage<0&&j.MapId==59&&!j.SonCapFinished&&!j.CloseRequested&&!j.CancelRequested&&j.TeamMembers.Length>0&&names.Length>0&&j.TeamMembers[0]!=names[0]&&j.TeamMembers.All(n=>Array.IndexOf(names,n)>=0)&&peers.All(p=>p!=null&&p.SonCapEnabled&&p.SonCapStage<0&&p.MapId==59&&!p.SonCapFinished&&!p.CloseRequested&&!p.CancelRequested);
        }
        public static string SonCapDisplayStatus(NativeJob j){return j.SonCapRegroupAfterReturn&&j.SonCapStage<0&&!j.SonCapFinished&&j.SonCapFailure.Length==0&&j.TeamStatus.Length>0?"Sơn Cáp: vào lại • "+j.TeamStatus:j.SonCapStatus;}
        // Read identity as soon as the confirmed world/player exist, without waiting
        // for the startup popup delay or performing team actions.
        internal static void PumpCharacterIdentity(Remote r,long game,NativeJob j){
            DateTime now=DateTime.UtcNow;if((now-j.IdentityPollAt).TotalMilliseconds<250)return;j.IdentityPollAt=now;
            try{
                long fcClass=r.Ptr(game+0x16ee888),charClass=r.Ptr(game+0x16ee330),canvasClass=r.Ptr(game+0x16ed658);
                if(fcClass==0||charClass==0||canvasClass==0||((fcClass|charClass|canvasClass)&1)!=0)return;
                long fs=r.Ptr(fcClass+0xb8),cs=r.Ptr(charClass+0xb8),ds=r.Ptr(canvasClass+0xb8);if(fs==0||cs==0||ds==0)return;
                long world=r.Ptr(fs+16),player=r.Ptr(cs+8),canvas=r.Ptr(ds);if(world==0||player==0||canvas==0)return;
                if(r.Ptr(world)!=fcClass||r.Ptr(player)!=charClass||r.Ptr(canvas+0x1b8)!=world)return;
                string name=ReadLabel(r,r.Ptr(player+0xf0));
                if(name.Length>0&&r.Ptr(cs+8)==player&&r.Ptr(canvas+0x1b8)==world)j.CharacterName=name;
            }catch{}
        }
        static void PumpTeam(Remote r,long game,long data,NativeJob j){
            DateTime now=DateTime.UtcNow;if((now-j.TeamReadAt).TotalSeconds<1)return;
            try{
                long fcSlot=r.Ptr(game+0x16ee888),playerSlot=r.Ptr(game+0x16ee330);if(fcSlot==0||playerSlot==0||(fcSlot&1)!=0||(playerSlot&1)!=0)return;
                long stat=r.Ptr(fcSlot+0xb8),playerStat=r.Ptr(playerSlot+0xb8);if(stat==0||playerStat==0)return;long fc=r.Ptr(stat+16),player=r.Ptr(playerStat+8);if(fc==0||player==0)return;
                long canvasClass=r.Ptr(game+0x16ed658);if(canvasClass==0||(canvasClass&1)!=0)return;long canvasStat=r.Ptr(canvasClass+0xb8);if(canvasStat==0)return;long canvas=r.Ptr(canvasStat);if(canvas==0||r.Ptr(canvas+0x1b8)!=fc)return;
                j.MapId=BitConverter.ToInt16(r.Read(fc+0x134,2),0);j.ZoneId=BitConverter.ToInt16(r.Read(fc+0x13a,2),0);j.ZoneReadAt=now;ObserveEntryAuto(j,j.MapId,now);j.CharacterName=ReadLabel(r,r.Ptr(player+0xf0));long party=r.Ptr(fc+0x2f0);j.TeamMembers=party==0||(!j.IzanamiEnabled&&!j.IzanamiRoundEntered&&!j.BeastQuestEnabled)?new string[0]:Names(r,r.Ptr(party+16),16);j.Friends=NeedsTeamFriends(j)?Names(r,r.Ptr(fc+0xf8),24):new string[0];j.TeamReadAt=now;ObserveEntryAuto(j,j.MapId,now);
                if(j.BeastQuestEnabled){PumpBeastSoloParty(r,data,j,party,now);ReadBeastRasa(r,game,j,fc,player);return;}
                if(j.QuestEnabled){j.TeamStatus="Đang chạy nhiệm vụ tuần hoàn";return;}
                if(j.SonCapEnabled&&j.SonCapStage>=0){j.TeamStatus="Tổ Sơn Cáp đang ở hang";return;}
                if(IzanamiTeamTravelPending(j)){j.TeamStatus="Đợi các thành viên dùng phù về Làng Cát";return;}
                if(!j.IzanamiEnabled){j.TeamConfirmed=false;j.TeamStatus="Tắt";r.Write(data,new byte[4]);return;}j.TeamConfirmed=false;
                if(!j.IzanamiRequested&&!(j.ReceiveFlowActive&&j.NpcAction==2)&&j.TeamJobIds.Length>0&&j.TeamJobIds[0]==j.Id){j.TeamStatus="Đã gửi vào cấm thuật";return;}
                if(!j.TeamManaged||(j.TeamJobIds.Length<1||j.TeamJobIds.Length>6)){j.TeamStatus="Chờ tổ đội từ 1 đến 6 tài khoản";return;}
                int teamSize=j.TeamJobIds.Length;NativeJob[] peers=new NativeJob[teamSize];string[] names=new string[teamSize];for(int n=0;n<teamSize;n++){peers[n]=Get(j.TeamJobIds[n]??"");if(!TeamLive(peers[n])||!peers[n].IzanamiEnabled){j.TeamStatus="Chờ tất cả thành viên mở game và bật cấm thuật";return;}names[n]=peers[n].CharacterName;}
                if(new HashSet<string>(names,StringComparer.Ordinal).Count!=teamSize){j.TeamStatus="Tên nhân vật bị trùng; kiểm tra tài khoản/server";return;}
                if(!TeamTogether(peers)){j.TeamStatus="Chờ tổ đội cùng bản đồ/khu; "+j.ZoneStatus;return;}
                if(SameParty(j.TeamMembers,names)){bool all=true;foreach(NativeJob peer in peers)if(!SameParty(peer.TeamMembers,names))all=false;j.TeamConfirmed=all;j.TeamStatus=all?"Đủ tổ đội "+teamSize+"/"+teamSize:"Chờ đồng bộ nhóm "+teamSize+"/"+teamSize;j.TeamAttempts=0;return;}
                if(j.TeamMembers.Length>0&&(j.TeamMembers[0]!=names[0]||Array.Exists(j.TeamMembers,delegate(string name){return Array.IndexOf(names,name)<0;}))){
                    if(CanRepairSonCapParty(j,peers,names)&&!j.ReceiveFlowActive&&!j.TravelActive){
                        int pending=BitConverter.ToInt32(r.Read(data,4),0);j.TeamStatus="Nhóm bị tách; đang rời nhóm cũ để về nhóm "+names[0];
                        if((pending==0||pending==6||pending==7)&&(now-j.TeamActionAt).TotalSeconds>=5){r.Write(data+32,BitConverter.GetBytes(party));TeamCommand(r,data,j,8,"");j.TeamAcceptedPopup=0;Log(j,"soncap_regroup_leave",",\"expected_leader\":"+Quote(names[0]));}return;
                    }
                    j.TeamStatus="Đang ở nhóm khác; hãy rời nhóm trước";return;
                }
                int state=BitConverter.ToInt32(r.Read(data,4),0);if(state>0&&state<6){j.TeamStatus="Đợi game xử lý lệnh nhóm";return;}
                if(j.ReceiveFlowActive||j.DungeonCycleWaiting){j.TeamStatus="Đợi thao tác NPC/địa cung hoàn tất";return;}
                if(state==7)j.TeamAcceptedPopup=0;
                bool leader=j.Id==peers[0].Id;
                if(!leader){
                    j.TeamStatus="Chờ lời mời từ "+names[0];long inviter=r.Ptr(stat+24);if(ReadLabel(r,inviter)!=names[0])return;
                    long popup=r.Ptr(fc+0x90);if(popup==0||popup==j.TeamAcceptedPopup||Array.IndexOf(ReadVector(r,r.Ptr(fc+0x88)),popup)<0||r.Ptr(popup)!=r.Ptr(game+0x16eeba8)||r.Ptr(popup+0xd0)!=fc)return;
                    foreach(long control in ReadVector(r,r.Ptr(popup+0x50))){if(control!=0&&ReadLabel(r,r.Ptr(control+0x78))=="Đồng ý"&&BitConverter.ToInt32(r.Read(control+0x80,4),0)==2997){TeamCommand(r,data,j,3,names[0],popup,control,inviter);j.TeamAcceptedPopup=popup;j.TeamStatus="Đã gửi đồng ý; chờ server xác nhận";break;}}return;
                }
                string target=j.TeamMembers.Length==0?"@create":Array.Find(names,delegate(string name){return Array.IndexOf(j.TeamMembers,name)<0;});if(target==null)return;
                if(j.SonCapEnabled&&j.SonCapRegroupAfterReturn&&j.TeamAttempts>=3&&(now-j.TeamActionAt).TotalSeconds>=30){j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamAcceptedPopup=0;Log(j,"soncap_regroup_invite_retry",",\"target\":"+Quote(target));}
                int command=PlanTeamPreparation(j,target,Array.IndexOf(j.Friends,target)>=0,now);
                if(command<0){j.TeamStatus=target=="@create"?"Chưa xác nhận tạo nhóm; tắt/bật để thử lại":"Đã mời "+target+" 3 lần nhưng chưa vào nhóm; kiểm tra thành viên rồi tắt/bật để thử lại";return;}
                if(command==0){j.TeamStatus=target=="@create"?"Đợi xác nhận tạo nhóm":"Đợi "+target+" vào nhóm";return;}
                TeamCommand(r,data,j,command,target=="@create"?"":target);RecordTeamPreparation(j,command);
                j.TeamStatus=command==4?"Trưởng nhóm đang tạo nhóm":command==1?"Đã gửi kết bạn với "+target+"; chuẩn bị mời nhóm":"Đang mời "+target+" ("+j.TeamMembers.Length+"/"+teamSize+")";
            }catch(Exception e){j.TeamConfirmed=false;j.TeamStatus="Chưa đọc được nhóm: "+e.Message;}
        }

        internal static int ChooseQuietZone(int[] ids,int[] populations,int current){
            if(ids.Length!=populations.Length)return -1;int best=-1,count=Int32.MaxValue;
            for(int n=0;n<ids.Length;n++){if(ids[n]<0||ids[n]>255||populations[n]<0)continue;if(populations[n]<count||(populations[n]==count&&(ids[n]==current||best!=current&&ids[n]<best))){best=ids[n];count=populations[n];}}return best;
        }
        internal static bool TeamTogether(NativeJob[] peers){
            if(peers==null||(peers.Length<1||peers.Length>6)||!TeamLive(peers[0])||!peers[0].ZonePrepared||peers[0].MapId<0||peers[0].ZoneId<0)return false;
            foreach(NativeJob peer in peers)if(!TeamLive(peer)||peer.MapId!=peers[0].MapId||peer.ZoneId!=peers[0].ZoneId||peer.ZoneSyncActive)return false;return true;
        }
        internal static bool IzanamiEntryTeamReady(NativeJob j){
            if(j.PersistentUp&&!UpTeamReady(j,j.SonCapEnabled))return false;
            if(j.SonCapEnabled)return j.TeamConfirmed&&EntryZoneReady(j);
            if(!j.TeamConfirmed||j.IzanamiRunFinished||!EntryZoneReady(j))return false;
            NativeJob[] peers=j.TeamJobIds.Select(id=>Get(id??"")).ToArray();
            if(peers.Any(p=>!TeamLive(p)||!p.IzanamiEnabled||p.ReconnectRequested||p.IzanamiNoAttempts||p.State=="failed"||p.State=="closed"||p.State=="cancelled"))return false;
            string[] names=peers.Select(p=>p.CharacterName).ToArray();return peers.All(p=>SameParty(p.TeamMembers,names));
        }
        internal static bool EntryZoneReady(NativeJob j){NativeJob[] peers=new NativeJob[j.TeamJobIds.Length];for(int n=0;n<peers.Length;n++)peers[n]=Get(j.TeamJobIds[n]??"");return TeamTogether(peers);}
        static long ZonePopup(Remote r,long game){
            long fc=r.Ptr(r.Ptr(r.Ptr(game+0x16ee888)+0xb8)+16),zoneClass=r.Ptr(game+0x16f02b8);
            if(zoneClass==0||(zoneClass&1)!=0)return 0;foreach(long popup in ReadVector(r,r.Ptr(fc+0x88)))if(popup!=0&&r.Ptr(popup)==zoneClass)return popup;return 0;
        }
        static void QueueZone(Remote r,long data,NativeJob j,int command,int target){
            r.Write(data+8,BitConverter.GetBytes(j.MapId));r.Write(data+12,BitConverter.GetBytes(j.ZoneId));r.Write(data+16,BitConverter.GetBytes(target));r.Write(data,BitConverter.GetBytes(command));j.ZoneActionAt=DateTime.UtcNow;j.ZoneAttempts++;
            Log(j,"zone_command",",\"command\":"+command+",\"map\":"+j.MapId+",\"zone\":"+target);
        }
        // Thành viên lấy khu thực tế của trưởng nhóm; không tự chọn khu riêng.
        internal static bool PumpLeaderZoneSync(Remote r,long data,NativeJob j,DateTime now){
            if(!j.IzanamiEnabled||!j.TeamManaged||j.TeamJobIds.Length<1||j.TeamJobIds.Length>6||j.TeamJobIds[0]==j.Id)return false;
            if((now-j.ZonePollAt).TotalMilliseconds<750)return true;j.ZonePollAt=now;
            if(InEntryCombat(j)){j.ZoneSyncActive=false;return true;}
            NativeJob leader=Get(j.TeamJobIds[0]??"");j.ZoneSyncActive=true;
            if(!TeamLive(j)||!TeamLive(leader)||!leader.IzanamiEnabled||(now-j.TeamReadAt).TotalSeconds>=4||(now-leader.TeamReadAt).TotalSeconds>=4){j.ZoneStatus="Chờ dữ liệu khu mới của trưởng nhóm/thành viên";return true;}
            if(j.TravelActive||IzanamiTravelPending(j)||j.ReceiveFlowActive||j.ReceiveKeyRequested||j.ReplayRequested){j.ZoneStatus="Chờ nhân vật về làng và kết thúc thao tác NPC";return true;}
            if(j.MapId!=leader.MapId||j.MapId<0||j.ZoneId<0||leader.ZoneId<0){j.ZoneStatus="Chờ cùng map với "+leader.CharacterName+" ("+leader.MapId+")";return true;}
            int target=leader.ZoneId;
            if(j.LeaderZoneJob!=leader.Id||j.LeaderZoneMap!=j.MapId||j.LeaderZoneTarget!=target){
                j.LeaderZoneJob=leader.Id;j.LeaderZoneMap=j.MapId;j.LeaderZoneTarget=target;j.LeaderZoneConfirmed=false;j.PlannedZone=target;j.ZonePlanMap=j.MapId;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZonePrepared=false;
                Log(j,"team_zone_target",",\"leader\":"+Quote(leader.CharacterName)+",\"map\":"+j.MapId+",\"zone\":"+target);
            }
            int state=BitConverter.ToInt32(r.Read(data,4),0);if(state==1||state==2||state==3){j.ZoneStatus="Đợi lệnh khu đang xử lý; mục tiêu khu "+target;return true;}
            if(j.ZoneId==target){j.ZonePrepared=true;j.ZoneSyncActive=false;j.ZoneStatus="Cùng khu "+target+" với "+leader.CharacterName;if(!j.LeaderZoneConfirmed)Log(j,"team_zone_confirmed",",\"map\":"+j.MapId+",\"zone\":"+target);j.LeaderZoneConfirmed=true;return true;}
            j.LeaderZoneConfirmed=false;j.ZonePrepared=false;j.PlannedZone=target;
            int delay=j.ZoneAttempts>=3?30:8;j.ZoneStatus="Theo "+leader.CharacterName+": khu "+j.ZoneId+" → "+target+"; đợi xác nhận";
            if((now-j.ZoneActionAt).TotalSeconds<delay)return true;
            QueueZone(r,data,j,2,target);return true;
        }
        static void PumpZones(Remote r,long game,long data,NativeJob j){
            if(j.BeastQuestEnabled){PumpBeastCombatZones(r,game,data,j);return;}
            if(j.SonCapEnabled&&j.SonCapStage>=0){j.ZoneSyncActive=false;return;}
            if(j.FarmEnabled&&j.FarmQuietZone&&j.FarmZoneWaiting){PumpFarmZones(r,game,data,j);return;}
            if(j.QuestEnabled){j.ZoneSyncActive=false;return;}
            try{if(PumpLeaderZoneSync(r,data,j,DateTime.UtcNow))return;}catch(Exception e){j.ZoneSyncActive=true;j.ZoneStatus="Chưa đồng bộ khu trưởng nhóm: "+e.Message;return;}
            if(IzanamiTeamTravelPending(j)){j.ZoneSyncActive=true;j.ZoneStatus="Đợi tổ đội về Làng Cát";return;}
            DateTime now=DateTime.UtcNow;if((now-j.ZonePollAt).TotalMilliseconds<750)return;j.ZonePollAt=now;
            try{
                if(!j.IzanamiEnabled||!j.TeamManaged||(j.TeamJobIds.Length<1||j.TeamJobIds.Length>6)||!TeamLive(j)){j.ZoneSyncActive=false;j.ZoneStatus="";r.Write(data,new byte[4]);return;}
                NativeJob leader=Get(j.TeamJobIds[0]??"");if(!TeamLive(leader)||!leader.IzanamiEnabled){j.ZoneSyncActive=true;j.ZoneStatus="Chờ trưởng nhóm";return;}
                bool own=leader.Id==j.Id;
                if(j.MapId<0||j.ZoneId<0){j.ZoneSyncActive=true;j.ZoneStatus="Chờ dữ liệu khu từ server";return;}
                int state=BitConverter.ToInt32(r.Read(data,4),0);if(state==1||state==2||state==3){j.ZoneSyncActive=true;return;}
                if(own&&!j.IzanamiRequested){j.ZoneSyncActive=false;j.ZoneStatus="Khu "+j.ZoneId;return;}
                if(j.ReceiveFlowActive||j.ReceiveKeyRequested||j.ReplayRequested||j.DungeonCycleWaiting){j.ZoneSyncActive=true;j.ZoneStatus="Chờ thao tác NPC hoàn tất";return;}
                if(own){
                    if(!j.IzanamiRequested){j.ZoneSyncActive=false;j.ZoneStatus="Khu "+j.ZoneId;return;}
                    if(j.ZonePlanMap!=j.MapId){j.ZonePlanMap=j.MapId;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;}
                    if(j.PlannedZone<0){
                        j.ZoneSyncActive=true;j.ZoneStatus="Đọc số người từng khu";
                        if(j.ZoneListWaiting){long popup=ZonePopup(r,game);if(popup!=0&&popup==r.Ptr(data+32)&&r.Ptr(popup+0xe8)!=r.Ptr(data+24)){
                            long array=r.Ptr(popup+0xe8);int length=array==0?0:BitConverter.ToInt32(r.Read(array+24,4),0);if(length<1||length>256)throw new Exception("zone_list_invalid");
                            int[] ids=new int[length],counts=new int[length];for(int n=0;n<length;n++){long item=r.Ptr(array+32+n*8);if(item==0)throw new Exception("zone_list_changed");ids[n]=n;counts[n]=BitConverter.ToInt32(r.Read(item+16,4),0);}
                            if(r.Ptr(popup+0xe8)!=array||ZonePopup(r,game)!=popup)throw new Exception("zone_list_changed");j.PlannedZone=ChooseQuietZone(ids,counts,j.ZoneId);if(j.PlannedZone<0)throw new Exception("zone_list_invalid");j.ZoneListWaiting=false;j.ZoneAttempts=0;j.ZoneActionAt=DateTime.MinValue;Log(j,"quiet_zone_chosen",",\"zone\":"+j.PlannedZone);}
                            else if((now-j.ZoneActionAt).TotalSeconds<10)return;else j.ZoneListWaiting=false;
                        }
                        if(j.PlannedZone<0){if(j.ZoneAttempts>=3){j.ZoneStatus="Chưa đọc được khu; chờ 30 giây để thử lại";if((now-j.ZoneActionAt).TotalSeconds<30)return;j.ZoneAttempts=0;Log(j,"zone_list_retry","");}j.ZoneOldPopup=ZonePopup(r,game);QueueZone(r,data,j,1,-1);j.ZoneListWaiting=true;return;}
                    }
                    if(j.ZoneId==j.PlannedZone&&(j.ZonePrepared||state==4&&j.ZoneAttempts>0)){j.ZonePrepared=true;j.ZoneSyncActive=false;j.ZoneStatus="Đã chọn khu ít người: "+j.ZoneId;return;}
                }else{
                    if(j.MapId!=leader.MapId){j.ZoneSyncActive=true;j.ZoneStatus="Chờ cùng bản đồ với trưởng nhóm";return;}
                    if(!leader.ZonePrepared&&leader.IzanamiRequested){j.ZoneSyncActive=true;j.ZoneStatus="Chờ trưởng nhóm chọn khu ít người";return;}
                    int target=leader.ZoneId;if(j.PlannedZone!=target||j.ZonePlanMap!=j.MapId){j.PlannedZone=target;j.ZonePlanMap=j.MapId;j.ZoneAttempts=0;j.ZoneActionAt=DateTime.MinValue;}
                    if(j.ZoneId==target){j.ZoneSyncActive=false;j.ZoneStatus="Cùng khu "+target+" với trưởng nhóm";return;}
                }
                j.ZoneSyncActive=true;j.ZoneStatus="Chuyển tới khu "+j.PlannedZone+"; chờ server xác nhận";
                if((now-j.ZoneActionAt).TotalSeconds<8)return;if(j.ZoneAttempts>=3){j.ZoneStatus="Chưa đổi được khu "+j.PlannedZone+"; chờ 30 giây để đọc lại khu";if((now-j.ZoneActionAt).TotalSeconds<30)return;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;Log(j,"zone_reselect_after_retry","");return;}QueueZone(r,data,j,2,j.PlannedZone);
            }catch(Exception e){j.ZoneSyncActive=true;j.ZoneStatus="Chưa đồng bộ khu: "+e.Message;}
        }
        internal static bool IsIzanamiLeader(NativeJob j){return j.TeamManaged&&(j.TeamJobIds.Length>=1&&j.TeamJobIds.Length<=6)&&j.TeamJobIds[0]==j.Id;}
        internal static bool InEntryCombat(NativeJob j){return j.EntryCombatMap>=0&&j.MapId==j.EntryCombatMap;}
        internal static bool ShouldFollow(NativeJob j,NativeJob leader){return !j.QuestEnabled&&!(j.SonCapEnabled&&j.SonCapStage>=0)&&!InEntryCombat(j)&&j.IzanamiEnabled&&!j.CloseRequested&&!j.CancelRequested&&j.TeamManaged&&(j.TeamJobIds.Length>=1&&j.TeamJobIds.Length<=6)&&TeamLive(leader)&&leader.IzanamiEnabled&&j.Id!=leader.Id&&j.TeamMembers.Length==j.TeamJobIds.Length&&j.TeamMembers[0]==leader.CharacterName&&Array.IndexOf(j.TeamMembers,j.CharacterName)>=0;}
        static void PumpFollow(Remote r,long game,long data,NativeJob j){
            if(data==0)return;DateTime now=DateTime.UtcNow;if((now-j.FollowPollAt).TotalMilliseconds<750)return;j.FollowPollAt=now;
            try{
                NativeJob leader=(j.TeamJobIds.Length>=1&&j.TeamJobIds.Length<=6)?Get(j.TeamJobIds[0]??""):null;bool wanted=ShouldFollow(j,leader)&&(now-j.TeamReadAt).TotalSeconds<4&&!j.ReceiveFlowActive&&!j.ReceiveKeyRequested&&!j.ReplayRequested;
                int state=BitConverter.ToInt32(r.Read(data,4),0);j.FollowMoves=BitConverter.ToInt32(r.Read(data+52,4),0);
                if(!wanted){if(j.FollowActive)j.FollowStopPending=true;j.FollowActive=false;j.FollowDistance=-1;j.FollowStatus=InEntryCombat(j)?"Đã vào cấm thuật; nhường di chuyển cho treo đánh quái":"";if(j.FollowStopPending&&state==0){r.Write(data,BitConverter.GetBytes(2));j.FollowStopPending=false;}return;}
                if(j.ZoneSyncActive||j.MapId!=leader.MapId||j.ZoneId!=leader.ZoneId){j.FollowActive=true;j.FollowDistance=-1;j.FollowStatus=j.ZoneStatus;return;}j.FollowStopPending=false;j.FollowActive=true;
                // A pending native sample does not invalidate the previous distance.
                // Resetting it every poll made settings turn idle/train off between samples.
                if(state!=0)return;
                long stat=r.Ptr(r.Ptr(game+0x16ee888)+0xb8),fc=r.Ptr(stat+16),party=r.Ptr(fc+0x2f0);if(party==0)return;long[] members=ReadVector(r,r.Ptr(party+16));if(members.Length!=j.TeamJobIds.Length||ReadLabel(r,r.Ptr(members[0]+16))!=leader.CharacterName)return;
                long candidate=0;foreach(long player in ReadVector(r,r.Ptr(fc+0x148))){if(player!=0&&ReadLabel(r,r.Ptr(player+0xf0))==leader.CharacterName){if(candidate!=0){j.FollowStatus="Tên trưởng nhóm trùng trên bản đồ";return;}candidate=player;}}
                if(candidate==0){j.FollowDistance=-1;j.FollowStatus="Chờ trưởng nhóm xuất hiện trên bản đồ";return;}
                int result=BitConverter.ToInt32(r.Read(data+36,4),0);j.FollowDistance=result>=4?-1:BitConverter.ToInt32(r.Read(data+40,4),0);j.FollowStatus=result==2?"Đã ở gần "+leader.CharacterName:result==3?"Đợi chiêu kết thúc để đi theo":"Đi theo "+leader.CharacterName;
                r.Write(data+8,BitConverter.GetBytes(party));r.Write(data+16,BitConverter.GetBytes(r.Ptr(candidate+0xf0)));r.Write(data+24,BitConverter.GetBytes(r.Ptr(members[0]+16)));r.Write(data+32,r.Read(candidate+0x38,4));r.Write(data,BitConverter.GetBytes(1));
            }catch{if(j.FollowActive)j.FollowStopPending=true;j.FollowActive=false;j.FollowDistance=-1;j.FollowStatus="Chưa đọc được trưởng nhóm";}
        }
        public static void SetIzanami(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.State=="failed"||j.State=="closed"||j.State=="cancelled")throw new Exception("npc_session_unavailable");
            if(j.SonCapEnabled&&!j.SonCapApplying)return;
            if(j.DailyManaged&&!j.DailyApplying){j.DailyPlan[2]=enabled;return;}
            if((j.MailQueueOnly||j.CloakQueueOnly)||j.DungeonQueueOnly||j.QuestQueueOnly)enabled=false;
            if(enabled&&!j.IzanamiEnabled){j.IzanamiRoundsCompleted=0;j.IzanamiRoundsTarget=Math.Max(1,IzanamiRoundLimit);j.IzanamiTravelReady=false;j.IzanamiTravelStopped=false;j.IzanamiRequested=!j.TeamManaged||(j.TeamJobIds.Length>0&&j.TeamJobIds[0]==j.Id);j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamFriendRequested=false;j.TeamTarget="";j.TeamAcceptedPopup=0;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;}j.IzanamiEnabled=enabled;if(!enabled){j.IzanamiRequested=false;j.IzanamiMapAutoActive=false;j.IzanamiMapSeenAt=DateTime.MinValue;}
            if(enabled&&j.QuestEnabled)SetAutoQuest(id,false);
        }
        public static void SetDungeonHigh(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.State=="failed"||j.State=="closed"||j.State=="cancelled")throw new Exception("npc_session_unavailable");
            if(j.SonCapEnabled&&!j.SonCapApplying)return;
            if(j.DailyManaged&&!j.DailyApplying){j.DailyPlan[1]=enabled;return;}
            if((j.MailQueueOnly||j.CloakQueueOnly)||j.IzanamiQueueOnly||j.QuestQueueOnly)enabled=false;
            if(enabled&&!j.DungeonHighEnabled){j.DungeonProbeRefused=false;j.DungeonResumeProbe=false;j.DungeonResumeChecked=false;j.DungeonReturnConfirmed=false;j.DungeonEntryRetries=0;j.DungeonFinished=false;j.DungeonStopped=false;j.DungeonKeyChecked=false;j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=false;if(!j.ReceiveFlowActive){j.PartyReady=false;j.ReceiveKeyRequested=true;}}
            j.DungeonHighEnabled=enabled;if(!enabled){j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=false;j.DungeonStopped=false;}
            if(enabled&&j.QuestEnabled)SetAutoQuest(id,false);
            if(!enabled&&!j.ReceiveKeyEnabled)j.ReceiveKeyRequested=false;
        }
        public static void SetCloseAfterKey(string id,bool enabled){NativeJob j=Get(id);if(j==null||j.CloseRequested)throw new Exception("game_session_unavailable");j.CloseAfterKey=enabled&&!KeepGameForUp(j)&&!j.DungeonQueueOnly&&!j.MailQueueOnly&&!j.CloakQueueOnly&&!j.IzanamiQueueOnly&&!j.QuestQueueOnly;}
        internal static int CountReceiptKeys(InventorySnapshot snapshot){if(snapshot==null||!snapshot.Ready)throw new Exception("key_receipt_unverified");long count=0;foreach(InventoryItem item in snapshot.Items){string n=(item.Name??"").Normalize(NormalizationForm.FormC).Replace("khoá","khóa");if(item.Quantity>0&&n.StartsWith("Chìa khóa địa cung",StringComparison.OrdinalIgnoreCase))count+=item.Quantity;}return (int)Math.Min(Int32.MaxValue,count);}
        internal static int ReadReceiptKeyCount(Remote r,long game){
            long canvasClass=ResolvedClass(r,game+0x16ed658),playerClass=ResolvedClass(r,game+0x16ee330),itemClass=ResolvedClass(r,game+0x16ee578);
            long cs=r.Ptr(canvasClass+0xb8),ps=r.Ptr(playerClass+0xb8);if(cs==0||ps==0)throw new Exception("key_inventory_not_ready");
            long canvas=r.Ptr(cs),player=r.Ptr(ps+8);if(canvas==0||player==0)throw new Exception("key_inventory_not_ready");
            long bag=r.Ptr(player+0x128),templates=r.Ptr(canvas+0x118);int capacity,templateCount;
            byte[] slots=InventoryArray(r,bag,512,out capacity),catalog=InventoryArray(r,templates,65536,out templateCount);
            List<long> addresses=new List<long>();List<byte[]> values=new List<byte[]>();long count=0;
            for(int i=0;i<capacity;i++){
                long item=BitConverter.ToInt64(slots,i*8);if(item==0)continue;if(r.Ptr(item)!=itemClass)throw new Exception("key_inventory_changed");
                int id=BitConverter.ToInt32(r.Read(item+20,4),0);if(id<0||id>=templateCount)throw new Exception("key_template_missing");
                long template=BitConverter.ToInt64(catalog,id*8);if(template==0)throw new Exception("key_template_missing");long label=r.Ptr(template+24);
                if(label==0)continue;int n=BitConverter.ToInt32(r.Read(label+16,4),0);if(n<0||n>512)throw new Exception("key_label_invalid");
                string name=Encoding.Unicode.GetString(r.Read(label+20,n*2)).Normalize(NormalizationForm.FormC).Replace("khoá","khóa");
                if(!name.StartsWith("Chìa khóa địa cung",StringComparison.OrdinalIgnoreCase))continue;
                byte[] fields=r.Read(item+20,8);if(BitConverter.ToInt32(fields,0)!=id)throw new Exception("key_inventory_changed");int quantity=BitConverter.ToInt32(fields,4);if(quantity>0)count+=quantity;addresses.Add(item+20);values.Add(fields);
            }
            for(int i=0;i<addresses.Count;i++)if(!SameBytes(values[i],r.Read(addresses[i],values[i].Length)))throw new Exception("key_inventory_changed");
            if(r.Ptr(cs)!=canvas||r.Ptr(ps+8)!=player||r.Ptr(player+0x128)!=bag||r.Ptr(canvas+0x118)!=templates||r.Ptr(bag+24)!=capacity||r.Ptr(templates+24)!=templateCount||!SameBytes(slots,r.Read(bag+32,slots.Length))||!SameBytes(catalog,r.Read(templates+32,catalog.Length)))throw new Exception("key_inventory_changed");
            return (int)Math.Min(Int32.MaxValue,count);
        }
        internal static bool IsDailyKeyLimit(string text){
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD)){if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark)continue;b.Append(c=='đ'?'d':Char.IsLetterOrDigit(c)?c:' ');}
            string s=System.Text.RegularExpressions.Regex.Replace(b.ToString(),"\\s+"," ").Trim();
            bool once=System.Text.RegularExpressions.Regex.IsMatch(s,"(?:^| )(?:1|mot) lan(?: |$)");return s.Contains("nhan")&&((s.Contains("moi ngay")&&once)||(s.Contains("chi")&&s.Contains("ngay")&&once)||(s.Contains("da nhan")&&s.Contains("chia")&&(s.Contains("hom nay")||s.Contains("trong ngay"))));
        }
        // Chỉ nhận diện lời nhắc giải bùa; không đọc ảnh mã, ô nhập hoặc gửi xác nhận.
        internal static bool IsCurseNotice(string text){
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD)){
                if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark)continue;
                b.Append(c=='đ'?'d':Char.IsLetterOrDigit(c)?c:' ');
            }
            string normalized=System.Text.RegularExpressions.Regex.Replace(b.ToString(),"\\s+"," ").Trim();
            return normalized.Contains("nhap ma")&&(normalized.Contains("giai tru bua")||normalized.Contains("giai bua"))&&(normalized.Contains("yem bua")||normalized.Contains("bua chu"));
        }
        internal static void ObserveCurseNotice(NativeJob j,string notice,DateTime now){
            bool active=!j.CloseRequested&&!j.CancelRequested&&IsCurseNotice(notice);bool changed=j.CurseActive!=active;
            j.CurseActive=active;j.CurseReadAt=now;
            if(changed)Log(j,active?"curse_detected":"curse_cleared",",\"pid\":"+j.ProcessId);
        }
        public static string CurseDisplayStatus(NativeJob j){
            return j!=null&&j.CurseActive&&!j.CloseRequested&&!j.CancelRequested&&j.State!="closed"&&j.State!="failed"&&j.State!="cancelled"&&(DateTime.UtcNow-j.CurseReadAt).TotalSeconds<=5?"Bị bùa — cần nhập mã":"";
        }
        // Chỉ khôi phục khi hộp thoại hiện tại báo mất kết nối, không dựa vào FPS.
        internal static bool IsDisconnectNotice(string text){
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD)){
                if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark)continue;
                b.Append(c=='đ'?'d':Char.IsLetterOrDigit(c)?c:' ');
            }
            string normalized=System.Text.RegularExpressions.Regex.Replace(b.ToString(),"\\s+"," ").Trim();
            return normalized.Contains("mat ket noi")&&normalized.Contains("may chu");
        }
        internal static string ReadDisconnectNotice(Remote r,long game){
            try{
                long slot=ResolvedClass(r,game+0x16ed658),stat=r.Ptr(slot+0xb8),canvas=r.Ptr(stat);
                if(canvas==0||r.Ptr(canvas)!=slot)return "";
                long screen=r.Ptr(canvas+0x1b8);if(screen==0)return "";
                // dl.ewy là màn hình hk hiện tại; hk.gco là popup đang hiển thị.
                long popup=r.Ptr(screen+0x90);
                if(popup==0||r.Ptr(popup)!=ResolvedClass(r,game+0x16eeba8)||Array.IndexOf(ReadVector(r,r.Ptr(screen+0x88)),popup)<0)return "";
                List<string> lines=new List<string>();string body=ReadLabel(r,r.Ptr(popup+0xb0));if(body.Length>0)lines.Add(body);
                foreach(long line in ReadArray(r,r.Ptr(popup+0xa8))){string part=ReadLabel(r,line);if(part.Length>0)lines.Add(part);}
                if(r.Ptr(canvas+0x1b8)!=screen||r.Ptr(screen+0x90)!=popup)return "";
                return String.Join(" ",lines.ToArray());
            }catch{return "";}
        }
        internal static bool ObserveDisconnect(NativeJob j,string notice,DateTime now){
            if(!AutoReconnectEnabled||j.CloseRequested||j.CancelRequested){j.DisconnectSeenAt=DateTime.MinValue;return false;}
            if(j.ReconnectRequested)return false;
            if(!IsDisconnectNotice(notice)){j.DisconnectSeenAt=DateTime.MinValue;return false;}
            if(j.DisconnectSeenAt==DateTime.MinValue){j.DisconnectSeenAt=now;return false;}
            if((now-j.DisconnectSeenAt).TotalSeconds<2)return false;
            j.ReconnectRequested=true;j.State="reconnecting";j.Error="game_disconnected";
            Log(j,"disconnect_detected",",\"pid\":"+j.ProcessId+",\"message\":"+Quote(notice));return true;
        }
        static void BeginDisconnectRecovery(NativeJob j){BeginSessionRecovery(j,"game_disconnected");}
        static void BeginSessionRecovery(NativeJob j,string reason){
            Log(j,"session_recovery_requested",",\"reason\":"+Quote(reason)+",\"pid\":"+j.ProcessId);
            Thread worker=new Thread(delegate(){
                while(!j.CancelRequested&&!j.CloseRequested){
                    try{
                        CloseGamesResult closed=ManagedGames.CloseOne(j.ProcessId);
                        if(closed.Failed==0&&!ManagedGames.IsRunning(j.ProcessId)){
                            if(!j.CancelRequested&&!j.CloseRequested){j.Error=reason;j.State="failed";Log(j,"reconnect_ready",",\"pid\":"+j.ProcessId+",\"reason\":"+Quote(reason));}return;
                        }
                        Log(j,"reconnect_close_pending",",\"pid\":"+j.ProcessId);
                    }catch{Log(j,"reconnect_close_error",",\"pid\":"+j.ProcessId);}
                    Thread.Sleep(5000);
                }
            });worker.IsBackground=true;worker.Start();
        }
        internal static string ReadKeyNotice(Remote r,long game){
            try{
                long cls=ResolvedClass(r,game+0x16ee888),stat=r.Ptr(cls+0xb8),fc=r.Ptr(stat+16);if(fc==0||r.Ptr(fc)!=cls)return "";
                long popup=r.Ptr(fc+0x90);if(popup==0||r.Ptr(popup)!=ResolvedClass(r,game+0x16eeba8)||Array.IndexOf(ReadVector(r,r.Ptr(fc+0x88)),popup)<0)return "";
                // je.gpp is the message; je.gpo holds its wrapped string lines.
                List<string> lines=new List<string>();string body=ReadLabel(r,r.Ptr(popup+0xb0));if(body.Length>0)lines.Add(body);foreach(long line in ReadArray(r,r.Ptr(popup+0xa8))){string textLine=ReadLabel(r,line);if(textLine.Length>0)lines.Add(textLine);}string text=String.Join(" ",lines.ToArray());
                if(r.Ptr(fc+0x90)!=popup)return "";return text;
            }catch{return "";}
        }
        internal static Dictionary<string,string> ReadKeyNotifications(Remote r,long game){
            Dictionary<string,string> result=new Dictionary<string,string>();
            try{
                // nq.hse is cj[], not Vector[]. Each cj.enz (+16) holds its Vector.
                // nq.dji stores server notifications (channel 7) as cl objects.
                long cls=ResolvedClass(r,game+0x16ee3b8),stat=r.Ptr(cls+0xb8),messageClass=ResolvedClass(r,game+0x16f1d50);
                foreach(long channel in ReadArray(r,r.Ptr(stat))){
                    if(channel==0)continue;long vector=r.Ptr(channel+16);
                    foreach(long item in ReadVector(r,vector)){
                        if(item==0||r.Ptr(item)!=messageClass||BitConverter.ToInt32(r.Read(item+16,4),0)!=7)continue;
                        long body=r.Ptr(item+40);string text=ReadLabel(r,body);if(text.Length==0)continue;
                        // A repeated identical server response is still a new event.
                        result[item.ToString("x")+":"+body.ToString("x")]=text;
                    }
                }
            }catch{}
            return result;
        }
        internal static bool ObserveKeyNotifications(NativeJob j,Dictionary<string,string> entries){
            foreach(KeyValuePair<string,string> entry in entries){
                if(!j.KeyNotificationSeen.Add(entry.Key))continue;
                bool daily=IsDailyKeyLimit(entry.Value);
                Log(j,"key_receipt_notice",",\"source\":\"notification\",\"message\":"+Quote(entry.Value)+",\"daily_limit\":"+(daily?"true":"false"));
                if(daily){Log(j,"key_receipt_daily_limit",",\"source\":\"notification\",\"message\":"+Quote(entry.Value));return true;}
            }
            return false;
        }
        static void CloseAfterReceipt(NativeJob j,Stopwatch timer,Action close){
            j.KeyReceiptPending=false;j.ReceiveFlowActive=false;j.DungeonStartAt=DateTime.MinValue;timer.Stop();
            if(j.DailyActive&&j.DailyStage==1){j.KeyReceiptVerified=true;j.ReceiveKeyEnabled=false;j.State="key_submitted";Log(j,"daily_key_verified","");return;}
            if(KeepGameForUp(j)){j.KeyReceiptVerified=true;j.ReceiveKeyEnabled=false;j.State="key_submitted";Log(j,"up_keeps_game_open","");return;}
            try{Log(j,"key_game_close_requested","");if(close==null){if(CloseGame(j.Id).Failed>0)throw new Exception("key_game_close_failed");}else close();Log(j,"key_game_close_completed","");}
            catch{j.State="key_submitted";j.Error="key_game_close_failed";Log(j,"key_game_close_failed","");}
        }
        internal static void PumpKeyReceipt(Remote r,long data,NativeJob j,Stopwatch timer,long game,Func<InventorySnapshot> reader,Action close=null,Func<string> noticeReader=null){
            if(!j.CloseAfterKey&&!(j.DailyActive&&j.DailyStage==1)){j.KeyReceiptPending=false;j.ReceiveFlowActive=false;j.State="key_submitted";return;}
            if(!j.KeyReceiptPending)return;
            if(noticeReader==null&&ObserveKeyNotifications(j,ReadKeyNotifications(r,game))){CloseAfterReceipt(j,timer,close);return;}
            string notice=noticeReader==null?ReadKeyNotice(r,game):noticeReader();
            if(notice!=j.KeyNoticeLast){j.KeyNoticeLast=notice;Log(j,"key_receipt_notice",",\"message\":"+Quote(notice)+",\"daily_limit\":"+(IsDailyKeyLimit(notice)?"true":"false"));}
            if(notice!=j.KeyNoticeBefore&&IsDailyKeyLimit(notice)){Log(j,"key_receipt_daily_limit",",\"message\":"+Quote(notice));CloseAfterReceipt(j,timer,close);return;}
            if(DateTime.UtcNow<j.KeyReceiptPollAt)return;j.KeyReceiptPollAt=DateTime.UtcNow.AddSeconds(1);
            int count=-1;
            string readError="";try{if(reader==null)count=ReadReceiptKeyCount(r,game);else{InventorySnapshot snapshot=reader();j.Inventory=snapshot;count=CountReceiptKeys(snapshot);}}catch(Exception e){readError=e.Message;}
            if(count!=j.KeyReceiptLastCount||readError!=j.KeyReceiptReadError){j.KeyReceiptLastCount=count;j.KeyReceiptReadError=readError;Log(j,"key_receipt_check",",\"before\":"+j.KeyReceiptBefore+",\"after\":"+count+",\"read_error\":"+Quote(readError));}
            if(j.KeyReceiptBefore>=0&&count>j.KeyReceiptBefore){
                Log(j,"key_receipt_confirmed",",\"before\":"+j.KeyReceiptBefore+",\"after\":"+count);CloseAfterReceipt(j,timer,close);
                return;
            }
            if((DateTime.UtcNow-j.KeyReceiptAt).TotalSeconds>20){j.KeyReceiptPending=false;j.ReceiveFlowActive=false;timer.Stop();j.State="key_submitted";j.Error="key_receipt_unverified";Log(j,"key_receipt_unverified","");}
        }
        internal static void FinishNpcAction(Remote r,long data,NativeJob j,Stopwatch timer){
            r.Write(data,BitConverter.GetBytes(12));
            if(j.NpcAction==0){
                Log(j,"npc_key_selected",",\"npc_id\":"+j.NpcId+",\"index\":"+j.MenuSelectedIndex);
                if(j.CloseAfterKey||j.DailyActive&&j.DailyStage==1){j.KeyReceiptPending=true;j.KeyReceiptAt=DateTime.UtcNow;j.KeyReceiptPollAt=DateTime.MinValue;j.State="waiting_key_receipt";j.ReceiveFlowActive=true;j.DungeonStartAt=DateTime.MinValue;timer.Stop();return;}
                if(j.DungeonHighEnabled){j.State="waiting_dungeon";j.DungeonStartAt=DateTime.UtcNow.AddMilliseconds(750);timer.Restart();return;}
                j.State="key_submitted";
            }else if(j.NpcAction==2){if(j.SonCapEnabled){foreach(string id in j.TeamJobIds){NativeJob peer=Get(id);if(peer!=null&&peer.SonCapEnabled){peer.SonCapEntrySubmitted=true;peer.SonCapStatus="Đã gửi vào Sơn Cáp; đợi map hang";}}Log(j,"npc_soncap_selected",",\"npc_id\":"+j.NpcId+",\"template_id\":"+j.NpcTemplateId+",\"index\":"+j.MenuSelectedIndex);}SubmitIzanamiAuto(j);j.State="izanami_submitted";j.IzanamiEntries++;j.Stage="izanami_done";Log(j,"npc_izanami_selected",",\"npc_id\":"+j.NpcId+",\"template_id\":"+j.NpcTemplateId+",\"index\":"+j.MenuSelectedIndex);}
            else{PrepareEntryAuto(j,1,j.DungeonOriginMap);SubmitEntryAuto(j);TrackDungeonEntrySubmission(j,DateTime.UtcNow);Log(j,"npc_dungeon_selected",",\"npc_id\":"+j.NpcId+",\"index\":"+j.MenuSelectedIndex);}
            j.ReceiveFlowActive=false;j.DungeonStartAt=DateTime.MinValue;timer.Stop();
        }
        internal static string StartupPopupFailureState(NativeJob j){return j.ReceiveKeyEnabled||j.DungeonHighEnabled?"npc_unavailable":"startup_unavailable";}
        internal static void CancelTravel(Remote r,NativeJob j){if(j.TravelData!=0){r.Write(j.TravelData+12,new byte[4]);r.Write(j.TravelData+64,new byte[4]);}j.TravelActive=false;j.TravelReady=false;j.TravelPhase=0;}
        internal static bool IzanamiTravelPending(NativeJob j){return j!=null&&j.IzanamiEnabled&&j.TravelData!=0&&!j.IzanamiTravelReady&&!j.IzanamiRoundEntered&&!j.IzanamiRoundComplete;}
        internal static bool IzanamiTeamTravelPending(NativeJob j){
            if(IzanamiTravelPending(j))return true;
            foreach(string id in j.TeamJobIds)if(IzanamiTravelPending(Get(id)))return true;
            return false;
        }
        internal static void PumpIzanamiTravel(Remote r,long data,NativeJob j,Stopwatch timer){
            if(!IzanamiTravelPending(j)||j.IzanamiTravelStopped||j.TravelActive||j.ReceiveFlowActive||j.ReceiveKeyRequested||j.ReplayRequested||j.DungeonCycleWaiting||j.KeyReceiptPending||j.QuestEnabled||j.CancelRequested||j.CloseRequested||!j.GameSettingsReady||!j.TrainReady)return;
            j.NpcAction=2;j.TeamConfirmed=false;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;
            BeginTravel(r,data,j,timer);Log(j,"izanami_travel_requested","");
        }
        internal static void BeginTravel(Remote r,long data,NativeJob j,Stopwatch timer){
            j.KeyReceiptBefore=-1;j.KeyBaselineAt=DateTime.MinValue;j.KeyReceiptLastCount=-1;j.KeyReceiptReadError="";
            j.ReceiveFlowActive=true;j.TrainEnabled=false;j.TravelActive=true;j.TravelReady=false;j.TravelMapWaiting=false;j.TravelPhase=1;j.TravelAt=DateTime.UtcNow;
            r.Write(data,BitConverter.GetBytes(12));r.Write(j.TravelData+64,new byte[4]);r.Write(j.TravelData+4,BitConverter.GetBytes(168));r.Write(j.TravelData+8,new byte[4]);r.Write(j.TravelData+12,BitConverter.GetBytes(1));r.Write(j.TravelData,BitConverter.GetBytes(1));
            timer.Restart();j.Stage="travel";j.State="using_travel_item";Log(j,"travel_start",",\"template_id\":168");
        }
        static bool SandNpcReady(Remote r,long game){
            long cls=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(cls+0xb8)+16);if(fc==0||r.Ptr(fc)!=cls)return false;
            long npcClass=ResolvedClass(r,game+0x16ee910);foreach(long npc in ReadVector(r,r.Ptr(fc+0x158))){if(npc==0||r.Ptr(npc)!=npcClass)continue;if(BitConverter.ToInt16(r.Read(npc+80,2),0)==59)return true;}return false;
        }
        internal static void PumpTravel(Remote r,long data,NativeJob j,Stopwatch timer,long game,Func<bool> arrived=null){
            if(!j.TravelActive)return;
            string error="";int phase=j.TravelPhase;
            if(j.CancelRequested||j.CloseRequested||(j.NpcAction==3?!j.BeastQuestEnabled:(j.NpcAction==2?!j.IzanamiEnabled:!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled))){CancelTravel(r,j);j.ReceiveFlowActive=false;r.Write(data,BitConverter.GetBytes(12));timer.Stop();return;}
            if(phase==1&&BitConverter.ToInt32(r.Read(j.TravelData,4),0)==0){
                int result=BitConverter.ToInt32(r.Read(j.TravelData+8,4),0);
                if(result==2){j.TravelPhase=2;j.TravelAt=DateTime.UtcNow;r.Write(j.TravelData+64,BitConverter.GetBytes(2));j.State="choosing_village";Log(j,"travel_item_requested",",\"template_id\":168,\"slot\":"+BitConverter.ToInt32(r.Read(j.TravelData+16,4),0));}
                else if(result==3)error="travel_item_missing";else if(result==4)error="travel_slot_changed";else error="travel_item_not_ready";
            }else if(phase==2||phase==3){
                int menu=BitConverter.ToInt32(r.Read(j.TravelData+64,4),0);
                if(menu==3&&phase==2){j.TravelPhase=3;j.TravelAt=DateTime.UtcNow;j.State="choosing_sand_village";Log(j,"travel_village_selected","");}
                if(menu==4){j.TravelPhase=5;j.TravelAt=DateTime.UtcNow;r.Write(j.TravelData+12,new byte[4]);r.Write(data,BitConverter.GetBytes(13));timer.Restart();j.State="waiting_sand_village";Log(j,"travel_sand_selected","");}
            }else if(phase==5){
                int state=BitConverter.ToInt32(r.Read(data,4),0);
                if(state==14){if(!j.TravelMapWaiting){j.TravelMapWaiting=true;timer.Restart();}else if(timer.ElapsedMilliseconds>=5000)r.Write(data,BitConverter.GetBytes(15));}
                else if(state==16){bool ready=false;try{ready=arrived==null?(j.NpcAction==3?ReadDungeonMap(r,game)==59&&SandNpcReady(r,game):SandNpcReady(r,game)):arrived();}catch{}if(ready){j.TravelActive=false;j.TravelReady=true;j.TravelPhase=0;r.Write(j.TravelData+64,new byte[4]);Log(j,"travel_arrival_confirmed","");
                    if(j.NpcAction==3){CompleteBeastStartupSand(r,data,j,timer);return;}
                    if(j.NpcAction==2){j.IzanamiTravelReady=true;j.ReceiveFlowActive=false;j.IzanamiRequested=IsIzanamiLeader(j);j.State="opened";j.Stage="izanami_wait_team";j.TeamStatus="Đã về Làng Cát; đợi tổ đội cùng khu";r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"izanami_travel_ready","");return;}
                    BeginNpcSearch(r,data,j,timer,game);return;}}
            }
            if(error.Length==0&&(DateTime.UtcNow-j.TravelAt).TotalSeconds>=30)error=phase==2?"travel_village_menu_not_ready":phase==3?"travel_sand_menu_not_ready":"travel_arrival_not_confirmed";
            if(error.Length>0){if(j.NpcAction==2){j.IzanamiTravelStopped=true;j.IzanamiRequested=false;}CancelTravel(r,j);j.ReceiveFlowActive=false;j.ReceiveKeyRequested=false;j.DungeonStartAt=DateTime.MinValue;j.Error=error;j.State=j.NpcAction==2?"izanami_unavailable":"npc_unavailable";j.Stage="travel_stopped";r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"travel_stopped",",\"reason\":"+Quote(error));if(j.NpcAction==3){j.BeastStartupSandStopped=true;j.QuestStatus="Linh thú: chưa phù về Làng Cát • "+error;}}
        }
        internal static bool CanSkipDungeonReturnTravel(NativeJob j){return j.NpcAction==1&&j.DungeonHighEnabled&&!j.DungeonCycleWaiting;}
        internal static void BeginNpcSearch(Remote r,long data,NativeJob j,Stopwatch timer,long game=0,Func<InventorySnapshot> reader=null,Func<bool> returnedToSand=null){
            if(j.TravelData!=0&&!j.TravelReady&&CanSkipDungeonReturnTravel(j)){
                bool inSand=false;try{inSand=returnedToSand!=null?returnedToSand():ReadDungeonMap(r,game)==59&&SandNpcReady(r,game);}catch{}
                if(inSand){j.TravelReady=true;Log(j,"travel_skip_dungeon_return",",\"reason\":\"verified_sand_map_and_raikage\"");}
            }
            if(j.TravelData!=0&&!j.TravelReady){BeginTravel(r,data,j,timer);return;}
            if(j.NpcAction==0&&(j.CloseAfterKey||j.DailyActive&&j.DailyStage==1)&&j.KeyReceiptBefore<0){
                if(j.KeyBaselineAt==DateTime.MinValue)j.KeyBaselineAt=DateTime.UtcNow;
                try{j.KeyReceiptBefore=reader==null?ReadReceiptKeyCount(r,game):CountReceiptKeys(reader());j.KeyNoticeBefore=ReadKeyNotice(r,game);j.KeyNotificationSeen=new HashSet<string>(ReadKeyNotifications(r,game).Keys);Log(j,"key_receipt_baseline",",\"count\":"+j.KeyReceiptBefore);}
                catch(Exception e){j.State="waiting_key_baseline";if(j.KeyReceiptReadError!=e.Message){j.KeyReceiptReadError=e.Message;Log(j,"key_baseline_read_error",",\"reason\":"+Quote(e.Message));}if((DateTime.UtcNow-j.KeyBaselineAt).TotalSeconds>20){j.ReceiveFlowActive=false;j.State="npc_unavailable";j.Error="key_baseline_unavailable";r.Write(data,BitConverter.GetBytes(12));timer.Stop();}return;}
            }
            if(j.NpcAction==1&&j.DungeonStopped){StopDungeon(r,data,j,timer,j.Error.Length==0?"dungeon_no_keys":j.Error);return;}
            if(j.NpcAction==1&&!j.DungeonKeyChecked){
                j.ReceiveFlowActive=true;j.TrainEnabled=false;r.Write(data+3292,new byte[4]);r.Write(data,BitConverter.GetBytes(22));timer.Restart();j.Stage="dungeon_keys";j.State="checking_dungeon_keys";return;
            }
            if(j.NpcAction==2&&!IzanamiEntryTeamReady(j)){j.IzanamiRequested=true;j.ReceiveFlowActive=false;r.Write(data,BitConverter.GetBytes(12));timer.Reset();j.State="opened";return;}
            if((j.NpcAction==1||j.NpcAction==2)&&!j.PartyReady){
                j.ReceiveFlowActive=true;j.TrainEnabled=false;r.Write(data+3284,new byte[8]);r.Write(data,BitConverter.GetBytes(18));
                timer.Restart();j.Stage="party";j.State="creating_party";Log(j,"party_prepare","");return;
            }
            if(j.NpcAction==1){j.DungeonProbeRefused=false;try{j.DungeonProbePopupBefore=ReadKeyNotice(r,game);j.DungeonProbeNotices=new HashSet<string>(ReadKeyNotifications(r,game).Keys);}catch{j.DungeonProbeNotices=new HashSet<string>();}}
            j.ReceiveFlowActive=true;j.TrainEnabled=false;j.NpcId=-1;j.NpcTemplateId=-1;
            r.Write(data+3280,BitConverter.GetBytes(j.SonCapEnabled&&j.NpcAction==2?3:j.NpcAction));
            r.Write(data+28,BitConverter.GetBytes(-1));r.Write(data+32,new byte[4]);
            r.Write(data,BitConverter.GetBytes(8));timer.Restart();j.Stage="npc";j.State=j.NpcAction==2?"finding_onoki":"finding_npc";Log(j,"npc_search_start","");
        }
        internal static bool ResumeHighDungeon(Remote r,long data,NativeJob j,Stopwatch timer,int map){
            if(!j.DungeonHighEnabled||j.DungeonCycleWaiting||map!=18||j.CloseRequested||j.CancelRequested)return false;
            j.DungeonOriginMap=59;j.DungeonCycleWaiting=true;j.DungeonLeftOrigin=true;j.DungeonReturnConfirmed=false;j.DungeonResumeProbe=false;j.DungeonResumeChecked=true;j.DungeonFinished=false;j.DungeonStopped=false;
            j.ReceiveKeyRequested=false;j.ReceiveFlowActive=false;j.DungeonRepeatRequested=false;j.DungeonStartAt=DateTime.MinValue;j.KeyReceiptPending=false;
            j.DungeonEntries=Math.Max(1,j.DungeonEntries);j.DungeonCycleAt=DateTime.UtcNow;j.DungeonReturnAt=DateTime.MinValue;j.DungeonPollAt=DateTime.MinValue;
            PrepareEntryAuto(j,1,59);SubmitEntryAuto(j);ObserveEntryAuto(j,map,DateTime.UtcNow);j.State="dungeon_running";j.Stage="dungeon_wait_return";j.Error="";
            r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"dungeon_resumed",",\"map\":"+map);return true;
        }
        internal static void PumpReceiveKey(Remote r,long data,NativeJob j,Stopwatch timer,long game=0,Func<InventorySnapshot> inventoryReader=null,Func<int> mapReader=null){
            if(j.BeastQuestEnabled&&(!j.BeastStartupSandReady||j.BeastStartupSandPid!=j.ProcessId)){PumpBeastStartupSand(r,data,j,timer,game);return;}
            if(j.DungeonHighEnabled&&j.ReceiveKeyRequested&&!j.ReceiveFlowActive&&!j.DungeonCycleWaiting){try{if(ResumeHighDungeon(r,data,j,timer,mapReader==null?ReadDungeonMap(r,game):mapReader()))return;}catch{return;}}
            if(j.KeyReceiptPending){PumpKeyReceipt(r,data,j,timer,game,inventoryReader);return;}
            if(j.QuestEnabled&&!j.ReceiveFlowActive)return;
            bool disabled=(j.NpcAction==2&&!j.IzanamiEnabled)||(j.NpcAction==1&&!j.DungeonHighEnabled)||(j.NpcAction==0&&!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled);
            if(timer.IsRunning&&disabled){j.ReceiveFlowActive=false;CancelTravel(r,j);r.Write(data,BitConverter.GetBytes(12));timer.Reset();j.DungeonStartAt=DateTime.MinValue;j.State="opened";j.Error="";return;}
            if(j.TravelActive){PumpTravel(r,data,j,timer,game);return;}
            if(j.IzanamiEnabled&&j.IzanamiRequested&&IsIzanamiLeader(j)&&IzanamiEntryTeamReady(j)&&!j.ReceiveFlowActive&&!j.DungeonCycleWaiting&&!j.CancelRequested){
                j.IzanamiPopupBefore=ReadKeyNotice(r,game);j.IzanamiNoticeSeen=new HashSet<string>(ReadKeyNotifications(r,game).Keys);j.IzanamiLimitArmed=true;j.IzanamiNoticeAt=DateTime.UtcNow;j.IzanamiNoticePollAt=DateTime.MinValue;PrepareIzanamiAuto(j);j.IzanamiRequested=false;j.NpcAction=2;j.PartyReady=true;j.ReceiveFlowActive=true;j.TrainEnabled=false;j.NpcId=-1;j.NpcTemplateId=-1;j.Error="";
                r.Write(data+28,BitConverter.GetBytes(-1));r.Write(data,BitConverter.GetBytes(13));timer.Restart();j.Stage="popup";j.State="waiting_map";Log(j,"izanami_requested","");return;
            }
            if(j.ReceiveKeyRequested&&!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled){j.ReceiveKeyRequested=false;}
            if(j.ReceiveKeyRequested&&(!j.ReceiveFlowActive||j.NpcAction!=2)){j.ReceiveKeyRequested=false;j.TravelReady=false;j.NpcAction=j.DungeonRepeatRequested?1:j.ReceiveKeyEnabled?0:1;j.DungeonRepeatRequested=false;j.DungeonKeyChecked=false;j.PartyReady=false;j.DungeonStartAt=DateTime.MinValue;j.ReceiveFlowActive=true;j.TrainEnabled=false;j.NpcId=-1;j.NpcTemplateId=-1;j.Error="";r.Write(data+28,BitConverter.GetBytes(-1));r.Write(data,BitConverter.GetBytes(13));timer.Restart();j.Stage="popup";j.State="waiting_map";Log(j,"receive_key_requested","");return;}
            if(!timer.IsRunning)return;
            if(j.DungeonStartAt!=DateTime.MinValue){
                if(!j.DungeonHighEnabled){j.DungeonStartAt=DateTime.MinValue;j.ReceiveFlowActive=false;timer.Stop();j.State="key_submitted";return;}
                if(DateTime.UtcNow>=j.DungeonStartAt){
                    j.DungeonStartAt=DateTime.MinValue;j.NpcAction=1;
                    // Clear an ordinary key-receipt confirmation if one appeared.
                    // The map's five-second initial wait has already completed.
                    r.Write(data,BitConverter.GetBytes(15));timer.Restart();j.Stage="popup";j.State="confirming_popup";
                }return;
            }
            int state=BitConverter.ToInt32(r.Read(data,4),0);
            if(state==22){
                j.State="checking_dungeon_keys";if(CheckDungeonKeys(r,data,j,timer,game,inventoryReader))BeginNpcSearch(r,data,j,timer,game,inventoryReader);return;
            }
            if(state==21){
                j.State="checking_dungeon_keys";
                if(CheckDungeonKeys(r,data,j,timer,game,inventoryReader)){
                    try{j.DungeonOriginMap=mapReader==null?ReadDungeonMap(r,game):mapReader();}catch{StopDungeon(r,data,j,timer,"dungeon_map_unavailable");return;}
                    r.Write(data+3292,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(9));timer.Restart();j.State="choosing_dungeon";
                }return;
            }
            if(state==18||state==19){
                j.State="creating_party";j.PartyCreateAttempts=BitConverter.ToInt32(r.Read(data+3284,4),0);j.PartyMembers=BitConverter.ToInt32(r.Read(data+3288,4),0);
                if(timer.ElapsedMilliseconds>=30000){j.ReceiveFlowActive=false;j.Error="party_create_not_confirmed";j.State=j.NpcAction==2?"izanami_unavailable":"npc_unavailable";r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"party_timeout","");}return;
            }
            if(state==20){
                j.PartyReady=true;j.PartyMembers=BitConverter.ToInt32(r.Read(data+3288,4),0);j.PartyCreateAttempts=BitConverter.ToInt32(r.Read(data+3284,4),0);
                Log(j,"party_ready",",\"members\":"+j.PartyMembers+",\"create_attempts\":"+j.PartyCreateAttempts);
                // A create-group reply may have left an ordinary confirmation.
                r.Write(data,BitConverter.GetBytes(15));j.State="confirming_popup";j.Stage="popup";timer.Restart();return;
            }
            if(state==14&&j.State!="waiting_popup"){timer.Restart();j.State="waiting_popup";}
            else if(state==14&&timer.ElapsedMilliseconds>=5000){r.Write(data,BitConverter.GetBytes(15));j.State="confirming_popup";timer.Restart();}
            else if(state==16){BeginNpcSearch(r,data,j,timer,game,inventoryReader);}
            else if(state==8&&j.NpcId<0)SelectNpcId(r,data,j);
        }
        public static void ReplayCapturedNpc(string id){NativeJob j=Get(id);if(j==null||j.CloseRequested||j.ProcessId==0||j.State=="failed"||j.State=="closed")throw new Exception("npc_session_unavailable");
            if(j.CapturedNpcId<0||!IsRaikage(j.CapturedNpcName,j.CapturedTemplateId))throw new Exception("npc_id_not_captured: chọn Raikage trong game để bắt ID trước.");j.ReceiveKeyEnabled=true;j.NpcAction=0;j.DungeonStartAt=DateTime.MinValue;j.ReceiveKeyRequested=false;j.TrainEnabled=false;j.CancelRequested=false;j.ReplayRequested=true;}
        static void Replay(Remote r,long data,NativeJob j){r.Write(data+3280,new byte[4]);j.ReplayRequested=false;j.NpcId=j.CapturedNpcId;j.NpcTemplateId=j.CapturedTemplateId;r.Write(data+36,BitConverter.GetBytes(j.NpcTemplateId));r.Write(data+28,BitConverter.GetBytes(j.NpcId));r.Write(data,BitConverter.GetBytes(8));j.Error="";j.State="finding_npc";Log(j,"npc_replay",",\"npc_id\":"+j.NpcId+",\"template_id\":"+j.NpcTemplateId);}
        const string GameHash="d707ad643f8870f14a7f29fc5c872f4f2c99c95275198ebc9a8434fb668474c4";
        const string BaseHash="1e35be4c04d748071d98247c199eaa6ddfcbd297e3de96842c27576777a691fd";
        static readonly ConcurrentDictionary<string,NativeJob> jobs=new ConcurrentDictionary<string,NativeJob>();
        static readonly LaunchQueue queue=new LaunchQueue(6,10000,Work);
        static readonly FileHashCache buildHashes=new FileHashCache();
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto){return Enqueue(target,username,password,proxy,auto,true);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey){return Enqueue(target,username,password,proxy,auto,receiveKey,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh){return Enqueue(target,username,password,proxy,auto,receiveKey,dungeonHigh,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh,bool izanami){return Enqueue(target,username,password,proxy,auto,receiveKey,dungeonHigh,izanami,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh,bool izanami,bool queueOnly){return Enqueue(target,username,password,proxy,auto,receiveKey,dungeonHigh,izanami,queueOnly,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh,bool izanami,bool queueOnly,bool questQueueOnly){return Enqueue(target,username,password,proxy,auto,receiveKey,dungeonHigh,izanami,queueOnly,questQueueOnly,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh,bool izanami,bool queueOnly,bool questQueueOnly,bool mailQueueOnly){return Enqueue(target,username,password,proxy,auto,receiveKey,dungeonHigh,izanami,queueOnly,questQueueOnly,mailQueueOnly,false);}
        public static NativeJob Enqueue(string target,string username,string password,SocksConfig proxy,bool auto,bool receiveKey,bool dungeonHigh,bool izanami,bool queueOnly,bool questQueueOnly,bool mailQueueOnly,bool cloakQueueOnly){
            if(mailQueueOnly||cloakQueueOnly){receiveKey=false;dungeonHigh=false;izanami=false;queueOnly=false;questQueueOnly=false;}
            if(IntPtr.Size!=8)throw new Exception("native_x64_required");if(!File.Exists(target)||Path.GetFileName(target)!="TheGioiLangLa.exe")throw new Exception("game_invalid");
            if(String.IsNullOrWhiteSpace(username)||String.IsNullOrEmpty(password)||username.Length>128||password.Length>128||username.IndexOf('\0')>=0||password.IndexOf('\0')>=0)throw new Exception("login_length_invalid");
            NativeJob j=new NativeJob(target,username,password,proxy,auto);j.MailQueueOnly=mailQueueOnly;j.CloakQueueOnly=cloakQueueOnly;j.IzanamiQueueOnly=queueOnly&&!questQueueOnly;j.QuestQueueOnly=questQueueOnly;j.ReceiveKeyEnabled=receiveKey&&!queueOnly&&!questQueueOnly&&!mailQueueOnly&&!cloakQueueOnly;j.DungeonHighEnabled=dungeonHigh&&!queueOnly&&!questQueueOnly&&!mailQueueOnly&&!cloakQueueOnly;j.IzanamiRoundsTarget=Math.Max(1,IzanamiRoundLimit);j.IzanamiEnabled=izanami&&!questQueueOnly&&!mailQueueOnly&&!cloakQueueOnly;j.IzanamiRequested=j.IzanamiEnabled;if(questQueueOnly){j.QuestEnabled=true;j.QuestPauseAuto=true;j.QuestAt=DateTime.UtcNow;}jobs[j.Id]=j;if(!queue.TryAdd(j)){NativeJob ignored;jobs.TryRemove(j.Id,out ignored);j.Password=null;throw new Exception("login_queue_full");}return j;
        }
        public static NativeJob Get(string id){NativeJob j;return id!=null&&jobs.TryGetValue(id,out j)?j:null;}
        public static void CancelAll(){foreach(NativeJob j in jobs.Values)if(j.State!="opened"&&j.State!="key_submitted"&&j.State!="dungeon_submitted"&&j.State!="izanami_submitted"&&j.State!="izanami_unavailable"&&j.State!="npc_unavailable"&&j.State!="manual_entry"&&j.State!="closed")j.CancelRequested=true;}
        public static CloseGamesResult CloseGame(string id){NativeJob j=Get(id);if(j==null)throw new Exception("game_session_unavailable");j.CancelRequested=true;j.CloseRequested=true;j.QuestEnabled=false;j.AutoItemEnabled=false;j.TrainEnabled=false;j.GameAutoWanted=false;CloseGamesResult result=ManagedGames.CloseOne(j.ProcessId);if(result.Failed==0){j.State="closed";j.Error="";}return result;}
        public static CloseBatchOperation BeginCloseBatch(string[] ids){
            string[] copy=ids==null?new string[0]:(string[])ids.Clone();var operation=new CloseBatchOperation();
            var worker=new Thread(delegate(){try{foreach(string id in copy){try{operation.Failed+=CloseGame(id).Failed;}catch{operation.Failed++;}}}catch{operation.Error="batch_close_failed";}finally{operation.Completed=true;}});
            worker.IsBackground=true;worker.Start();return operation;
        }
        static NativeJob[] StopAllForClose(){var snapshot=jobs.Values.ToArray();foreach(NativeJob j in snapshot){j.CancelRequested=true;j.CloseRequested=true;j.TrainEnabled=false;j.GameAutoWanted=false;j.State="closing";}return snapshot;}
        static CloseGamesResult CloseStoppedGames(NativeJob[] snapshot){var result=ManagedGames.CloseAll();foreach(NativeJob j in snapshot){if(j.ProcessId<=0||!ManagedGames.IsRunning(j.ProcessId)){j.State="closed";j.Error="";}else{j.State="close_failed";j.Error="game_close_failed";}}return result;}
        public static CloseGamesOperation BeginCloseAllGames(){var snapshot=StopAllForClose();return CloseGamesOperation.Start(delegate(){return CloseStoppedGames(snapshot);});}
        public static CloseGamesResult CloseAllGames(){return CloseStoppedGames(StopAllForClose());}
        static void Check(NativeJob j){if(j.CancelRequested)throw new OperationCanceledException();}
        static string Hash(string path){return buildHashes.Read(path);}
        static string FailureCode(Exception e){string code=e.Message;return code.StartsWith("native_")||code.StartsWith("proxy_")||code=="game_exited"||code=="character_screen_not_ready"||code=="startup_popup_not_ready"?code:"native_operation_failed";}
        // Timeout chọn nhân vật phải qua cleanup trước khi mở lại, không giữ ở manual_entry.
        internal static void CheckCharacterWait(NativeJob j,bool selected){
            Check(j);
            if(j.Error=="game_exited")throw new Exception("game_exited");
            if(!selected&&(j.Error.Length==0||j.Error=="character_screen_not_ready"))throw new Exception("character_screen_not_ready");
        }
        internal static void CheckStartupMapWait(NativeJob j,bool ready){
            Check(j);
            if(j.Error=="game_exited")throw new Exception("game_exited");
            if(!ready)throw new Exception("startup_popup_not_ready");
        }
        internal static bool RecoverableSessionError(string error){
            return error=="character_screen_not_ready"||error=="startup_popup_not_ready"||error=="native_login_not_ready"||error=="game_exited";
        }
        public static void RecoverStalledSession(string id){
            NativeJob j=Get(id);if(j==null)return;
            lock(j){
                if(j.CloseRequested||j.CancelRequested||j.ReconnectRequested||!RecoverableSessionError(j.Error))return;
                if(j.State!="manual_entry"&&j.State!="startup_unavailable"&&j.State!="npc_unavailable")return;
                j.ReconnectRequested=true;j.State="reconnecting";
            }
            BeginSessionRecovery(j,j.Error);
        }
        internal static bool RetryStartup(string stage,Exception e){
            if(stage=="character"&&e.Message=="character_screen_not_ready"||stage=="popup"&&e.Message=="startup_popup_not_ready"||(stage=="character"||stage=="popup")&&e.Message=="game_exited")return true;
            if(e.Message=="native_login_not_ready"||e.Message=="native_wrong_process"||e.Message=="native_operation_failed")return true;
            if(stage!="start_game"&&stage!="register_game"&&stage!="load_modules"&&stage!="install_hooks")return false;
            if(e.Message=="game_exited"||e.Message=="native_process_not_ready"||e.Message=="native_module_not_found")return true;
            if(e is NullReferenceException)return true;
            System.ComponentModel.Win32Exception win=e as System.ComponentModel.Win32Exception;
            return win!=null&&(win.NativeErrorCode==6||win.NativeErrorCode==299||win.NativeErrorCode==8||win.NativeErrorCode==14||win.NativeErrorCode==1450);
        }
        static void WaitRetry(NativeJob j,int delay){
            Stopwatch timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<delay){Check(j);Thread.Sleep(50);}Check(j);
        }
        static readonly LaunchPacer launchPacer=new LaunchPacer();
        public static void SetLaunchDelayMs(int milliseconds){launchPacer.SetDelay(milliseconds);}
        public static void SetLaunchConcurrency(int count){queue.SetConcurrency(count);}
        public static NativeJob EnqueueManual(ProcessStartInfo info,string target,string username){if(info==null)throw new ArgumentNullException("info");NativeJob j=new NativeJob(target,username,null,null,false);j.ManualLaunch=info;jobs[j.Id]=j;if(!queue.TryAdd(j)){NativeJob ignored;jobs.TryRemove(j.Id,out ignored);throw new Exception("login_queue_full");}return j;}
        static void RunManual(NativeJob j){j.State="starting";j.Stage="start_game";launchPacer.Wait(delegate{Check(j);});Process p=null;try{Check(j);p=Process.Start(j.ManualLaunch);if(p==null)throw new Exception("game_exited");j.ProcessId=p.Id;j.Stage="register_game";ManagedGames.Register(p,j.Target);Check(j);j.State="opened";j.Error="";}catch{if(p!=null)try{if(!p.HasExited){p.Kill();p.WaitForExit(3000);}}catch{}throw;}finally{if(p!=null)p.Dispose();}}
        static void Work(NativeJob j){ExecuteLaunch(j,j.ManualLaunch==null?(Action<NativeJob>)Run:RunManual,WaitRetry);}
        internal static void ExecuteLaunch(NativeJob j,Action<NativeJob> launch,Action<NativeJob,int> wait){
            try{
                for(int attempt=1;attempt<=3;attempt++){
                    Check(j);j.StartAttempts=attempt;j.Error="";
                    try{launch(j);break;}
                    catch(Exception e){
                        if(attempt>=3||j.CancelRequested||j.CloseRequested||!RetryStartup(j.Stage,e))throw;
                        // Run has closed the failed process before returning here.
                        j.ProcessId=0;j.ReadyPhase=0;j.TrainReady=false;j.GameSettingsReady=false;
                        j.Error=FailureCode(e);j.State="retrying";j.StartAttempts=attempt+1;
                        Log(j,"startup_retry",",\"attempt_failed\":"+attempt+",\"next_attempt\":"+(attempt+1)+",\"stage\":"+Quote(j.Stage)+",\"error\":"+Quote(j.Error));
                        wait(j,attempt*1000);
                    }
                }
            }catch(OperationCanceledException){j.Error="cancelled";j.State="cancelled";}
            catch(Exception e){j.Error=FailureCode(e);j.State="failed";System.ComponentModel.Win32Exception win=e as System.ComponentModel.Win32Exception;Log(j,"failure",",\"attempts\":"+j.StartAttempts+",\"stage\":"+Quote(j.Stage)+",\"error\":"+Quote(j.Error)+",\"exception_type\":"+Quote(e.GetType().Name)+",\"stack\":"+Quote(e.StackTrace)+",\"win32_code\":"+(win==null?0:win.NativeErrorCode));}
            finally{j.Password=null;if(j.CloseRequested){j.Error="";j.State="closed";}}
        }
        static ProcessModule Module(Process p,string name){
            try{p.Refresh();foreach(ProcessModule m in p.Modules)if(m!=null&&String.Equals(m.ModuleName,name,StringComparison.OrdinalIgnoreCase))return m;}
            catch(NullReferenceException){} // Loader snapshot incomplete; next poll retries.
            catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode!=299&&e.NativeErrorCode!=6)throw;}
            return null;
        }
        internal static bool SelectNpcId(Remote r,long data,NativeJob job){
            int generation=BitConverter.ToInt32(r.Read(data+32,4),0);if((generation&1)!=0||generation==0)return false;
            int count=Math.Max(0,Math.Min(128,BitConverter.ToInt32(r.Read(data+24,4),0)));
            byte[] entries=r.Read(data+64,count*16);
            if(BitConverter.ToInt32(r.Read(data+32,4),0)!=generation)return false;
            List<string> names=new List<string>();int selected=-1,template=-1,matches=0;
            for(int i=0;i<count;i++){
                long pointer=BitConverter.ToInt64(entries,i*16+8);if(pointer==0)continue;
                try{
                    int length=BitConverter.ToInt32(r.Read(pointer+16,4),0);if(length<1||length>256)continue;
                    string name=Encoding.Unicode.GetString(r.Read(pointer+20,length*2)).Normalize(NormalizationForm.FormC).Trim();
                    int id=BitConverter.ToInt32(entries,i*16),type=BitConverter.ToInt32(entries,i*16+4);
                    names.Add(id+"/"+type+": "+name);
                    if(id>=0&&(job.NpcAction==2?(job.SonCapEnabled?type==105&&String.Equals(name,"Fukasaku",StringComparison.OrdinalIgnoreCase):type==32&&String.Equals(name,"Onoki",StringComparison.OrdinalIgnoreCase)):IsRaikage(name,type))){selected=id;template=type;matches++;}
            }catch{continue;} // The map may have unloaded between frames; retry a fresh snapshot.
            }
            string detail=String.Join("; ",names.ToArray());job.NpcDetails=detail.Length>1000?detail.Substring(0,1000):detail;
            if(matches!=1)return false;
            job.NpcId=selected;job.NpcTemplateId=template;r.Write(data+36,BitConverter.GetBytes(template));r.Write(data+28,BitConverter.GetBytes(selected));return true;
        }
        static long SocketExport(Process p,string name){IntPtr local=LoadLibrary("ws2_32.dll");IntPtr proc=GetProcAddress(local,name);ProcessModule mod=Module(p,"ws2_32.dll");if(proc==IntPtr.Zero||mod==null)throw new Exception("native_exports_missing");return mod.BaseAddress.ToInt64()+proc.ToInt64()-local.ToInt64();}
        static void Run(NativeJob j){
            Stopwatch launchTimer=Stopwatch.StartNew();
            Log(j,"startup_begin","");
            j.Stage="verify_build";
            string dir=Path.GetDirectoryName(j.Target),assembly=Path.Combine(dir,"GameAssembly.dll"),baselib=Path.Combine(dir,"baselib.dll");
            if(!File.Exists(assembly)||Hash(assembly)!=GameHash||!File.Exists(baselib)||Hash(baselib)!=BaseHash)throw new Exception("native_build_unsupported");
            Log(j,"build_verified",",\"elapsed_ms\":"+launchTimer.ElapsedMilliseconds);
            j.State="starting";Check(j);if(j.Proxy!=null){j.Stage="proxy_auth";long authAt=launchTimer.ElapsedMilliseconds;using(TcpClient check=SocksRelay.Authenticate(j.Proxy)){}Check(j);Log(j,"proxy_verified",",\"duration_ms\":"+(launchTimer.ElapsedMilliseconds-authAt));}
            Process p=null;
            Remote r=null;SocksRelay relay=null;bool installed=false;long data=0,user=0,pass=0,trainData=0,questData=0,farmData=0,chestData=0,cloakData=0,marketData=0,itemData=0,giftData=0,mailData=0,inboxData=0,settingsData=0;
            try {
                j.Stage="start_game";
                long delayAt=launchTimer.ElapsedMilliseconds;
                launchPacer.Wait(delegate{Check(j);});
                Check(j);
                Log(j,"launch_delay_elapsed",",\"duration_ms\":"+(launchTimer.ElapsedMilliseconds-delayAt));
                p=Process.Start(new ProcessStartInfo(j.Target){WorkingDirectory=dir,UseShellExecute=false});
                if(p==null)throw new Exception("game_exited");
                j.ProcessId=p.Id;j.Stage="register_game";
                ManagedGames.Register(p,j.Target);Check(j);
                Log(j,"game_started",",\"elapsed_ms\":"+launchTimer.ElapsedMilliseconds);
                j.Stage="load_modules";Stopwatch timer=Stopwatch.StartNew();ProcessModule gm=null,bm=null;
                while(timer.ElapsedMilliseconds<30000){Check(j);if(p.HasExited)throw new Exception("game_exited");gm=Module(p,"GameAssembly.dll");bm=Module(p,"baselib.dll");if(gm!=null&&bm!=null&&Module(p,"ws2_32.dll")!=null)break;Thread.Sleep(100);}
                if(gm==null||bm==null)throw new Exception("native_module_not_found");Log(j,"modules_ready",",\"duration_ms\":"+timer.ElapsedMilliseconds);r=new Remote(p);long game=gm.BaseAddress.ToInt64(),baseModule=bm.BaseAddress.ToInt64();
                j.Stage="install_hooks";long originalConnect=r.Ptr(game+0x129e618),connectCode=0;
                if(j.Proxy!=null){long slots=r.Allocate(2048);relay=new SocksRelay(r,slots,j.Proxy);ushort networkPort=(ushort)IPAddress.HostToNetworkOrder((short)relay.Port);
                    connectCode=r.Allocate(4096);byte[] code=NativeCode.Connect(slots,networkPort,originalConnect,r.Ptr(game+0x129e5f0),r.Ptr(game+0x129e640),SocketExport(p,"WSAGetLastError"),SocketExport(p,"WSASetLastError"));r.Write(connectCode,code);r.Protect(connectCode,4096,0x20);
                }
                long teamData=0,teamCode=0,followCode=0,zoneCode=0;long update=game+0x24ddb0,trampoline=0,loginCode=0;byte[] patch=null;
                if(j.Auto){byte[] prefix=r.Read(update,16);byte[] expected={0x40,0x57,0x48,0x83,0xec,0x40,0x80,0x3d};for(int i=0;i<expected.Length;i++)if(prefix[i]!=expected[i])throw new Exception("native_build_unsupported");
                    if(prefix[12]!=0||prefix[13]!=0x48||prefix[14]!=0x8b||prefix[15]!=0xf9)throw new Exception("native_build_unsupported");
                    trampoline=r.Near(update,4096);byte[] relocated=(byte[])prefix.Clone();long comparison=update+13+BitConverter.ToInt32(prefix,8);Array.Copy(BitConverter.GetBytes(checked((int)(comparison-trampoline-13))),0,relocated,8,4);
                    Code64 tr=new Code64();tr.Emit(relocated);tr.Emit(0xff,0x25,0,0,0,0);tr.Q(update+16);r.Write(trampoline,tr.Finish());
                    data=r.Allocate(139264);user=r.Utf8(j.Username);pass=r.Utf8(j.Password);r.Write(data+8,BitConverter.GetBytes(user));r.Write(data+16,BitConverter.GetBytes(pass));
                    long partyCode=r.Allocate(4096);byte[] partyBytes=NativeCode.Party(data,game+0x16ee888,game+0x16f0240,game+0x16ee820,game+0x16f760,game+0x2e2aa0,game+0x1dd900);r.Write(partyCode,partyBytes);r.Protect(partyCode,4096,0x20);
                    long npcCode=r.Allocate(8192);byte[] npcBytes=NativeCode.Npc(data,game+0x16ed658,game+0x16ee888,game+0x16ee910,game+0x16efe08,game+0x4e19f0,game+0x2d8f00,game+0x12d9e0,game+0x1dd900,game+0x303250,partyCode);
                    if(npcBytes.Length>8192)throw new Exception("native_code_too_large");r.Write(npcCode,npcBytes);r.Protect(npcCode,8192,0x20);
                    long prepareCode=r.Allocate(4096);byte[] prepareBytes=NativeCode.PrepareMap(data,game+0x16ed658,game+0x16ee888,game+0x16eeba8,game+0x1dd900,npcCode);if(prepareBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(prepareCode,prepareBytes);r.Protect(prepareCode,4096,0x20);
                    long characterCode=trampoline+1024;r.Write(characterCode,NativeCode.Character(data,game+0x16ed658,game+0x16eeaf8,game+0x453fb0,prepareCode));
                    long interact=game+0x4e19f0;byte[] interactionPrefix=r.Read(interact,13),expectedInteraction={0x48,0x89,0x5c,0x24,0x08,0x57,0x48,0x83,0xec,0x40,0x48,0x8b,0xf9};
                    for(int n=0;n<13;n++)if(interactionPrefix[n]!=expectedInteraction[n])throw new Exception("native_build_unsupported");
                    long captureCode=r.Near(interact,4096);r.Write(captureCode,NativeCode.CaptureNpc(data,game+0x303250,interactionPrefix,interact+13));r.Protect(captureCode,4096,0x20);
                    byte[] capturePatch=new byte[13];for(int n=0;n<13;n++)capturePatch[n]=0x90;capturePatch[0]=0xe9;Array.Copy(BitConverter.GetBytes(checked((int)(captureCode-interact-5))),0,capturePatch,1,4);
                    r.Suspended(delegate{r.Install(interact,capturePatch);});
                    trainData=r.Allocate(4096);long trainCode=r.Allocate(4096);byte[] trainBytes=NativeCode.Train(trainData,game+0x16ed658,game+0x16ee888,game+0x16ee330,game+TrainingMobSlotRva,game+0x37f020,game+0x3e7390,game+0x4f4d80,game+0x12d9e0,game+0x4ce5f0,game+0x3760d0);if(trainBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(trainCode,trainBytes);r.Protect(trainCode,4096,0x20);
                    farmData=r.Allocate(4096);long portalKeyCode=r.Allocate(4096);r.Write(portalKeyCode,NativeCode.PortalKey(farmData,game+0x16ee888,game+0x299080,game+0x2ab040,game+0x2ab6a0,game+0xec4800));r.Protect(portalKeyCode,4096,0x20);long farmCode=r.Allocate(8192);byte[] farmBytes=NativeCode.Quest(farmData,game,portalKeyCode);r.Write(farmCode,farmBytes);r.Protect(farmCode,8192,0x20);
                    questData=r.Allocate(4096);long questCode=r.Allocate(8192);byte[] questBytes=NativeCode.Quest(questData,game);if(questBytes.Length>8192)throw new Exception("quest_code_too_large");r.Write(questCode,questBytes);r.Protect(questCode,8192,0x20);
                    itemData=r.Allocate(4096);long itemCode=r.Allocate(4096);byte[] itemBytes=NativeCode.AutoItem(itemData,game);r.Write(itemCode,itemBytes);r.Protect(itemCode,4096,0x20);
                    chestData=r.Allocate(4096);long chestCode=r.Allocate(4096);byte[] chestBytes=NativeCode.StoneChest(chestData,game);if(chestBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(chestCode,chestBytes);r.Protect(chestCode,4096,0x20);
                    cloakData=r.Allocate(4096);long cloakAction=r.Allocate(4096),cloakUse=r.Allocate(4096),cloakBuy=r.Allocate(4096),cloakEquip=r.Allocate(4096),cloakPacket=r.Allocate(4096),cloakCode=r.Allocate(4096);
                    byte[] cloakActions=NativeCode.CloakAction(cloakData,game);if(cloakActions.Length>4096)throw new Exception("native_code_too_large");r.Write(cloakAction,cloakActions);r.Write(cloakUse,NativeCode.AutoItem(cloakData+256,game));r.Write(cloakBuy,NativeCode.StoneChest(cloakData+512,game));r.Write(cloakPacket,NativeCode.EquipPacket(game));r.Write(cloakEquip,NativeCode.EquipItem(cloakData+768,game+0x16ed658,game+0x16ee888,game+0x3672c0,game+0x16ee578,cloakPacket));r.Write(cloakCode,NativeCode.CloakBundle(cloakData,game,cloakAction,cloakUse,cloakBuy,cloakEquip));foreach(long address in new[]{cloakAction,cloakUse,cloakBuy,cloakEquip,cloakPacket,cloakCode})r.Protect(address,4096,0x20);
                    giftData=r.Allocate(4096);long giftCode=r.Allocate(4096);byte[] giftBytes=NativeCode.GiftCode(giftData,game);r.Write(giftCode,giftBytes);r.Protect(giftCode,4096,0x20);
                    mailData=r.Allocate(4096);long mailCode=r.Allocate(4096);byte[] mailBytes=NativeCode.Mail(mailData,game);if(mailBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(mailCode,mailBytes);r.Protect(mailCode,4096,0x20);
                    marketData=r.Allocate(8192);long marketCode=r.Allocate(4096),marketSplit=r.Allocate(4096);r.Write(marketSplit,NativeCode.Mail(marketData+4096,game));r.Protect(marketSplit,4096,0x20);byte[] marketBytes=NativeCode.Market(marketData,game,marketSplit);if(marketBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(marketCode,marketBytes);r.Protect(marketCode,4096,0x20);
                    inboxData=r.Allocate(4096);long inboxCode=r.Allocate(4096);r.Write(inboxCode,NativeCode.Inbox(inboxData,game));r.Protect(inboxCode,4096,0x20);
                    long inboxResponse=game+0x4f0300;byte[] inboxPrefix=r.Read(inboxResponse,15);
                    if(NativeCode.InboxPrefixSupported(inboxPrefix)){
                        long inboxTrampoline=r.Near(inboxResponse,4096);Code64 it=new Code64();it.Emit(inboxPrefix);it.Emit(0xff,0x25,0,0,0,0);it.Q(inboxResponse+15);r.Write(inboxTrampoline,it.Finish());
                        long inboxAckCode=inboxTrampoline+128;r.Write(inboxAckCode,NativeCode.InboxListAck(inboxData,inboxTrampoline));r.Protect(inboxTrampoline,4096,0x20);
                        byte[] inboxPatch=new byte[15];for(int n=0;n<15;n++)inboxPatch[n]=0x90;inboxPatch[0]=0xe9;Array.Copy(BitConverter.GetBytes(checked((int)(inboxAckCode-inboxResponse-5))),0,inboxPatch,1,4);r.Suspended(delegate{r.Install(inboxResponse,inboxPatch);});j.InboxSupported=true;
                    }else{j.InboxSupported=false;j.InboxStatus="Nhận thư chưa hỗ trợ bản game này";Log(j,"inbox_hook_unavailable",",\"prefix\":"+Quote(BitConverter.ToString(inboxPrefix)));}
                    long mailResponse=game+0x4f2570;byte[] mailPrefix=r.Read(mailResponse,15),expectedMail={0x48,0x89,0x5c,0x24,8,0x48,0x89,0x74,0x24,16,0x57,0x48,0x83,0xec,0x70};
                    for(int n=0;n<15;n++)if(mailPrefix[n]!=expectedMail[n])throw new Exception("native_build_unsupported");
                    long mailTrampoline=r.Near(mailResponse,4096);Code64 mt=new Code64();mt.Emit(mailPrefix);mt.Emit(0xff,0x25,0,0,0,0);mt.Q(mailResponse+15);r.Write(mailTrampoline,mt.Finish());
                    long mailAckCode=mailTrampoline+128;r.Write(mailAckCode,NativeCode.MailAck(mailData,mailTrampoline));r.Protect(mailTrampoline,4096,0x20);
                    byte[] mailPatch=new byte[15];for(int n=0;n<15;n++)mailPatch[n]=0x90;mailPatch[0]=0xe9;Array.Copy(BitConverter.GetBytes(checked((int)(mailAckCode-mailResponse-5))),0,mailPatch,1,4);r.Suspended(delegate{r.Install(mailResponse,mailPatch);});
                    j.TravelData=r.Allocate(4096);long travelItemCode=r.Allocate(4096),travelMenuCode=r.Allocate(4096);byte[] travelUse=NativeCode.AutoItem(j.TravelData,game),travelMenu=NativeCode.TravelMenu(j.TravelData,game+0x16ee888,game+0x16efe08,game+0x2d8f00,game+0x1dd900);if(travelUse.Length>4096||travelMenu.Length>4096)throw new Exception("native_code_too_large");r.Write(travelItemCode,travelUse);r.Protect(travelItemCode,4096,0x20);r.Write(travelMenuCode,travelMenu);r.Protect(travelMenuCode,4096,0x20);
                    long moveReleaseCode=r.Allocate(4096);r.Write(moveReleaseCode,NativeCode.ReleaseMovement(game+0x299080,game+0xeed700,game+0x27bf70,game+0x16ee888,game+0x2ab6a0));r.Protect(moveReleaseCode,4096,0x20);settingsData=r.Allocate(4096);long settingsCode=r.Allocate(4096);byte[] settingsBytes=NativeCode.Settings(settingsData,game+0x16ed658,game+0x16ee888,game+0x3632a0,game+0x3610f0,game+0x3634b0,game+0x3621a0,game+0x16ee808,moveReleaseCode);r.Write(settingsCode,settingsBytes);r.Protect(settingsCode,4096,0x20);
                    teamData=r.Allocate(4096);teamCode=r.Allocate(4096);byte[] teamBytes=NativeCode.Team(teamData,game,game+0x1dd900);if(teamBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(teamCode,teamBytes);r.Protect(teamCode,4096,0x20);
                    followCode=r.Allocate(4096);byte[] followBytes=NativeCode.Follow(teamData+1024,game,game+0x1dd900);if(followBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(followCode,followBytes);r.Protect(followCode,4096,0x20);
                    zoneCode=r.Allocate(4096);byte[] zoneBytes=NativeCode.Zone(teamData+2048,game,game+0x1dd900);if(zoneBytes.Length>4096)throw new Exception("native_code_too_large");r.Write(zoneCode,zoneBytes);r.Protect(zoneCode,4096,0x20);
                    loginCode=trampoline+128;byte[] loginBytes=NativeCode.Login(data,game+0x16ee778,trampoline,game+0x161150,game+0x2fced0,game+0x2bc700,characterCode,trainCode,settingsCode,teamCode,followCode,zoneCode,itemCode,questCode,travelItemCode,travelMenuCode,giftCode,mailCode,inboxCode,farmCode,chestCode,cloakCode,marketCode);r.Write(loginCode,loginBytes);r.Protect(trampoline,4096,0x20);
                    patch=new byte[16];for(int i=0;i<16;i++)patch[i]=0x90;patch[0]=0xe9;Array.Copy(BitConverter.GetBytes(checked((int)(loginCode-update-5))),0,patch,1,4);
                }
                Check(j);r.Suspended(delegate{if(j.Proxy!=null){r.Install(game+0x129e618,BitConverter.GetBytes(connectCode));r.Install(baseModule+0x45448,BitConverter.GetBytes(connectCode));}if(j.Auto)r.Install(update,patch);});installed=true;
                if(j.Auto){r.Write(data,BitConverter.GetBytes(1));j.State="typing";timer.Restart();bool done=false;
                    j.Stage="login";while(timer.ElapsedMilliseconds<60000){Check(j);if(p.HasExited)throw new Exception("game_exited");byte[] progress=r.Read(data,8);j.ReadyPhase=BitConverter.ToInt32(progress,4);if(BitConverter.ToInt32(progress,0)==3){done=true;break;}Thread.Sleep(100);}
                    if(!done)throw new Exception("native_login_not_ready");j.State="submitted";Log(j,"login_submitted",",\"elapsed_ms\":"+launchTimer.ElapsedMilliseconds);
                    r.Write(data,new byte[4]);r.Write(user,new byte[Encoding.UTF8.GetByteCount(j.Username)+1]);r.Write(pass,new byte[Encoding.UTF8.GetByteCount(j.Password)+1]);
                    j.Stage="character";j.State="selecting_character";r.Write(data,BitConverter.GetBytes(4));timer.Restart();bool selected=false;
                    // Keep game/relay alive if server has not returned a character list.
                    while(timer.ElapsedMilliseconds<60000){
                        if(j.CancelRequested){j.Error="cancelled";break;}if(p.HasExited){j.Error="game_exited";break;}
                        byte[] progress=r.Read(data,8);int state=BitConverter.ToInt32(progress,0);j.ReadyPhase=BitConverter.ToInt32(progress,4);
                        if(state==6){selected=true;break;}if(state==7){j.Error="character_list_empty";break;}Thread.Sleep(100);
                    }
                    CheckCharacterWait(j,selected);
                    r.Write(data,new byte[4]);j.State=selected?"character_submitted":j.Error=="cancelled"?"cancelled":"manual_entry";
                    if(selected){
                        Log(j,"character_submitted",",\"elapsed_ms\":"+launchTimer.ElapsedMilliseconds);
                        j.Stage="popup";j.State="waiting_map";r.Write(data,BitConverter.GetBytes(13));timer.Restart();Stopwatch mapDelay=null;bool popupReady=false,popupLogged=false;
                        while(timer.ElapsedMilliseconds<90000){
                            if(j.CancelRequested){j.Error="cancelled";break;}if(p.HasExited){j.Error="game_exited";break;}
                            PumpCharacterIdentity(r,game,j);PumpThiQuyPriority(r,game,trainData,j);PumpTraining(r,trainData,j);PumpGameSettings(r,settingsData,j);byte[] gate=r.Read(data,8);int gateState=BitConverter.ToInt32(gate,0);j.ReadyPhase=BitConverter.ToInt32(gate,4);
                            if(gateState==14){if(mapDelay==null){mapDelay=Stopwatch.StartNew();j.State="waiting_popup";Log(j,"map_ready",",\"delay_ms\":5000");}if(mapDelay.ElapsedMilliseconds>=5000){Log(j,"popup_delay_elapsed",",\"elapsed_ms\":"+mapDelay.ElapsedMilliseconds);j.State="confirming_popup";r.Write(data,BitConverter.GetBytes(15));}}
                            if(!popupLogged&&BitConverter.ToInt32(r.Read(data+3264,4),0)>0){popupLogged=true;Log(j,"popup_confirmed",",\"label\":\"Xác nhận\"");}
                            if(gateState==16){popupReady=true;Log(j,"popup_clear","");break;}Thread.Sleep(100);
                        }
                        CheckStartupMapWait(j,popupReady);
                        if(popupReady&&j.DungeonHighEnabled&&ResumeHighDungeon(r,data,j,timer,ReadDungeonMap(r,game))){}
                        else if(popupReady&&(j.ReceiveKeyEnabled||j.DungeonHighEnabled)){j.ReceiveKeyRequested=false;j.NpcAction=j.ReceiveKeyEnabled?0:1;if(j.NpcAction==2)j.IzanamiRequested=false;BeginNpcSearch(r,data,j,timer,game);
                        }else{r.Write(data,BitConverter.GetBytes(12));j.State=popupReady&&!j.ReceiveKeyEnabled&&!j.DungeonHighEnabled?"opened":j.Error=="cancelled"?"cancelled":StartupPopupFailureState(j);}
                    }
                }else j.State="opened";
                // Retain the proxy relay independently of the job; keep QLTK running.
                if(data!=0&&!j.CancelRequested&&!j.ReceiveFlowActive)r.Write(data,BitConverter.GetBytes(12));
                Remote keep=r;SocksRelay keepRelay=relay;r=null;relay=null;
                Thread lifetime=new Thread(delegate(){Stopwatch maintenance=new Stopwatch();Stopwatch npcTimer=new Stopwatch();if(j.ReceiveFlowActive)npcTimer.Start();try{while(!p.HasExited){if(data!=0&&!j.CloseRequested){try{
                    if(!j.ReconnectRequested&&(DateTime.UtcNow-j.DisconnectPollAt).TotalSeconds>=1){j.DisconnectPollAt=DateTime.UtcNow;if(ObserveDisconnect(j,ReadDisconnectNotice(keep,game),j.DisconnectPollAt))BeginDisconnectRecovery(j);}
                    if(j.ReconnectRequested){Thread.Sleep(150);continue;}
                    if((DateTime.UtcNow-j.CursePollAt).TotalSeconds>=1){j.CursePollAt=DateTime.UtcNow;ObserveCurseNotice(j,ReadDisconnectNotice(keep,game),j.CursePollAt);}

                    if(j.CancelRequested&&npcTimer.IsRunning){CancelTravel(keep,j);j.ReceiveKeyEnabled=false;j.DungeonHighEnabled=false;j.IzanamiEnabled=false;j.IzanamiRequested=false;j.DungeonStartAt=DateTime.MinValue;j.ReceiveKeyRequested=false;j.ReplayRequested=false;j.ReceiveFlowActive=false;j.TrainEnabled=false;keep.Write(data,BitConverter.GetBytes(12));npcTimer.Reset();j.State="cancelled";j.Error="cancelled";}
                    if(!maintenance.IsRunning||maintenance.ElapsedMilliseconds>=150){PumpActivityRetry(keep,data,questData,j,npcTimer);PumpQuest(keep,questData,j);PumpGameSettings(keep,settingsData,j);}
                    PumpThiQuyPriority(keep,game,trainData,j);PumpTraining(keep,trainData,j);
                    if(!maintenance.IsRunning||maintenance.ElapsedMilliseconds>=150){maintenance.Restart();PumpMarket(keep,marketData,game,j);PumpVitals(keep,game,j);PumpFarm(keep,farmData,game,j);if(j.InboxEnabled||j.InboxBusy)PumpInbox(keep,inboxData,game,j);if(j.MailEnabled||j.MailBusy)PumpMail(keep,mailData,game,j);PumpSonCap(keep,game,farmData,j);PumpSonCapMapAuto(keep,game,data,settingsData,j,npcTimer);PumpIzanamiMapAuto(keep,game,data,j,npcTimer);PumpIzanamiTravel(keep,data,j,npcTimer);if(j.GiftEnabled||j.GiftWaiting||j.GiftResultWaiting)PumpGiftCodes(keep,giftData,game,j);PumpCloak(keep,cloakData,game,j);PumpStoneChest(keep,chestData,itemData,game,j);PumpAutoItem(keep,itemData,j);PumpInventory(keep,game,j);PumpBiMa(keep,game,j);PumpTeam(keep,game,teamData,j);PumpZones(keep,game,teamData+2048,j);PumpFollow(keep,game,teamData+1024,j);
                    ReadDiagnostics(keep,data,j);PumpIzanamiAttemptLimit(keep,game,j);PumpDungeonCycle(keep,data,game,j,npcTimer);PumpReceiveKey(keep,data,j,npcTimer,game);if(j.ReplayRequested){Replay(keep,data,j);npcTimer.Restart();}
                    if(npcTimer.IsRunning&&!j.TravelActive&&(j.ReceiveKeyEnabled||j.DungeonHighEnabled||j.IzanamiEnabled)){int state=BitConverter.ToInt32(keep.Read(data,4),0);if(state==9)j.State=j.NpcAction==2?"choosing_izanami":j.NpcAction==0?"choosing_key":"choosing_dungeon";
                        if(state==11){FinishNpcAction(keep,data,j,npcTimer);}
                        else if(npcTimer.ElapsedMilliseconds>60000){j.ReceiveFlowActive=false;j.Error=state==8?(j.NpcAction==2?"onoki_not_found":"npc_not_found"):state==13||state==14||state==15||state==17?"startup_popup_not_ready":(j.NpcAction==2?"npc_izanami_menu_not_ready":j.NpcAction==1?"npc_dungeon_menu_not_ready":"npc_key_menu_not_ready");j.State=j.NpcAction==2?"izanami_unavailable":"npc_unavailable";keep.Write(data,BitConverter.GetBytes(12));npcTimer.Stop();}}
                    }
                }catch(Exception e){if((DateTime.UtcNow-j.WorkerErrorAt).TotalSeconds>=5){j.WorkerErrorAt=DateTime.UtcNow;Log(j,"worker_error",",\"type\":"+Quote(e.GetType().Name));}}}Thread.Sleep(TrainingPollMs(j));}}catch{}finally{lock(j.MailGate){j.InboxEnabled=false;j.InboxBusy=false;j.InboxStatus="Game đã thoát";j.MailEnabled=false;j.MailStatus=j.MailBusy?"Game đã thoát khi đang gửi; kiểm tra thư trước khi chạy lại":"Game đã thoát";j.MailBusy=false;}RecordBiMaClosed(j,"process_exit");j.GiftEnabled=false;j.GiftStatus="Game đã thoát";j.QuestEnabled=false;j.QuestPauseAuto=false;j.QuestStatus="Game đã thoát";j.AutoItemEnabled=false;j.AutoItemStatus="Game đã thoát";j.CloakEnabled=false;j.CloakStatus=j.CloakPhase==8?"Game đã thoát khi mua; kiểm tra túi trước khi chạy lại":"Game đã thoát";j.ChestEnabled=false;j.ChestUseOwned=false;j.ChestStatus=j.ChestPhase==2?"Game đã thoát khi mua; kiểm tra túi trước khi chạy lại":"Game đã thoát";j.ThiQuyCombatActive=false;j.ThiQuyPriorityArmed=false;j.ThiQuyNativeMode=0;j.ThiQuyFarmPaused=false;j.ThiQuyReadSuspended=false;j.ThiQuyMissingAt=DateTime.MinValue;j.ThiQuyPriorityAt=DateTime.MinValue;j.CurseActive=false;j.CurseReadAt=DateTime.MinValue;j.ZoneSyncActive=false;j.MapId=-1;j.ZoneId=-1;j.ZoneReadAt=DateTime.MinValue;j.ZonePrepared=false;j.ZoneStatus="";j.FollowActive=false;j.FollowStatus="";j.Vitals=null;j.CharacterName="";j.TeamMembers=new string[0];j.TeamConfirmed=false;j.InventoryScanning=false;j.Inventory=new InventorySnapshot(new InventoryItem[0],0,false,"game_exited",DateTime.MinValue);j.FarmEnabled=false;j.FarmArrived=false;j.FarmStatus="Game đã thoát";j.GameSettingsReady=false;j.GameAutoEnabled=false;j.TrainReady=false;j.TrainEnabled=false;if(keepRelay!=null)keepRelay.Dispose();keep.Dispose();p.Dispose();}});lifetime.IsBackground=true;lifetime.Start();
            }catch{
                bool stopped=true;
                if(p!=null){try{if(!p.HasExited){p.Kill();stopped=p.WaitForExit(3000);}}catch{stopped=false;}finally{p.Dispose();}}
                if(!stopped)throw new Exception("native_cleanup_failed");
                throw;
            }
            finally{
                if(r!=null){if(data!=0)try{r.Write(data,new byte[4]);if(user!=0)r.Write(user,new byte[Encoding.UTF8.GetByteCount(j.Username)+1]);if(pass!=0)r.Write(pass,new byte[Encoding.UTF8.GetByteCount(j.Password)+1]);}catch{}if(relay!=null)relay.Dispose();r.Dispose();}
                else if(installed&&data!=0){ // Clear the remote credential staging immediately after submission.
                    try{using(Remote clear=new Remote(p)){clear.Write(user,new byte[Encoding.UTF8.GetByteCount(j.Username)+1]);clear.Write(pass,new byte[Encoding.UTF8.GetByteCount(j.Password)+1]);}}catch{}
                }
            }
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
    }
}



namespace TGLL {
    public sealed class FarmSpot {
        public int MapId {get;set;} public string MapName {get;set;} public int MobId {get;set;} public string MobName {get;set;} public int Level {get;set;} public int X {get;set;} public int Y {get;set;}
        public override string ToString(){return MapId+" • "+MapName+" • "+MobName+" cấp "+Level;}
    }
    public static partial class NativeBackend {
        // Chỉ bãi thường được dùng; loại trừ làng, phụ bản và map không có tên.
        internal static bool OrdinaryFarmMap(int id,int kind,string name){return kind==0&&!String.IsNullOrWhiteSpace(name)&&((id>=61&&id<=66)||(id>=70&&id<=83)||(id>=86&&id<=88)||(id>=103&&id<=106));}
        public static bool IsOrdinaryFarmMap(int id,int kind,string name){return OrdinaryFarmMap(id,kind,name);}
        internal static FarmSpot SelectFarmSpot(FarmSpot[] spots,int level,bool characterMode){
            FarmSpot best=null;foreach(FarmSpot s in spots??new FarmSpot[0]){if(s==null||s.Level<1||s.Level>500||s.MapId<0||s.X<0||s.Y<0)continue;if(characterMode&&s.Level>level)continue;
                int score=characterMode?-s.Level:Math.Abs(s.Level-level),prev=best==null?Int32.MaxValue:characterMode?-best.Level:Math.Abs(best.Level-level);
                if(best==null||score<prev||score==prev&&(s.Level<best.Level||s.Level==best.Level&&(s.MapId<best.MapId||s.MapId==best.MapId&&s.MobId<best.MobId)))best=s;
            }return best;
        }
        static bool FarmConflict(NativeJob j){return j.ReceiveKeyEnabled||j.ReceiveFlowActive||j.DungeonHighEnabled||j.IzanamiEnabled||j.QuestEnabled||j.MailEnabled||(j.MailQueueOnly||j.CloakQueueOnly)||j.DungeonQueueOnly||j.IzanamiQueueOnly||j.QuestQueueOnly;}
        public static void StartMapFarm(string id,bool characterMode,int level,FarmSpot[] spots,bool quietZone=false){
            NativeJob j=Get(id);if(j==null||!j.TrainReady||j.CloseRequested||j.CancelRequested)throw new Exception("Mở tài khoản và chờ nhân vật vào game trước.");
            if(FarmConflict(j))throw new Exception("Tắt auto nhận chìa, địa cung, cấm thuật, nhiệm vụ và gom đồ trước khi chạy bãi.");
            if(level<1||level>500)throw new Exception("Level phải từ 1 đến 500.");FarmSpot[] copied=(spots??new FarmSpot[0]).Where(delegate(FarmSpot s){return s!=null;}).Select(delegate(FarmSpot s){return new FarmSpot{MapId=s.MapId,MapName=s.MapName,MobId=s.MobId,MobName=s.MobName,Level=s.Level,X=s.X,Y=s.Y};}).ToArray();
            if(copied.Any(delegate(FarmSpot s){return !OrdinaryFarmMap(s.MapId,0,s.MapName)||s.X<0||s.Y<0||s.X>32767||s.Y>32767||s.Level<1||s.Level>500;}))throw new Exception("Bãi không hợp lệ; chỉ dùng map quái thường và tọa độ hợp lệ.");
            if(SelectFarmSpot(copied,characterMode?j.CharacterLevel:level,characterMode)==null)throw new Exception("Chưa có bãi phù hợp. Đọc và lưu bãi quái thường trước.");
            if(j.FarmEnabled)throw new Exception("Đang chạy bãi; dừng trước khi đổi cấu hình.");j.FarmQuietZone=quietZone;j.FarmZoneWaiting=false;j.ZonePrepared=false;j.ZonePlanMap=-1;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;j.FarmChoices=copied;j.FarmCharacterMode=characterMode;j.FarmLevel=level;j.FarmTarget=null;j.FarmZoneCheckAt=DateTime.MinValue;j.FarmRouteSent=false;j.FarmArrived=false;j.FarmStableAt=DateTime.MinValue;j.FarmStopPending=false;j.GameAutoManaged=true;j.GameAutoWanted=true;j.TrainEnabled=false;j.FarmStatus="Đang chọn bãi";j.FarmEnabled=true;j.UpRequested=true;Log(j,"farm_start",",\"character_mode\":"+(characterMode?"true":"false")+",\"level\":"+level);
        }
        // Lấy một ảnh chụp vị trí acc tham chiếu; không đọc game thêm khi bấm bắt đầu.
        public static FarmSpot GetReferenceFarmSpot(string id){
            NativeJob j=Get(id);if(j==null||!j.TrainReady||j.CloseRequested||j.CancelRequested)throw new Exception("Mở acc dòng 1 và chờ vào bãi quái trước.");
            DateTime at=j.FarmPositionReadAt;int map=j.FarmPositionMap,x=j.FarmPositionX,y=j.FarmPositionY;
            if(at==DateTime.MinValue||(DateTime.UtcNow-at).TotalSeconds>10)throw new Exception("Chờ cập nhật tọa độ acc dòng 1 rồi thử lại.");
            FarmSpot nearest=(j.FarmObserved??new FarmSpot[0]).Where(delegate(FarmSpot f){return f.MapId==map&&OrdinaryFarmMap(f.MapId,0,f.MapName);}).OrderBy(delegate(FarmSpot f){return Math.Abs(f.X-x)+Math.Abs(f.Y-y);}).FirstOrDefault();
            if(nearest==null)throw new Exception("Acc dòng 1 chưa ở bãi quái thường; không lấy tọa độ làng hoặc phụ bản.");
            if(at!=j.FarmPositionReadAt)throw new Exception("Vị trí acc dòng 1 đang cập nhật; thử lại.");
            return new FarmSpot{MapId=map,MapName=nearest.MapName,MobId=nearest.MobId,MobName=nearest.MobName,Level=nearest.Level,X=x,Y=y};
        }
        public static void StopMapFarm(string id){NativeJob j=Get(id);if(j!=null){j.UpRequested=false;if(j.DailyManaged)CancelDailyPriority(j);StopFarm(j);}}
        static void StopFarm(NativeJob j){j.TrainEnabled=false;j.FarmZoneWaiting=false;j.FarmEnabled=false;j.FarmArrived=false;j.FarmStopPending=true;j.GameAutoWanted=false;j.FarmStatus="Đã dừng chạy bãi";}
        // Đọc lại số người mỗi 5 phút; giữ treo cả khi đọc và chuyển khu trên cùng bãi.
        internal static bool BeginFarmZoneRecheck(NativeJob j,DateTime now){
            if(!j.FarmEnabled||!j.FarmArrived||!j.FarmQuietZone||j.FarmZoneWaiting||j.CloseRequested||j.CancelRequested||CurseDisplayStatus(j).Length>0)return false;
            if(j.FarmZoneCheckAt==DateTime.MinValue){j.FarmZoneCheckAt=now;return false;}
            if((now-j.FarmZoneCheckAt).TotalMinutes<5)return false;
            j.FarmZoneCheckAt=now;j.FarmZoneWaiting=true;j.FarmRouteAt=now;j.ZonePrepared=false;j.ZonePlanMap=-1;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;j.ZonePollAt=DateTime.MinValue;j.FarmStatus="Kiểm tra khu ít người (mỗi 5 phút)";
            Log(j,"farm_zone_recheck",",\"interval_seconds\":300");return true;
        }
        static void PumpFarmZones(Remote r,long game,long data,NativeJob j){
            DateTime now=DateTime.UtcNow;if((now-j.ZonePollAt).TotalMilliseconds<750)return;j.ZonePollAt=now;
            try {
                if(j.FarmTarget==null||j.MapId!=j.FarmTarget.MapId||j.ZoneId<0||(now-j.ZoneReadAt).TotalSeconds>4){j.ZoneSyncActive=true;j.ZoneStatus="Chờ dữ liệu khu của bãi";return;}
                int state=FarmI(r,data);if(state==1||state==2||state==3){j.ZoneSyncActive=true;return;}
                if(j.ZonePlanMap!=j.MapId){j.ZonePlanMap=j.MapId;j.ZonePrepared=false;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;}
                if(j.PlannedZone<0){
                    j.ZoneSyncActive=true;j.ZoneStatus="Đọc số người từng khu";
                    if(j.ZoneListWaiting){long popup=ZonePopup(r,game);if(popup!=0&&popup==r.Ptr(data+32)&&r.Ptr(popup+0xe8)!=r.Ptr(data+24)){
                        long array=r.Ptr(popup+0xe8);int length=array==0?0:FarmI(r,array+24);if(length<1||length>256)throw new Exception("zone_list_invalid");int[] ids=new int[length],counts=new int[length];
                        for(int n=0;n<length;n++){long item=r.Ptr(array+32+n*8);if(item==0)throw new Exception("zone_list_changed");ids[n]=n;counts[n]=FarmI(r,item+16);}
                        if(r.Ptr(popup+0xe8)!=array||ZonePopup(r,game)!=popup)throw new Exception("zone_list_changed");
                        j.PlannedZone=ChooseQuietZone(ids,counts,j.ZoneId);if(j.PlannedZone<0)throw new Exception("zone_list_invalid");j.ZoneListWaiting=false;j.ZoneAttempts=0;j.ZoneActionAt=DateTime.MinValue;Log(j,"farm_quiet_zone",",\"map\":"+j.MapId+",\"zone\":"+j.PlannedZone);
                    }else if((now-j.ZoneActionAt).TotalSeconds<10)return;else j.ZoneListWaiting=false;}
                    if(j.PlannedZone<0){if(j.ZoneAttempts>=3){j.ZoneStatus="Không đọc được danh sách khu sau 3 lần";return;}QueueZone(r,data,j,1,-1);j.ZoneListWaiting=true;return;}
                }
                if(j.ZoneId==j.PlannedZone&&(j.ZonePrepared||state==4&&j.ZoneAttempts>0)){j.ZonePrepared=true;j.ZoneSyncActive=false;j.ZoneStatus="Đã chọn khu ít người: "+j.ZoneId;return;}
                j.ZoneSyncActive=true;j.ZoneStatus="Chuyển tới khu "+j.PlannedZone+"; chờ xác nhận";
                if((now-j.ZoneActionAt).TotalSeconds<8)return;if(j.ZoneAttempts>=3){j.ZoneStatus="Chưa đổi được khu sau 3 lần";return;}QueueZone(r,data,j,2,j.PlannedZone);
            }catch{j.ZoneSyncActive=true;j.ZoneStatus="Chưa đọc được danh sách khu";}
        }

        static int FarmI(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
        static bool SameFarmTarget(FarmSpot a,FarmSpot b){return a!=null&&b!=null&&a.MapId==b.MapId&&a.MobId==b.MobId&&a.Level==b.Level&&a.X==b.X&&a.Y==b.Y;}
        internal static bool HandleThiQuyFarmPause(Remote r,long data,NativeJob j){
            if(j.ThiQuyCombatActive&&j.FarmEnabled){
                if(!j.FarmArrived&&!j.ThiQuyFarmPaused&&FarmI(r,data)==0){
                    // Cancel only this worker's owned route (same command as StopFarm),
                    // without removing Up, its destination, or user Auto intent.
                    r.Write(data,BitConverter.GetBytes(6));j.ThiQuyFarmPaused=true;
                    Log(j,"thi_quy_up_route_paused",",\"pid\":"+j.ProcessId+",\"character\":"+Quote(j.CharacterName));
                }
                j.FarmStatus=j.ThiQuyPriorityStatus;return true;
            }
            if(j.ThiQuyFarmPaused){
                if(FarmI(r,data)!=0)return true;j.ThiQuyFarmPaused=false;
                if(j.FarmEnabled&&!j.FarmArrived){j.FarmRouteSent=false;j.FarmRouteAt=DateTime.UtcNow;j.FarmStableAt=DateTime.MinValue;j.FarmPollAt=DateTime.MinValue;Log(j,"thi_quy_up_route_resumed",",\"pid\":"+j.ProcessId+",\"character\":"+Quote(j.CharacterName));}
            }
            return false;
        }
        static void PumpFarm(Remote r,long data,long game,NativeJob j){
            DateTime now=DateTime.UtcNow;
            if(j.FarmStopPending){if(FarmI(r,data)==0){r.Write(data,BitConverter.GetBytes(6));j.FarmStopPending=false;}return;}
            if(HandleThiQuyFarmPause(r,data,j))return;
            if((now-j.FarmPollAt).TotalSeconds<(j.FarmEnabled?1:5))return;j.FarmPollAt=now;
            try {
                long cc=ResolvedClass(r,game+0x16ed658),fcClass=ResolvedClass(r,game+0x16ee888),pc=ResolvedClass(r,game+0x16ee330);
                long canvas=r.Ptr(r.Ptr(cc+0xb8)),fc=r.Ptr(r.Ptr(fcClass+0xb8)+16),player=r.Ptr(r.Ptr(pc+0xb8)+8);
                int map=BitConverter.ToInt16(r.Read(fc+0x134,2),0);long costs=r.Ptr(canvas+0x210);int n=FarmI(r,costs+24);if(n<1||n>500)throw new Exception("farm_level_invalid");byte[] levels=r.Read(costs+32,n*8);long xp=BitConverter.ToInt64(r.Read(player+0xe8,8),0);int lvl=0;
                for(int k=0;k<n;k++){long cost=BitConverter.ToInt64(levels,k*8);if(cost<0||xp<cost)break;xp-=cost;lvl++;}j.CharacterLevel=lvl;
                long catalogue=r.Ptr(canvas+0xe0);int count=FarmI(r,catalogue+24);List<FarmSpot> observed=new List<FarmSpot>();
                if(map>=0&&map<count&&count<=512){long m=r.Ptr(catalogue+32+map*8);string name=ReadLabel(r,r.Ptr(m+24));int kind=r.Read(m+34,1)[0];
                    if(OrdinaryFarmMap(map,kind,name)){long templateArray=r.Ptr(canvas+0xd8);int templateCount=FarmI(r,templateArray+24);HashSet<string> seen=new HashSet<string>();foreach(long mob in ReadVector(r,r.Ptr(fc+0x150)).Take(512)){if(mob==0)continue;byte[] b=r.Read(mob+0x50,48);int tid=BitConverter.ToInt16(b,0),level=BitConverter.ToInt32(b,44);if(tid<0||tid>=templateCount||level<1||level>500)continue;long t=r.Ptr(templateArray+32+tid*8);byte category=r.Read(t+84,1)[0];if(category!=0)continue;string key=tid+":"+level;if(!seen.Add(key))continue;string mobName=ReadLabel(r,r.Ptr(t+48));if(String.IsNullOrWhiteSpace(mobName))continue;observed.Add(new FarmSpot{MapId=map,MapName=name,MobId=tid,MobName=mobName,Level=level,X=BitConverter.ToInt16(b,8),Y=BitConverter.ToInt16(b,10)});}}
                }
                if(BitConverter.ToInt16(r.Read(fc+0x134,2),0)!=map)return;j.FarmPositionReadAt=DateTime.MinValue;j.FarmPositionMap=map;j.FarmPositionX=BitConverter.ToInt16(r.Read(player+16,2),0);j.FarmPositionY=BitConverter.ToInt16(r.Read(player+18,2),0);j.FarmObserved=observed.ToArray();j.FarmPositionReadAt=now;
                if(!j.FarmEnabled){if(FarmI(r,data)==0&&FarmI(r,data+4)!=0)r.Write(data+4,BitConverter.GetBytes(0));return;}if(FarmConflict(j)||j.CancelRequested||j.CloseRequested){StopFarm(j);j.FarmStatus="Dừng chạy bãi vì có auto khác";return;}
                FarmSpot target=SelectFarmSpot(j.FarmChoices,j.FarmCharacterMode?lvl:j.FarmLevel,j.FarmCharacterMode);if(target==null){StopFarm(j);j.FarmStatus="Không có bãi phù hợp với level "+lvl;return;}
                if(!SameFarmTarget(target,j.FarmTarget)){j.FarmTarget=target;j.FarmZoneCheckAt=DateTime.MinValue;j.FarmArrived=false;j.FarmRouteSent=false;j.FarmStableAt=DateTime.MinValue;j.FarmZoneWaiting=false;j.ZonePrepared=false;j.ZonePlanMap=-1;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;}
                if(j.FarmZoneWaiting){
                    if(j.ZonePrepared&&j.ZonePlanMap==map&&j.ZoneId==j.PlannedZone){j.FarmZoneWaiting=false;j.FarmZoneCheckAt=now;j.FarmRouteSent=Math.Abs(BitConverter.ToInt16(r.Read(player+16,2),0)-target.X)<=80&&Math.Abs(BitConverter.ToInt16(r.Read(player+18,2),0)-target.Y)<=80;j.FarmStableAt=DateTime.MinValue;}
                    else{j.FarmStatus="Chọn khu ít người • "+j.ZoneStatus;if((now-j.FarmRouteAt).TotalSeconds>180){StopFarm(j);j.FarmStatus="Dừng: chưa chọn được khu ít người; kiểm tra thông báo game";}return;}
                }
                if(j.FarmArrived){if(map!=target.MapId){StopFarm(j);j.FarmStatus="Đã rời bãi; dừng để tránh xung đột";}else if(!BeginFarmZoneRecheck(j,now))j.FarmStatus="Đang treo • "+target;return;}
                if(!j.FarmRouteSent){if(FarmI(r,data)!=0)return;r.Write(data+96,BitConverter.GetBytes(target.MapId));r.Write(data+100,BitConverter.GetBytes(target.X));r.Write(data+104,BitConverter.GetBytes(target.Y));r.Write(data+4,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(8));j.FarmRouteAt=now;j.FarmRouteSent=true;Log(j,"farm_route",",\"map\":"+target.MapId+",\"level\":"+target.Level);}
                int x=BitConverter.ToInt16(r.Read(player+16,2),0),y=BitConverter.ToInt16(r.Read(player+18,2),0);bool arrived=map==target.MapId&&Math.Abs(x-target.X)<=80&&Math.Abs(y-target.Y)<=80&&r.Ptr(fc+0x310)==0&&FarmI(r,data)==0;
                if(arrived&&j.FarmQuietZone&&(!j.ZonePrepared||j.ZonePlanMap!=map||j.ZoneId!=j.PlannedZone)){j.FarmZoneWaiting=true;j.FarmStableAt=DateTime.MinValue;j.FarmStatus="Đã tới bãi; đang chọn khu ít người";return;}
                if(arrived){if(j.FarmStableAt==DateTime.MinValue)j.FarmStableAt=now;if((now-j.FarmStableAt).TotalSeconds>=2){j.FarmArrived=true;j.FarmZoneCheckAt=now;j.FarmStatus="Đã tới bãi; bật treo • "+target;Log(j,"farm_arrived",",\"map\":"+map);}}else j.FarmStableAt=DateTime.MinValue;
                if((now-j.FarmRouteAt).TotalSeconds>180&&!j.FarmArrived){StopFarm(j);j.FarmStatus="Dừng: chưa tới bãi sau 3 phút; kiểm tra đường đi";return;}
                if(!j.FarmArrived)j.FarmStatus="Đang chạy tới "+target;
            }catch(Exception){j.FarmPositionReadAt=DateTime.MinValue;j.FarmObserved=new FarmSpot[0];if(j.FarmEnabled)j.FarmStatus="Chờ dữ liệu bản đồ";}
        }
    }
}


namespace TGLL {
    public static partial class NativeBackend {
        static readonly object DailyGate=new object();
        public static int DailyDungeonLimit=12,DailyIzanamiLimit=12,DailyQuestLimit=12,DailySonCapLimit=12;
        static void ClearDailyActions(NativeJob j){
            j.DailyApplying=true;
            try{if(j.PersistentUp&&j.SonCapEnabled)SetSonCap(j.Id,false);SetDungeonHigh(j.Id,false);SetReceiveKey(j.Id,false);SetIzanami(j.Id,false);SetAutoQuest(j.Id,false);j.CloseAfterKey=false;j.DungeonQueueOnly=false;j.IzanamiQueueOnly=false;j.QuestQueueOnly=false;j.ReceiveKeyRequested=false;j.IzanamiRequested=false;j.GameAutoWanted=false;j.TrainEnabled=false;j.EntryAutoWaiting=false;}
            finally{j.DailyApplying=false;}
        }
        public static void ConfigureDailyPriority(string id,bool enabled,bool key,bool dungeon,bool izanami,bool quest){
            NativeJob j=Get(id);if(j==null)return;
            lock(DailyGate){
                if(!enabled){if(j.DailyManaged)CancelDailyPriority(j);return;}
                // Không có hoạt động được chọn: giữ nguyên bãi và trạng thái đánh hiện tại.
                if(!key&&!dungeon&&!izanami&&!quest){
                    j.DailyPlan=new bool[4];
                    if(!j.DailyManaged){j.DailyLastDate=DateTime.UtcNow.AddHours(7).Date;j.DailyManaged=true;}
                    if(j.DailyActive){ClearDailyActions(j);j.DailyRunPlan=new bool[4];j.DailyExecuting=false;j.DailyStage=5;}
                    j.DailyStatus="Train quái; chưa chọn hoạt động hằng ngày";return;
                }
                if(j.MailEnabled||(j.MailQueueOnly||j.CloakQueueOnly)||j.DungeonQueueOnly||j.IzanamiQueueOnly||j.QuestQueueOnly)throw new Exception("Dừng hàng chờ riêng trước khi dùng ưu tiên hằng ngày với Up.");
                j.DailyPlan=new[]{key,dungeon,izanami,quest};
                if(!j.DailyManaged){
                    if(j.ReceiveFlowActive||j.TravelActive)throw new Exception("Đợi thao tác NPC hiện tại hoàn tất rồi tích Up lại.");
                    j.DailyLastDate=DateTime.UtcNow.AddHours(7).Date;ClearDailyActions(j);j.DailyManaged=true;j.DailyStatus="Train quái; chờ 00:00 (giờ Việt Nam)";
                }
            }
        }
        public static void ConfigureDailyTeams(string[] orderedIds){
            lock(DailyGate){
                string[] ids=(orderedIds??new string[0]).Where(delegate(string id){NativeJob j=Get(id);return j!=null&&j.DailyManaged&&j.DailyPlan[2];}).ToArray();
                int size=IzanamiTeamSize;for(int start=0;start<ids.Length;start+=size){string[] team=ids.Skip(start).Take(size).ToArray();foreach(string id in team){NativeJob j=Get(id);if(!j.DailyActive)j.DailyTeamIds=team;}}
            }
        }
        public static bool TransferDailyPriority(string previousId,string nextId){
            lock(DailyGate){
                NativeJob old=Get(previousId),next=Get(nextId);if(old==null||next==null||!old.DailyManaged)return false;
                next.PersistentUp=old.PersistentUp;next.UpRequested=old.UpRequested;next.UpRepeatDaily=old.UpRepeatDaily;next.UpDay=old.UpDay;next.UpCompleted=(bool[])old.UpCompleted.Clone();next.UpSonCapTeamIds=old.UpSonCapTeamIds;next.UpIzanamiFull=old.UpIzanamiFull;next.UpSonCapFull=old.UpSonCapFull;next.FarmChoices=old.FarmChoices;next.FarmCharacterMode=old.FarmCharacterMode;next.FarmLevel=old.FarmLevel;next.FarmQuietZone=old.FarmQuietZone;next.DailyManaged=true;next.DailyActive=old.DailyActive;next.DailyPlan=(bool[])old.DailyPlan.Clone();next.DailyRunPlan=(bool[])old.DailyRunPlan.Clone();next.DailyLastDate=old.DailyLastDate;next.DailyStage=old.DailyStage;next.DailyExecuting=false;next.DailyReturnSpot=old.DailyReturnSpot;next.DailyReturnQuiet=old.DailyReturnQuiet;next.DailyTeamIds=old.DailyTeamIds;next.KeyReceiptVerified=old.KeyReceiptVerified;next.DailyStatus="Mở lại — tiếp tục "+DailyStageName(next.DailyStage);
                next.IzanamiRoundsCompleted=old.IzanamiRoundsCompleted;next.IzanamiRoundsTarget=old.IzanamiRoundsTarget;next.IzanamiRoundEntered=old.IzanamiRoundEntered;next.IzanamiRoundComplete=old.IzanamiRoundComplete;next.IzanamiNoAttempts=old.IzanamiNoAttempts;next.EntryAutoKind=old.EntryAutoKind;next.EntryAutoOrigin=old.EntryAutoOrigin;
                foreach(NativeJob peer in jobs.Values)if(peer.DailyManaged){peer.UpSonCapTeamIds=peer.UpSonCapTeamIds.Select(id=>id==previousId?nextId:id).ToArray();peer.DailyTeamIds=peer.DailyTeamIds.Select(delegate(string id){return id==previousId?nextId:id;}).ToArray();if(Array.IndexOf(peer.TeamJobIds,previousId)>=0){peer.TeamJobIds=peer.TeamJobIds.Select(delegate(string id){return id==previousId?nextId:id;}).ToArray();peer.TeamConfirmed=false;}}
                old.PersistentUp=false;old.DailyManaged=false;old.DailyActive=false;old.DailyExecuting=false;
                Log(next,"daily_reconnect_resume",",\"stage\":"+next.DailyStage);return true;
            }
        }
        static void CancelDailyPriority(NativeJob j){
            j.DailyManaged=false;j.DailyActive=false;j.DailyExecuting=false;j.DailyStage=0;j.DailyStatus="Đã dừng ưu tiên hằng ngày";
            if(!j.CloseRequested&&!j.CancelRequested&&j.State!="closed"&&j.State!="failed")ClearDailyActions(j);
            j.PersistentUp=false;
        }
        static bool DailyAlive(NativeJob j){return j!=null&&!j.CloseRequested&&!j.CancelRequested&&j.State!="closed"&&j.State!="failed"&&j.State!="cancelled"&&j.TrainReady&&j.GameSettingsReady;}
        static int DailyOccupancy(int stage){return jobs.Values.Count(delegate(NativeJob j){if(!DailyAlive(j))return false;return stage==2?j.DungeonHighEnabled:stage==3?j.IzanamiEnabled&&!j.SonCapEnabled:j.QuestEnabled&&!j.BeastQuestEnabled;});}
        static string DailyStageName(int stage){return stage==1?"Nhận chìa":stage==2?"Địa cung cao cấp":stage==3?"Cấm thuật":stage==4?"Nhiệm vụ tuần hoàn":"Quay lại bãi train";}
        static void AdvanceDailyStage(NativeJob j){
            ClearDailyActions(j);j.DailyExecuting=false;j.Error="";j.State="opened";
            do{j.DailyStage++;}while(j.DailyStage<=j.DailyRunPlan.Length&&!j.DailyRunPlan[j.DailyStage-1]);
            j.DailyStatus=(j.PersistentUp&&j.DailyStage==5?"Sơn Cáp":DailyStageName(j.DailyStage))+" — chờ sẵn sàng";
            Log(j,"daily_stage",",\"stage\":"+j.DailyStage);
        }
        static void StartDailyActivity(NativeJob j){
            if(j.PersistentUp&&(j.DailyStage==3||j.DailyStage==5)){StartUpTeamActivity(j);return;}
            if(j.DailyStage==3){
                string[] ids=j.DailyTeamIds.Length>0?j.DailyTeamIds:new[]{j.Id};NativeJob[] team=ids.Select(delegate(string id){return Get(id);}).ToArray();
                if(team.Any(delegate(NativeJob p){return !DailyAlive(p)||!p.DailyActive||p.DailyStage!=3;})){j.DailyStatus="Cấm thuật — chờ các thành viên xong bước trước";return;}
                if(team.Any(delegate(NativeJob p){return p.DailyExecuting;})){
                    if(DailyOccupancy(3)>=Math.Max(1,DailyIzanamiLimit)){j.DailyStatus="Mở lại cấm thuật — chờ lượt";return;}
                    j.DailyApplying=true;
                    try{j.TeamJobIds=ids;j.TeamManaged=true;SetIzanami(j.Id,true);if(j.IzanamiRoundEntered){j.IzanamiRequested=false;j.GameAutoManaged=true;j.GameAutoWanted=true;}j.DailyExecuting=true;j.DailyStatus="Đã mở lại — tiếp tục cấm thuật";}
                    finally{j.DailyApplying=false;}return;
                }
                if(DailyOccupancy(3)+team.Length>Math.Max(1,DailyIzanamiLimit)){j.DailyStatus="Cấm thuật — chờ lượt theo số acc tối đa";return;}
                foreach(NativeJob p in team)p.DailyApplying=true;
                try{
                    ConfigureIzanamiTeam(ids);
                    bool resuming=team.Any(delegate(NativeJob p){return p.IzanamiRoundEntered;});
                    foreach(NativeJob p in team){if(!resuming){p.IzanamiRoundEntered=false;p.IzanamiRoundComplete=false;p.IzanamiNoAttempts=false;p.IzanamiEntries=0;}SetIzanami(p.Id,true);if(p.IzanamiRoundEntered){p.IzanamiRequested=false;p.GameAutoManaged=true;p.GameAutoWanted=true;}p.DailyExecuting=true;p.DailyStatus="Đang đi cấm thuật";}
                }finally{foreach(NativeJob p in team)p.DailyApplying=false;}
                return;
            }
            if(j.DailyStage==2&&DailyOccupancy(2)>=Math.Max(1,DailyDungeonLimit)||j.DailyStage==4&&DailyOccupancy(4)>=Math.Max(1,DailyQuestLimit)){j.DailyStatus=DailyStageName(j.DailyStage)+" — chờ lượt theo số acc tối đa";return;}
            j.DailyApplying=true;
            try{
                if(j.DailyStage==1){j.KeyReceiptVerified=false;SetReceiveKey(j.Id,true);}
                else if(j.DailyStage==2)SetDungeonHigh(j.Id,true);
                else if(j.DailyStage==4)SetAutoQuest(j.Id,true);
                j.DailyExecuting=true;j.DailyStatus="Đang chạy: "+DailyStageName(j.DailyStage);
            }finally{j.DailyApplying=false;}
        }
        static bool DailyStageComplete(NativeJob j){
            if(j.PersistentUp&&(j.DailyStage==3||j.DailyStage==5)){string[] ids=j.DailyStage==3?j.DailyTeamIds:j.UpSonCapTeamIds;bool outside=j.MapId>=0&&(j.DailyStage==3?j.MapId!=89&&!j.IzanamiRoundEntered:Array.IndexOf(SonCapMaps,j.MapId)<0);return outside&&ids.Any(id=>{NativeJob p=Get(id);return p!=null&&(p.UpCompleted[j.DailyStage-1]||(j.DailyStage==3?p.IzanamiNoAttempts:p.SonCapNoAttempts));});}
            if(j.DailyStage==1)return j.KeyReceiptVerified;
            if(j.DailyStage==2)return j.DungeonFinished;
            if(j.DailyStage==4)return j.QuestFinished;
            if(j.DailyStage==3){
                string[] ids=j.DailyTeamIds.Length>0?j.DailyTeamIds:new[]{j.Id};
                return ids.All(delegate(string id){NativeJob p=Get(id);return p!=null&&(p.DailyStage>3||p.DailyActive&&p.DailyStage==3&&(p.IzanamiRunFinished||p.IzanamiNoAttempts));});
            }return false;
        }
        public static void PulseDailyPriority(DateTime utcNow){
            lock(DailyGate){foreach(NativeJob j in jobs.Values){
                if(!j.DailyManaged||!DailyAlive(j))continue;
                if(j.PersistentUp){if(UpSessionReady(j))PulseUpPriority(j,utcNow);continue;}
                if(CurseDisplayStatus(j).Length>0){if(j.DailyActive)j.DailyStatus="Bị bùa — chờ nhập mã";continue;}
                DateTime day=utcNow.AddHours(7).Date;
                if(!j.DailyActive){
                    if(day<=j.DailyLastDate||!j.FarmEnabled||!j.FarmArrived||j.FarmZoneWaiting)continue;
                    j.DailyLastDate=day;
                    if(!j.DailyPlan.Any(delegate(bool b){return b;}))continue;
                    if(j.FarmTarget==null){j.DailyStatus="Chưa có bãi train để quay lại";continue;}
                    FarmSpot s=j.FarmTarget;j.DailyReturnSpot=new FarmSpot{MapId=s.MapId,MapName=s.MapName,MobId=s.MobId,MobName=s.MobName,Level=s.Level,X=s.X,Y=s.Y};j.DailyReturnQuiet=j.FarmQuietZone;
                    j.DailyRunPlan=(bool[])j.DailyPlan.Clone();j.DailyStage=0;j.DailyExecuting=false;j.DailyActive=true;StopFarm(j);j.DailyStatus="Qua 00:00 — tạm dừng Up";Log(j,"daily_begin",",\"date\":"+Quote(day.ToString("yyyy-MM-dd")));continue;
                }
                if(j.FarmStopPending||j.ReceiveFlowActive||j.TravelActive)continue;
                if(j.DailyStage==0){AdvanceDailyStage(j);continue;}
                if(j.DailyStage==5){
                    FarmSpot s=j.DailyReturnSpot;if(s==null){j.DailyStatus="Thiếu bãi quay lại; giữ game để kiểm tra";continue;}
                    ClearDailyActions(j);j.DailyActive=false;j.State="opened";j.Error="";
                    try{StartMapFarm(j.Id,false,s.Level,new[]{s},j.DailyReturnQuiet);j.DailyStatus="Đã xong hoạt động — quay lại bãi train";Log(j,"daily_return_farm",",\"map\":"+s.MapId+",\"x\":"+s.X+",\"y\":"+s.Y);}
                    catch{j.DailyActive=true;j.DailyStatus="Chưa chạy lại bãi; giữ game để kiểm tra";}
                    continue;
                }
                if(!j.DailyExecuting){StartDailyActivity(j);continue;}
                if(DailyStageComplete(j)){AdvanceDailyStage(j);continue;}
                if(j.Error.Length>0){j.DailyStatus=DailyStageName(j.DailyStage)+" — cần kiểm tra: "+j.Error;continue;}
                if(j.DailyStage==4&&!j.QuestEnabled&&!j.QuestFinished)j.DailyStatus="Nhiệm vụ chưa hoàn tất: "+j.QuestStatus;
            }}
        }
    }
}

namespace TGLL {
    public static partial class NativeBackend {
        static readonly int[] SonCapMaps={94,95,97,93,96};
        static int[] sonCapX={2294,2480,3234,1480},sonCapY={264,201,436,277};
        public static int SonCapTeamSize=6;
        public static void ConfigureSonCapPortals(int[] xs,int[] ys){
            if(xs==null||ys==null||xs.Length!=4||ys.Length!=4||xs.Any(x=>x<0||x>32767)||ys.Any(y=>y<0||y>32767))throw new ArgumentException("soncap_portal_invalid");
            sonCapX=(int[])xs.Clone();sonCapY=(int[])ys.Clone();
        }
        public static void SetSonCap(string id,bool enabled){
            NativeJob j=Get(id);if(j==null||j.CloseRequested||j.CancelRequested)throw new Exception("game_session_unavailable");
            if(j.PersistentUp&&!j.DailyApplying&&!j.SonCapApplying){j.DailyPlan[4]=enabled;return;}
            if(j.SonCapEnabled==enabled)return;
            if(j.CloakQueueOnly)enabled=false;if(enabled&&((j.DailyManaged&&!j.DailyApplying)||j.FarmEnabled||j.DungeonQueueOnly||j.IzanamiQueueOnly||j.QuestQueueOnly||j.MailQueueOnly))throw new Exception("Dừng Up hoặc hàng chờ khác trước khi bật Sơn Cáp.");
            if(enabled&&!j.SonCapRebound){j.TeamJobIds=new string[0];j.TeamManaged=false;}j.SonCapRebound=false;
            j.SonCapApplying=true;
            try{
                if(enabled){SetReceiveKey(id,false);SetDungeonHigh(id,false);SetAutoQuest(id,false);SetIzanami(id,false);SetIzanami(id,true);j.CloseAfterKey=false;j.IzanamiLimitArmed=false;}
                else{SetIzanami(id,false);j.SonCapStopPending=j.SonCapMoving;}
            }finally{j.SonCapApplying=false;}
            j.IzanamiRoundEntered=false;j.IzanamiRoundComplete=false;j.EntryAutoWaiting=false;j.EntryCombatMap=-1;j.SonCapEntrySubmitted=false;
            j.SonCapPortalStillAt=DateTime.MinValue;j.SonCapPortalWatchX=Int32.MinValue;j.SonCapPortalWatchY=Int32.MinValue;j.SonCapRouteRetries=0;j.SonCapRouteLastX=Int32.MinValue;j.SonCapRouteLastY=Int32.MinValue;j.SonCapRouteStillAt=DateTime.MinValue;j.SonCapRouteRetryAt=DateTime.MinValue;
                j.SonCapRightOperation=null;j.SonCapRightAttempts=0;j.SonCapArrivalAt=DateTime.MinValue;j.SonCapRightError="";j.SonCapClearedStage=-1;j.SonCapReleaseOperation=null;j.SonCapReleaseAttempted=false;j.SonCapAutoRetryAt=DateTime.MinValue;j.SonCapAutoRetries=0;j.SonCapAutoConfirmed=false;j.SonCapMapAutoActive=false;j.SonCapAutoMap=-1;j.SonCapAutoSeenAt=DateTime.MinValue;j.SonCapAutoPollAt=DateTime.MinValue;j.SonCapRegroupAfterReturn=false;j.SonCapEnabled=enabled;j.SonCapNoAttempts=false;j.SonCapReturnAt=DateTime.MinValue;j.SonCapFinished=false;j.SonCapMoving=false;j.SonCapStage=-1;j.SonCapAlive=-1;j.SonCapEmptyAt=DateTime.MinValue;j.SonCapMapAt=DateTime.MinValue;j.SonCapPollAt=DateTime.MinValue;j.SonCapFailure="";
            j.Error="";j.GameAutoManaged=true;j.GameAutoWanted=false;j.TrainEnabled=false;j.SonCapStatus=enabled?"Sơn Cáp: đợi game, về Làng Cát và lập nhóm":"Sơn Cáp: đã dừng";
            Log(j,enabled?"soncap_start":"soncap_stop","");
        }
        public static void ConfigureSonCapTeams(string[] ids){
            if(ids==null)return;int size=Math.Max(1,Math.Min(6,SonCapTeamSize));
            for(int start=0;start<ids.Length;start+=size){string[] team=ids.Skip(start).Take(size).ToArray();
                // Giữ tổ đang trong hang, nhưng phải điền các chỗ chờ đăng nhập.
                NativeJob anchor=team.Select(id=>Get(id??"")).FirstOrDefault(p=>p!=null&&p.TeamJobIds.Length>0&&(p.SonCapStage>=0||p.SonCapEntrySubmitted));
                if(anchor!=null){
                    string[] previous=anchor.TeamJobIds;if(previous.Length!=team.Length)continue;
                    bool compatible=true;for(int n=0;n<team.Length;n++)if(!String.IsNullOrEmpty(previous[n])&&!String.IsNullOrEmpty(team[n])&&previous[n]!=team[n])compatible=false;
                    if(!compatible)continue;
                    string[] merged=new string[team.Length];for(int n=0;n<team.Length;n++)merged[n]=String.IsNullOrEmpty(team[n])?previous[n]:team[n];
                    foreach(string id in merged){NativeJob peer=Get(id??"");if(peer==null||!peer.SonCapEnabled)continue;
                        if(String.Join("|",peer.TeamJobIds)!=String.Join("|",merged)){peer.TeamJobIds=(string[])merged.Clone();peer.TeamManaged=true;Log(peer,"soncap_team_slots_updated",",\"members\":"+merged.Count(x=>!String.IsNullOrEmpty(x))+",\"slots\":"+merged.Length);}
                        if(id!=merged[0])peer.IzanamiRequested=false;
                    }
                    continue;
                }
                ConfigureIzanamiTeam(team);
            }
        }
        public static void RebindSonCapJob(string oldId,string newId){
            NativeJob old=Get(oldId),next=Get(newId);if(old==null||next==null||!old.SonCapEnabled)return;
            foreach(NativeJob p in jobs.Values)if(p.SonCapEnabled&&p.TeamJobIds.Contains(oldId))p.TeamJobIds=p.TeamJobIds.Select(id=>id==oldId?newId:id).ToArray();
            next.TeamJobIds=(string[])old.TeamJobIds.Clone();next.TeamManaged=old.TeamManaged;next.SonCapRebound=true;old.SonCapEnabled=false;old.IzanamiEnabled=false;
        }
        internal static bool SonCapTeamClear(NativeJob j,DateTime now){
            string[] ids=j.TeamJobIds.Length>0?j.TeamJobIds:new[]{j.Id};
            return ids.All(id=>{NativeJob p=Get(id);return p!=null&&p.SonCapEnabled&&!p.CloseRequested&&!p.CancelRequested&&p.SonCapStage==j.SonCapStage&&p.SonCapAlive==0&&p.SonCapEmptyAt!=DateTime.MinValue&&(now-p.SonCapEmptyAt).TotalSeconds>=3&&(now-p.SonCapPollAt).TotalSeconds<4;});
        }
        // Tách quyết định khỏi đọc game để kiểm thử map trống, chờ nhóm và chuyển hang.
        // Xác nhận map độc lập với danh sách quái, để không giữ bước chạy cổng cũ.
        internal static bool ObserveSonCapMapEntry(NativeJob j,int map,DateTime now){
            int stage=Array.IndexOf(SonCapMaps,map);if(stage<0)return false;if(stage==j.SonCapStage)return true;
                if(j.SonCapStage>=0&&stage!=j.SonCapStage+1){j.GameAutoWanted=false;j.SonCapFailure="Map chuyển không đúng thứ tự";j.SonCapStatus="Sơn Cáp: "+j.SonCapFailure;return false;}
                j.SonCapPortalStillAt=DateTime.MinValue;j.SonCapPortalWatchX=Int32.MinValue;j.SonCapPortalWatchY=Int32.MinValue;j.SonCapRouteRetries=0;j.SonCapRouteLastX=Int32.MinValue;j.SonCapRouteLastY=Int32.MinValue;j.SonCapRouteStillAt=DateTime.MinValue;j.SonCapRouteRetryAt=DateTime.MinValue;
                j.SonCapRightOperation=null;j.SonCapRightAttempts=0;j.SonCapArrivalAt=DateTime.MinValue;j.SonCapRightError="";j.SonCapRegroupAfterReturn=false;j.SonCapClearedStage=-1;j.SonCapStage=stage;j.SonCapMapAt=now;j.SonCapEmptyAt=DateTime.MinValue;j.SonCapMoving=false;j.SonCapStopPending=false;j.SonCapAlive=-1;j.SonCapReleaseOperation=null;j.SonCapReleaseAttempted=false;j.SonCapAutoRetryAt=DateTime.MinValue;j.SonCapAutoRetries=0;j.SonCapAutoConfirmed=false;j.SonCapMapAutoActive=false;j.GameAutoWanted=false;j.SonCapEntrySubmitted=true;j.EntryAutoKind=3;j.EntryCombatMap=map;j.IzanamiRoundEntered=true;j.IzanamiRequested=false;j.IzanamiTravelReady=true;j.EntryAutoWaiting=false;
                Log(j,"soncap_map",",\"stage\":"+(stage+1)+",\"map\":"+map);
            return true;
        }
        internal static int ObserveSonCap(NativeJob j,int map,int alive,bool loaded,DateTime now){
            if(!j.SonCapEnabled||j.SonCapFinished||j.SonCapFailure.Length>0)return 0;
            if(!loaded){j.SonCapAlive=-1;j.SonCapEmptyAt=DateTime.MinValue;j.SonCapStatus="Sơn Cáp: đợi dữ liệu map";return 0;}
            int stage=Array.IndexOf(SonCapMaps,map);
            if(stage>=0&&j.SonCapAlive!=alive)Log(j,"soncap_monsters",",\"map\":"+map+",\"alive\":"+alive+",\"loaded\":"+(loaded?"true":"false"));
            if(stage<0){if(ObserveSonCapReturn(j,map,now))return 0;if(j.SonCapEntrySubmitted&&j.SonCapStage<0&&(now-j.EntryAutoAt).TotalSeconds>60){j.GameAutoWanted=false;j.SonCapFailure="Chưa xác nhận vào hang Sơn Cáp";j.SonCapStatus="Sơn Cáp: "+j.SonCapFailure;}if(j.SonCapStage>=0){j.GameAutoWanted=false;j.SonCapFailure="Rời hang trước khi xác nhận hoàn thành";j.SonCapStatus="Sơn Cáp: "+j.SonCapFailure;}return 0;}
            if(!ObserveSonCapMapEntry(j,map,now))return 0;
            j.SonCapAlive=alive;
            if(alive<0){j.SonCapEmptyAt=DateTime.MinValue;j.SonCapStatus="Sơn Cáp: chờ xác nhận số quái, giữ kiểm tra treo theo map";return 0;}
            if(j.SonCapMoving&&alive>0){j.SonCapMoving=false;j.SonCapStopPending=true;j.SonCapClearedStage=-1;j.SonCapEmptyAt=DateTime.MinValue;j.GameAutoWanted=false;j.SonCapStatus="Sơn Cáp: còn quái, hủy chạy cổng để đánh tiếp";Log(j,"soncap_portal_cancelled",",\"alive\":"+alive);return 0;}
            if(j.SonCapMoving){j.GameAutoWanted=false;j.SonCapStatus="Sơn Cáp: đang qua cổng map "+(stage+1);if((now-j.SonCapRouteAt).TotalSeconds>180){j.SonCapFailure="Chưa xác nhận sang map kế tiếp";j.SonCapStopPending=true;j.SonCapMoving=false;j.SonCapStatus="Sơn Cáp: "+j.SonCapFailure;}return 0;}
            if(alive>0){j.SonCapClearedStage=-1;j.SonCapEmptyAt=DateTime.MinValue;j.GameAutoWanted=true;j.SonCapStatus="Sơn Cáp map "+(stage+1)+"/5: còn "+alive+" quái • "+(j.GameAutoEnabled?"treo đang bật":"treo chưa bật, đang thử lại");return 0;}
            if((now-j.SonCapMapAt).TotalSeconds<5){j.SonCapEmptyAt=DateTime.MinValue;j.GameAutoWanted=false;return 0;}
            if(j.SonCapEmptyAt==DateTime.MinValue)j.SonCapEmptyAt=now;
            j.GameAutoWanted=false;j.SonCapStatus="Sơn Cáp map "+(stage+1)+": hết quái, xác nhận và tắt treo để qua cổng";
            // Mỗi acc tự qua cổng; không phụ thuộc trạng thái/mất kết nối của đồng đội.
            if(j.SonCapClearedStage!=stage){
                if((now-j.SonCapEmptyAt).TotalSeconds<3)return 0;
                j.SonCapClearedStage=stage;
            }
            if(j.GameAutoEnabled)return 0;
            if(stage==SonCapMaps.Length-1){j.SonCapFinished=true;j.IzanamiRoundComplete=true;j.SonCapStatus="Đã hoàn thành Sơn Cáp (5/5 map)";Log(j,"soncap_finished","");return 2;}
            return 1;
        }
        internal static bool IsSonCapMonsterType(string name){return name=="iu";}
        // Mixed world lists contain Char and gd (ground item, gb at +0x50).
        // Only iu monsters have the HP/state layout used below.
        internal static int SonCapEntityType(string name){return IsSonCapMonsterType(name)?1:(name=="Char"||name=="gd")?0:-1;}
        static int SonCapEntityClass(Remote r,long cls){
            for(int depth=0;cls!=0&&depth<12;depth++){
                byte[] bytes=r.Read(r.Ptr(cls+16),80);int end=Array.IndexOf(bytes,(byte)0);if(end<0)throw new Exception("soncap_monster_class_unverified");
                int kind=SonCapEntityType(Encoding.UTF8.GetString(bytes,0,end));if(kind>=0)return kind;
                cls=r.Ptr(cls+0x58);
            }return -1;
        }
        static string ReadClassName(Remote r,long cls){byte[] bytes=r.Read(r.Ptr(cls+16),80);int end=Array.IndexOf(bytes,(byte)0);return end<0?"invalid_name":Encoding.UTF8.GetString(bytes,0,end);}
        internal static int ReadSonCapAlive(Remote r,long game,long fc){
            HashSet<long> seen=new HashSet<long>();Dictionary<long,int> types=new Dictionary<long,int>();int count=0;
            foreach(int offset in new[]{0x150,0x160,0x190})foreach(long mob in ReadVector(r,r.Ptr(fc+offset))){
                if(mob==0||!seen.Add(mob))continue;long cls=r.Ptr(mob);int kind;
                if(!types.TryGetValue(cls,out kind)){kind=SonCapEntityClass(r,cls);types[cls]=kind;}
                if(kind==0)continue;
                // Kiểu chưa biết vẫn chặn chuyển map, tránh bỏ sót quái/boss.
                if(kind<0){string name=ReadClassName(r,cls);throw new Exception("soncap_monster_class_unverified:"+name+":list="+offset.ToString("x"));}
                if(r.Read(mob+0x3d,1)[0]<4&&BitConverter.ToInt32(r.Read(mob+0x78,4),0)>0)count++;
            }
            return count;
        }
        internal static bool SonCapReleaseAllowed(NativeJob j){return j!=null&&j.SonCapEnabled&&j.SonCapMapAutoActive&&j.GameAutoWanted&&!j.SonCapMoving&&!j.SonCapStopPending&&!j.SonCapFinished&&j.SonCapFailure.Length==0&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&CurseDisplayStatus(j).Length==0;}
        internal static bool SonCapNudgeAllowed(NativeJob j){return j!=null&&j.SonCapEnabled&&j.SonCapMoving&&!j.SonCapFinished&&!j.CloseRequested&&!j.CancelRequested&&!j.SonCapStopPending&&j.SonCapStage>=0&&j.SonCapStage<4&&j.TrainReady&&j.GameSettingsReady&&!j.ReconnectRequested&&j.MapId==SonCapMaps[j.SonCapStage]&&j.SonCapAlive==0&&!j.GameAutoEnabled&&j.SonCapFailure.Length==0&&CurseDisplayStatus(j).Length==0;}
        internal static bool PlanSonCapRight(NativeJob j,int x,int y,bool routeActive,DateTime now){
            if(!SonCapNudgeAllowed(j)||routeActive){j.SonCapArrivalAt=DateTime.MinValue;return false;}
            int stage=j.SonCapStage;if(x<sonCapX[stage]-32||x>sonCapX[stage]+224||Math.Abs(y-sonCapY[stage])>32){j.SonCapArrivalAt=DateTime.MinValue;return false;}
            if(j.SonCapArrivalAt==DateTime.MinValue){j.SonCapArrivalAt=now;return false;}
            if((now-j.SonCapArrivalAt).TotalSeconds<1||j.SonCapRightOperation!=null||j.SonCapRightAttempts>=3||(now-j.SonCapRightAt).TotalSeconds<3)return false;
            return true;
        }
        internal static bool PlanSonCapRouteRetry(NativeJob j,int x,int y,bool routeActive,DateTime now){
            if(!SonCapNudgeAllowed(j)||routeActive||j.SonCapRightAttempts>0){j.SonCapRouteStillAt=DateTime.MinValue;return false;}
            int stage=j.SonCapStage;if(Math.Abs(x-sonCapX[stage])<=32&&Math.Abs(y-sonCapY[stage])<=32){j.SonCapRouteStillAt=DateTime.MinValue;return false;}
            if(Math.Abs((long)x-j.SonCapRouteLastX)>2||Math.Abs((long)y-j.SonCapRouteLastY)>2||j.SonCapRouteStillAt==DateTime.MinValue){j.SonCapRouteLastX=x;j.SonCapRouteLastY=y;j.SonCapRouteStillAt=now;return false;}
            j.SonCapStatus="Sơn Cáp: chưa tới cổng, đang ở "+x+", "+y+" • đích "+sonCapX[stage]+", "+sonCapY[stage]+" • thử lại "+j.SonCapRouteRetries+"/3";
            return (now-j.SonCapRouteStillAt).TotalSeconds>=3&&(now-j.SonCapRouteAt).TotalSeconds>=8&&(now-j.SonCapRouteRetryAt).TotalSeconds>=8&&j.SonCapRouteRetries<3&&j.SonCapRightOperation==null;
        }
        internal static bool SonCapRouteStalled(NativeJob j,int x,int y,bool routeActive,DateTime now){
            if(!SonCapNudgeAllowed(j)||!routeActive){j.SonCapPortalStillAt=DateTime.MinValue;return false;}
            if(j.SonCapPortalStillAt==DateTime.MinValue||Math.Abs((long)x-j.SonCapPortalWatchX)>2||Math.Abs((long)y-j.SonCapPortalWatchY)>2){j.SonCapPortalWatchX=x;j.SonCapPortalWatchY=y;j.SonCapPortalStillAt=now;return false;}
            return (now-j.SonCapPortalStillAt).TotalSeconds>=8;
        }
        static void PumpSonCapPortalTravel(Remote r,long data,NativeJob j,int x,int y,bool routeActive,DateTime now){
            if(SonCapRouteStalled(j,x,y,routeActive,now)&&FarmI(r,data)==0){
                // Command 6 clears only the route owned by this callback (data+72).
                r.Write(data,BitConverter.GetBytes(6));j.SonCapPortalStillAt=DateTime.MinValue;
                Log(j,"soncap_route_stalled",",\"map\":"+j.MapId+",\"x\":"+x+",\"y\":"+y);return;
            }
            if(PlanSonCapRouteRetry(j,x,y,routeActive,now)&&FarmI(r,data)==0){
                int stage=j.SonCapStage;j.SonCapRouteRetries++;j.SonCapRouteRetryAt=now;j.SonCapRouteStillAt=DateTime.MinValue;
                r.Write(data+96,BitConverter.GetBytes(SonCapMaps[stage]));r.Write(data+100,BitConverter.GetBytes(sonCapX[stage]));r.Write(data+104,BitConverter.GetBytes(sonCapY[stage]));r.Write(data+4,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(8));
                Log(j,"soncap_route_retry",",\"map\":"+SonCapMaps[stage]+",\"x\":"+x+",\"y\":"+y+",\"target_x\":"+sonCapX[stage]+",\"target_y\":"+sonCapY[stage]+",\"retry\":"+j.SonCapRouteRetries);return;
            }
            PumpSonCapRight(r,data,j,x,y,routeActive,now);
        }
        // Deliver a bounded right-key pulse on the game thread, without Windows input.
        internal static void PumpSonCapRight(Remote r,long data,NativeJob j,int x,int y,bool routeActive,DateTime now){
            int result=BitConverter.ToInt32(r.Read(data+136,4),0);
            if(result!=0){r.Write(data+136,new byte[4]);Log(j,"soncap_right_game_result",",\"status\":"+result+",\"map\":"+j.MapId+",\"x\":"+x+",\"y\":"+y);}
            if(FarmI(r,data)!=0||BitConverter.ToInt32(r.Read(data+116,4),0)!=0)return;
            if(PlanSonCapRight(j,x,y,routeActive,now)){
                int stage=j.SonCapStage;r.Write(data+96,BitConverter.GetBytes(SonCapMaps[stage]));r.Write(data+4,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(9));
                j.SonCapRightAttempts++;j.SonCapRightAt=now;j.SonCapStatus="Sơn Cáp: chạy phải qua hàm game ("+j.SonCapRightAttempts+"/3)";
                Log(j,"soncap_right_game_requested",",\"map\":"+SonCapMaps[stage]+",\"next_map\":"+SonCapMaps[stage+1]+",\"x\":"+x+",\"y\":"+y+",\"hold_ms\":600,\"attempt\":"+j.SonCapRightAttempts);
            }else if(j.SonCapRightAttempts>=3){j.SonCapStatus="Sơn Cáp: đã chạy phải 3 lần, chưa xác nhận chuyển map";}
        }
        // Chỉ xác nhận map mỗi giây; bật treo sau 2 giây, nhường bước chạy cổng/hết quái.
        internal static void ObserveSonCapMapAuto(NativeJob j,int map,bool loaded,DateTime now){
            int stage=Array.IndexOf(SonCapMaps,map);
            if(!j.SonCapEnabled||!loaded||stage<0||j.CloseRequested||j.CancelRequested||j.ReconnectRequested||!j.TrainReady||!j.GameSettingsReady||j.SonCapFinished||j.SonCapFailure.Length>0){j.SonCapMapAutoActive=false;j.SonCapAutoMap=-1;j.SonCapAutoSeenAt=DateTime.MinValue;return;}
            if(!ObserveSonCapMapEntry(j,map,now))return;
            if(j.SonCapAutoMap!=map){j.SonCapAutoMap=map;j.SonCapAutoSeenAt=now;j.SonCapMapAutoActive=false;j.SonCapAutoConfirmed=false;}
            if(j.SonCapMoving||j.SonCapStopPending||(j.SonCapStage==stage&&j.SonCapAlive==0)||CurseDisplayStatus(j).Length>0){j.SonCapMapAutoActive=false;return;}
            if((now-j.SonCapAutoSeenAt).TotalSeconds<3){j.GameAutoStatus="Sơn Cáp: map "+map+", đợi đủ 3 giây trước khi bật treo";return;}
            if(!j.SonCapMapAutoActive)Log(j,"soncap_map_auto_enabled",",\"map\":"+map+",\"delay_seconds\":3");
            j.SonCapMapAutoActive=true;j.ReceiveFlowActive=false;j.ReceiveKeyRequested=false;j.ReplayRequested=false;j.EntryAutoWaiting=false;j.IzanamiRequested=false;j.IzanamiTravelReady=true;j.GameAutoManaged=true;j.GameAutoWanted=true;j.TrainEnabled=false;j.State="soncap_running";j.Stage="soncap_combat";j.GameAutoStatus="Sơn Cáp: tự bật treo sau 3 giây ở map "+map;
            if(j.GameAutoEnabled){if(!j.SonCapAutoConfirmed)Log(j,"soncap_map_auto_confirmed",",\"map\":"+map);j.SonCapAutoConfirmed=true;j.GameAutoStatus="Sơn Cáp map "+map+": đã xác nhận treo bật";}
            else{if(j.SonCapAutoConfirmed)Log(j,"soncap_map_auto_lost",",\"map\":"+map);j.SonCapAutoConfirmed=false;j.GameAutoStatus="Sơn Cáp map "+map+": chờ xác nhận bật treo, thử lại cách 3 giây";}

        }
        // Kiểm tra trạng thái thật của panel hiện tại; thử lại độc lập với hàng tùy chọn.
        internal static void PumpSonCapAutoRetry(Remote r,long data,long root,NativeJob j,DateTime now){
            if(data==0||root==0||!j.SonCapEnabled||!GameAutoDesired(j))return;
            bool enabled=r.Read(root+0x188,1)[0]!=0;j.GameAutoEnabled=enabled;if(enabled){j.SonCapAutoRetries=0;j.SonCapReleaseAttempted=false;return;}
            if((now-j.SonCapAutoRetryAt).TotalSeconds<3||BitConverter.ToInt32(r.Read(data,4),0)!=0)return;
            if(BitConverter.ToInt32(r.Read(data+4,4),0)!=1||r.Ptr(data+8)!=root)return;
            if(j.SonCapAutoRetries>=3&&!j.SonCapReleaseAttempted&&SonCapReleaseAllowed(j)){
                r.Write(data+60,BitConverter.GetBytes(1));j.SonCapReleaseAttempted=true;
                Log(j,"soncap_movement_release_requested",",\"map\":"+j.SonCapAutoMap+",\"retry\":"+j.SonCapAutoRetries);
            }
            r.Write(data+32,BitConverter.GetBytes(1));r.Write(data,BitConverter.GetBytes(1));j.SettingsInFlight=true;j.SettingsAutoAt=now;j.SettingsAutoValue=1;j.SonCapAutoRetryAt=now;j.SonCapAutoRetries++;
            Log(j,"soncap_auto_retry",",\"map\":"+j.SonCapAutoMap+",\"attempt\":"+j.SonCapAutoRetries+",\"observed_enabled\":false");
        }
        static void PumpSonCapMapAuto(Remote r,long game,long data,long settingsData,NativeJob j,Stopwatch timer){
            if(!j.SonCapEnabled){j.SonCapMapAutoActive=false;j.SonCapAutoMap=-1;return;}
            DateTime now=DateTime.UtcNow;if((now-j.SonCapAutoPollAt).TotalSeconds<1)return;j.SonCapAutoPollAt=now;
            try{
                long cls=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(cls+0xb8)+16);if(fc==0||r.Ptr(fc)!=cls)throw new Exception("map_not_ready");
                int map=BitConverter.ToInt16(r.Read(fc+0x134,2),0);long cc=ResolvedClass(r,game+0x16ed658),canvas=r.Ptr(r.Ptr(cc+0xb8));bool loaded=canvas!=0&&r.Ptr(canvas+0x1b8)==fc;
                long rootClass=ResolvedClass(r,game+0x16ee808),root=r.Ptr(r.Ptr(rootClass+0xb8)+8);
                if(root!=0&&r.Ptr(root)==rootClass)j.GameAutoEnabled=r.Read(root+0x188,1)[0]!=0;else root=0;
                ObserveSonCapMapAuto(j,map,loaded,now);
                if(root!=0)PumpSonCapAutoRetry(r,settingsData,root,j,now);
                if(j.SonCapMapAutoActive&&(timer.IsRunning||j.TravelActive)){CancelTravel(r,j);r.Write(data,BitConverter.GetBytes(12));timer.Stop();}
            }catch{j.SonCapMapAutoActive=false;j.SonCapAutoMap=-1;j.SonCapAutoSeenAt=DateTime.MinValue;}
        }
        static void PumpSonCap(Remote r,long game,long routeData,NativeJob j){
            if(j.SonCapStopPending){if(FarmI(r,routeData)==0){r.Write(routeData,BitConverter.GetBytes(6));j.SonCapStopPending=false;}return;}
            if(!j.SonCapEnabled||j.CloseRequested||j.CancelRequested||!j.TrainReady||!j.GameSettingsReady)return;
            DateTime now=DateTime.UtcNow;if((now-j.SonCapPollAt).TotalSeconds<1)return;j.SonCapPollAt=now;
            if(CurseDisplayStatus(j).Length>0){j.SonCapEmptyAt=DateTime.MinValue;j.SonCapStatus=CurseDisplayStatus(j);return;}
            try{
                long cls=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(cls+0xb8)+16);if(fc==0||r.Ptr(fc)!=cls)throw new Exception("map_not_ready");
                int map=BitConverter.ToInt16(r.Read(fc+0x134,2),0);
                if(j.SonCapPendingMap==map){if(FarmI(r,routeData)!=0)return;r.Write(routeData,BitConverter.GetBytes(6));j.SonCapPendingMap=-1;Log(j,"soncap_next_map_route_arrived",",\"map\":"+map);return;}
                if(Array.IndexOf(SonCapMaps,map)<0){if(j.Error.Length>0){j.GameAutoWanted=false;j.SonCapFailure=j.Error;j.SonCapStatus="Sơn Cáp: cần kiểm tra "+j.Error;return;}ObserveSonCap(j,map,-1,true,now);return;}
                int alive=ReadSonCapAlive(r,game,fc);if(map!=ReadDungeonMap(r,game))throw new Exception("map_changed");
                long canvasClass=ResolvedClass(r,game+0x16ed658),canvas=r.Ptr(r.Ptr(canvasClass+0xb8));bool loaded=canvas!=0&&r.Ptr(canvas+0x1b8)==fc;
                int action=ObserveSonCap(j,map,alive,loaded,now);
                if(j.SonCapMoving){long pc=ResolvedClass(r,game+0x16ee330),player=r.Ptr(r.Ptr(pc+0xb8)+8);if(player!=0)PumpSonCapPortalTravel(r,routeData,j,BitConverter.ToInt16(r.Read(player+16,2),0),BitConverter.ToInt16(r.Read(player+18,2),0),r.Ptr(fc+0x310)!=0,now);}
                if(action==1&&FarmI(r,routeData)==0&&r.Ptr(fc+0x310)==0){
                    int stage=j.SonCapStage;int x=sonCapX[stage],y=sonCapY[stage];
                    r.Write(routeData+96,BitConverter.GetBytes(map));r.Write(routeData+100,BitConverter.GetBytes(x));r.Write(routeData+104,BitConverter.GetBytes(y));r.Write(routeData+4,BitConverter.GetBytes(1));r.Write(routeData,BitConverter.GetBytes(8));j.SonCapMoving=true;j.SonCapRouteAt=now;
                    Log(j,"soncap_portal",",\"map\":"+map+",\"next_map\":"+SonCapMaps[stage+1]+",\"x\":"+x+",\"y\":"+y);
                }
            }catch(Exception e){j.SonCapAlive=-1;j.SonCapClearedStage=-1;j.SonCapEmptyAt=DateTime.MinValue;j.SonCapStatus="Sơn Cáp: chờ đọc dữ liệu map/quái • "+e.Message;
                if((now-j.SonCapReadErrorAt).TotalSeconds>=5){j.SonCapReadErrorAt=now;j.SonCapReadError=e.Message;Log(j,"soncap_read_error",",\"map\":"+j.MapId+",\"reason\":"+Quote(e.Message));}
            }
        }
    }
}

namespace TGLL {
    internal sealed class PortalRightOperation {
        internal volatile bool Completed,Success;
        internal volatile string Error="";
    }
    // Nhấn phím phải có giới hạn, luôn nhả phím, dùng chung khóa cho nhiều acc.
    internal static class SonCapPortalInput {
        static readonly object Gate=new object();
        [StructLayout(LayoutKind.Sequential)]struct MouseInput {internal int x,y;internal uint data,flags,time;internal UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)]struct KeyboardInput {internal ushort key,scan;internal uint flags,time;internal UIntPtr extra;}
        [StructLayout(LayoutKind.Explicit)]struct InputUnion {[FieldOffset(0)]internal MouseInput mouse;[FieldOffset(0)]internal KeyboardInput keyboard;}
        [StructLayout(LayoutKind.Sequential)]struct Input {internal uint type;internal InputUnion value;}
        [DllImport("user32.dll")]static extern uint SendInput(uint n,Input[] inputs,int size);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr hwnd,int command);
        [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll")]static extern bool AttachThreadInput(uint from,uint to,bool attach);
        [DllImport("user32.dll")]static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr SetActiveWindow(IntPtr hwnd);
        // Luôn tháo liên kết luồng, kể cả khi đưa cửa sổ lên trước thất bại.
        internal static bool FocusAttached(uint current,uint previous,uint target,Func<uint,uint,bool,bool> attach,Func<bool> activate){
            bool a=false,b=false;
            try{
                if(previous!=0&&previous!=current)a=attach(current,previous,true);
                if(target!=0&&target!=current&&target!=previous)b=attach(current,target,true);
                return activate();
            }finally{try{if(b)attach(current,target,false);}finally{if(a)attach(current,previous,false);}}
        }
        static bool FocusWindow(IntPtr hwnd){
            if(GetForegroundWindow()==hwnd)return true;
            SetForegroundWindow(hwnd);Thread.Sleep(80);if(GetForegroundWindow()==hwnd)return true;
            uint unused;uint current=GetCurrentThreadId(),previous=GetWindowThreadProcessId(GetForegroundWindow(),out unused),target=GetWindowThreadProcessId(hwnd,out unused);
            return FocusAttached(current,previous,target,AttachThreadInput,delegate(){BringWindowToTop(hwnd);SetActiveWindow(hwnd);SetForegroundWindow(hwnd);Thread.Sleep(100);return GetForegroundWindow()==hwnd;});
        }
        static void Right(bool up){var input=new Input{type=1};input.value.keyboard.key=0x27;input.value.keyboard.flags=(uint)(1|(up?2:0));if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1)throw new Exception("soncap_right_input_failed");}
        internal static PortalRightOperation BeginRelease(NativeJob j){return Begin(j,true);}
        internal static PortalRightOperation Begin(NativeJob j){return Begin(j,false);}
        static PortalRightOperation Begin(NativeJob j,bool releaseOnly){
            var op=new PortalRightOperation();var thread=new Thread(delegate(){
                lock(Gate){IntPtr hwnd=IntPtr.Zero,previous=IntPtr.Zero;bool down=false,visible=true,minimized=false;
                    try{
                        if(!(releaseOnly?NativeBackend.SonCapReleaseAllowed(j):NativeBackend.SonCapNudgeAllowed(j)))throw new Exception("soncap_right_cancelled");
                        hwnd=ManagedGames.PortalWindow(j.ProcessId);if(hwnd==IntPtr.Zero)throw new Exception("soncap_window_not_ready");
                        if((GetAsyncKeyState(0x27)&0x8000)!=0)throw new Exception("soncap_right_key_busy");
                        previous=GetForegroundWindow();visible=IsWindowVisible(hwnd);minimized=IsIconic(hwnd);
                        if(!visible||minimized){ShowWindowAsync(hwnd,minimized?9:5);Thread.Sleep(100);}
                        if(!FocusWindow(hwnd))throw new Exception("soncap_window_not_foreground");
                        if(!(releaseOnly?NativeBackend.SonCapReleaseAllowed(j):NativeBackend.SonCapNudgeAllowed(j)))throw new Exception("soncap_right_cancelled");
                        if(releaseOnly){Right(true);Thread.Sleep(200);op.Success=true;return;}
                        Right(false);down=true;var timer=Stopwatch.StartNew();
                        while(timer.ElapsedMilliseconds<250&&NativeBackend.SonCapNudgeAllowed(j)&&GetForegroundWindow()==hwnd)Thread.Sleep(20);
                        if(GetForegroundWindow()!=hwnd)throw new Exception("soncap_window_focus_lost");
                        op.Success=true;
                    }catch(Exception e){op.Error=e.Message;}
                    finally{
                        if(down){try{Right(true);if(GetForegroundWindow()==hwnd)Thread.Sleep(200);}catch{op.Error="soncap_right_release_failed";op.Success=false;}}
                        try{if(hwnd!=IntPtr.Zero&&previous!=IntPtr.Zero&&previous!=hwnd&&GetForegroundWindow()==hwnd)FocusWindow(previous);}catch{}
                        try{if(hwnd!=IntPtr.Zero){if(!visible)ShowWindowAsync(hwnd,0);else if(minimized)ShowWindowAsync(hwnd,6);}}catch{}finally{op.Completed=true;}
                    }
                }
            });thread.IsBackground=true;thread.Start();return op;
        }
    }
}

namespace TGLL {
    public static partial class NativeBackend {
        // Xác nhận trực tiếp map 89, không phụ thuộc trưởng nhóm đã bắt được gói vào hang.
        internal static void ObserveIzanamiMapAuto(NativeJob j,int map,DateTime now){
            bool eligible=j.IzanamiEnabled&&!j.SonCapEnabled&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&j.TrainReady&&j.GameSettingsReady;
            if(!eligible||map!=89){
                if(j.IzanamiMapAutoActive&&map!=89)j.GameAutoWanted=false;
                j.IzanamiMapAutoActive=false;j.IzanamiMapSeenAt=DateTime.MinValue;return;
            }
            if(j.IzanamiMapSeenAt==DateTime.MinValue){
                j.IzanamiMapSeenAt=now;if(j.EntryAutoOrigin<0||j.EntryAutoOrigin==89)j.EntryAutoOrigin=59;
                j.EntryAutoKind=2;j.EntryCombatMap=89;j.EntryAutoWaiting=false;j.IzanamiRoundEntered=true;j.IzanamiRoundComplete=false;j.IzanamiNoAttempts=false;j.IzanamiLimitArmed=false;j.IzanamiRequested=false;j.IzanamiTravelReady=true;j.IzanamiReturnAt=DateTime.MinValue;
                j.GameAutoManaged=true;j.GameAutoWanted=j.GameAutoEnabled;j.TrainEnabled=false;
                Log(j,"izanami_map89_seen",",\"map\":89,\"delay_seconds\":3");
            }
            if((now-j.IzanamiMapSeenAt).TotalSeconds<3){j.GameAutoStatus="Đã ở map 89; đợi đủ 3 giây để bật treo";return;}
            if(!j.IzanamiMapAutoActive){j.EntryAutoActivations++;Log(j,"izanami_map89_auto_enabled",",\"map\":89");}
            j.IzanamiMapAutoActive=true;j.ReceiveFlowActive=false;j.ReceiveKeyRequested=false;j.ReplayRequested=false;j.GameAutoManaged=true;j.GameAutoWanted=true;j.TrainEnabled=false;j.State="izanami_running";j.Stage="izanami_map89_combat";j.GameAutoStatus="Cấm thuật map 89: tự bật treo sau 3 giây";
        }
        internal static int ParseIzanamiStage(string text){
            StringBuilder b=new StringBuilder();foreach(char c in (text??"").ToLowerInvariant().Normalize(NormalizationForm.FormD))if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark)b.Append(c);
            var m=System.Text.RegularExpressions.Regex.Match(b.ToString(),@"vong\s+lap\s+ao\s+tuong\s+lan\s*[:：]?\s*(\d+)");int stage;return m.Success&&Int32.TryParse(m.Groups[1].Value,out stage)&&stage>0&&stage<=1000?stage:0;
        }
        internal static void ObserveIzanamiStage(NativeJob j,int map,Dictionary<string,string> notices){
            if(map!=89){j.IzanamiStage=0;j.IzanamiStageSeen=new HashSet<string>(notices.Keys);return;}
            foreach(var entry in notices)if(j.IzanamiStageSeen.Add(entry.Key)){int stage=ParseIzanamiStage(entry.Value);if(stage>j.IzanamiStage){j.IzanamiStage=stage;Log(j,"izanami_stage",",\"stage\":"+stage);}}
        }
        public static string IzanamiStageStatus(NativeJob j){
            if(j.CloseRequested)return "Cấm thuật: đang tắt game";
            if(j.MapId==89)return j.IzanamiStage>0?"Cấm thuật: ải "+j.IzanamiStage:"Cấm thuật: chờ thông báo ải";
            if(j.IzanamiNoAttempts)return "Cấm thuật: hết lượt tham gia";
            if(j.IzanamiRoundComplete)return "Cấm thuật: đã xong "+j.IzanamiRoundsCompleted+"/"+j.IzanamiRoundsTarget+" lượt"+(j.IzanamiRunFinished?"":" • chờ kiểm tra nhóm cho lượt tiếp");
            if(j.MapId<0)return "Cấm thuật: chờ đọc bản đồ";
            return j.IzanamiRoundEntered?"Cấm thuật: đã rời map 89 • chờ xác nhận kết thúc lượt":"Cấm thuật: chờ vào map 89";
        }
        static void PumpIzanamiMapAuto(Remote r,long game,long data,NativeJob j,Stopwatch timer){
            if(!j.IzanamiEnabled||j.SonCapEnabled){j.IzanamiMapAutoActive=false;j.IzanamiMapSeenAt=DateTime.MinValue;return;}
            DateTime now=DateTime.UtcNow;if((now-j.IzanamiMapPollAt).TotalSeconds<1)return;j.IzanamiMapPollAt=now;
            try{
                int map=ReadDungeonMap(r,game);j.MapId=map;ObserveIzanamiMapAuto(j,map,now);ObserveIzanamiStage(j,map,ReadKeyNotifications(r,game));
                if(map==89&&j.IzanamiMapSeenAt!=DateTime.MinValue&&(timer.IsRunning||j.TravelActive)){
                    CancelTravel(r,j);j.ReceiveFlowActive=false;j.ReceiveKeyRequested=false;r.Write(data,BitConverter.GetBytes(12));timer.Stop();
                }
            }catch{j.IzanamiMapSeenAt=DateTime.MinValue;j.IzanamiMapAutoActive=false;}
        }
    }
}

namespace TGLL {
    internal sealed class ChestOffer {
        internal int SaleId,TemplateId,LockedGoldPrice;internal bool LockedOnly;internal string Name;
    }
    public static partial class NativeBackend {
        public static void StartStoneChest(string id,int quantity,bool openAfter,bool useOnly){
            NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested||!j.TrainReady)throw new Exception("Mở acc và chờ nhân vật vào game trước.");
            if(quantity<1||quantity>100000)throw new Exception("Số lượng phải từ 1 đến 100000.");
            if(j.CloakEnabled)throw new Exception("Dừng mua áo trước khi mua/mở rương.");if(j.ChestEnabled||j.ChestUseOwned)throw new Exception("Tác vụ rương đang chạy; dừng hoặc đợi hoàn tất.");
            if(j.MailEnabled||j.MailBusy||j.MailQueueOnly)throw new Exception("Dừng gom đồ trước khi mua hoặc mở rương.");
            j.ChestQuantity=quantity;j.ChestOpenAfter=openAfter;j.ChestUseOnly=useOnly;j.ChestBought=0;j.ChestUsed=0;j.ChestRemaining=0;j.ChestPhase=0;j.ChestUnitPrice=0;j.ChestBefore=0;j.ChestAt=DateTime.MinValue;j.ChestPollAt=DateTime.MinValue;j.ChestUseOwned=false;j.ChestStatus=useOnly?"Đợi đọc rương đang có trong túi":"Đợi đọc cửa hàng Vàng khóa";j.ChestEnabled=true;
            Log(j,"stone_chest_start",",\"quantity\":"+quantity+",\"open_after\":"+(openAfter?"true":"false")+",\"use_only\":"+(useOnly?"true":"false"));
        }
        public static void StopStoneChest(string id){NativeJob j=Get(id);if(j==null)return;j.ChestEnabled=false;j.ChestStatus=j.ChestPhase==2?"Đã dừng; lệnh mua có thể đã gửi, kiểm tra túi trước khi mua lại":"Đã dừng mua/mở rương";}
        internal static long StoneChestCount(InventorySnapshot bag){if(bag==null||!bag.Ready)throw new Exception("inventory_not_ready");return bag.Items.Where(i=>i.TemplateId==161&&i.Quantity>0).Sum(i=>(long)i.Quantity);}
        internal static bool VerifiedLockedChest(ChestOffer offer){return offer!=null&&offer.SaleId>=0&&offer.TemplateId==161&&offer.LockedOnly&&offer.LockedGoldPrice>0&&String.Equals((offer.Name??"").Normalize(NormalizationForm.FormC).Trim(),"Rương Tinh Thạch",StringComparison.OrdinalIgnoreCase);}
        internal static ChestOffer ReadLockedChestOffer(Remote r,long game){
            long cls=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(cls+0xb8)+16),canvasClass=ResolvedClass(r,game+0x16ed658),canvas=r.Ptr(r.Ptr(canvasClass+0xb8));if(fc==0||canvas==0)throw new Exception("shop_not_ready");
            long source=r.Ptr(fc+0xe8),templates=r.Ptr(canvas+0x118);int count=BitConverter.ToInt32(r.Read(templates+24,4),0);
            foreach(long item in ReadVector(r,source)){
                if(item==0)continue;byte[] b=r.Read(item+16,84);int id=BitConverter.ToInt32(b,4);if(id!=161||id>=count)continue;
                long template=r.Ptr(templates+32+id*8);string name=ReadLabel(r,r.Ptr(template+24));
                int price=BitConverter.ToInt32(b,60);bool only=price>0&&b[81]!=0;
                foreach(int off in new[]{48,52,56,64,68,72,76})if(BitConverter.ToInt32(b,off)!=0)only=false;
                var offer=new ChestOffer{SaleId=BitConverter.ToInt32(b,0),TemplateId=id,LockedGoldPrice=price,LockedOnly=only,Name=name};
                if(!VerifiedLockedChest(offer))continue;
                if(!SameBytes(b,r.Read(item+16,84))||r.Ptr(fc+0xe8)!=source)throw new Exception("shop_changed");return offer;
            }return null;
        }
        static void FinishStoneChest(NativeJob j,string status){j.ChestEnabled=false;j.ChestUseOwned=false;j.ChestStatus=status;Log(j,"stone_chest_stopped",",\"status\":"+Quote(status)+",\"bought\":"+j.ChestBought+",\"used\":"+j.ChestUsed);}
        internal static void PumpStoneChest(Remote r,long buyData,long useData,long game,NativeJob j,Func<InventorySnapshot> bagReader=null,Func<ChestOffer> offerReader=null,DateTime? clock=null){
            if(buyData==0||useData==0)return;DateTime now=clock??DateTime.UtcNow;
            bool ready=j.ChestEnabled&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&j.GameSettingsReady&&!AuxiliaryActionBlocked(j)&&!j.ZoneSyncActive&&!j.GiftWaiting&&!j.GiftResultWaiting&&!j.MailEnabled&&!j.MailBusy;
            r.Write(buyData+12,BitConverter.GetBytes(ready?1:0));
            if(!j.ChestEnabled||j.CloseRequested||j.CancelRequested||j.ReconnectRequested){
                if(j.ChestUseOwned){r.Write(useData+12,new byte[4]);if(FarmI(r,useData)==0)j.ChestUseOwned=false;}return;
            }
            if(j.ChestUseOwned)r.Write(useData+12,BitConverter.GetBytes(ready?1:0));
            if(now<j.ChestPollAt)return;j.ChestPollAt=now.AddSeconds(1);
            try{
                if(j.ChestPhase==0){
                    if(!ready||FarmI(r,useData)!=0||FarmI(r,buyData)!=0){j.ChestStatus="Đợi game/thao tác đang chạy hoàn tất";return;}
                    long amount=StoneChestCount(bagReader==null?ReadInventorySnapshot(r,game):bagReader());j.ChestBefore=amount;
                    if(j.ChestUseOnly){j.ChestRemaining=amount;j.ChestPhase=3;j.ChestStatus="Đã đọc "+amount+" rương trong túi";return;}
                    r.Write(buyData+4,BitConverter.GetBytes(1));r.Write(buyData+24,new byte[4]);r.Write(buyData,BitConverter.GetBytes(1));j.ChestPhase=1;j.ChestAt=now;j.ChestStatus="Đang đọc cửa hàng Vàng khóa";return;
                }
                if(j.ChestPhase==1){
                    if(!ready||FarmI(r,buyData)!=0)return;if(FarmI(r,buyData+24)!=2){FinishStoneChest(j,"Chưa đọc được cửa hàng; chưa gửi mua");return;}ChestOffer offer=offerReader==null?ReadLockedChestOffer(r,game):offerReader();
                    if(!VerifiedLockedChest(offer)){j.ChestStatus="Chờ dữ liệu Rương Tinh Thạch mua bằng vàng khóa";if((now-j.ChestAt).TotalSeconds>=20)FinishStoneChest(j,"Chưa xác minh được rương/vàng khóa; chưa gửi mua");return;}
                    j.ChestBefore=StoneChestCount(bagReader==null?ReadInventorySnapshot(r,game):bagReader());j.ChestUnitPrice=offer.LockedGoldPrice;j.ChestSaleId=offer.SaleId;
                    r.Write(buyData+4,BitConverter.GetBytes(2));r.Write(buyData+8,BitConverter.GetBytes(j.ChestQuantity));r.Write(buyData+20,BitConverter.GetBytes(offer.SaleId));r.Write(buyData+24,new byte[4]);r.Write(buyData,BitConverter.GetBytes(1));j.ChestPhase=2;j.ChestAt=now;j.ChestStatus="Đã xếp mua "+j.ChestQuantity+" rương • "+((long)j.ChestUnitPrice*j.ChestQuantity)+" vàng khóa; chờ túi tăng";
                    Log(j,"stone_chest_buy_queued",",\"sale_id\":"+offer.SaleId+",\"template_id\":161,\"quantity\":"+j.ChestQuantity+",\"locked_gold_price\":"+offer.LockedGoldPrice);return;
                }
                if(j.ChestPhase==2){
                    if(FarmI(r,buyData)!=0)return;
                    if(FarmI(r,buyData+24)!=3){FinishStoneChest(j,"Lệnh mua chưa gửi; không tự thử lại");return;}
                    long amount=StoneChestCount(bagReader==null?ReadInventorySnapshot(r,game):bagReader()),delta=amount-j.ChestBefore;
                    if(delta==j.ChestQuantity){j.ChestBought=j.ChestQuantity;j.ChestRemaining=j.ChestOpenAfter?j.ChestQuantity:0;j.ChestPhase=3;Log(j,"stone_chest_buy_confirmed",",\"quantity\":"+j.ChestBought);if(!j.ChestOpenAfter)FinishStoneChest(j,"Đã mua "+j.ChestBought+" rương bằng vàng khóa; giữ trong túi");return;}
                    j.ChestStatus="Chờ xác nhận mua: túi tăng "+Math.Max(0,delta)+"/"+j.ChestQuantity;
                    if((now-j.ChestAt).TotalSeconds>=20)FinishStoneChest(j,"Chưa xác nhận đủ rương; kiểm tra thông báo/túi trước khi mua lại (không tự mua lặp)");return;
                }
                if(j.ChestPhase==3){
                    if(j.ChestRemaining<=0){FinishStoneChest(j,"Hoàn tất • đã mua "+j.ChestBought+" • đã mở "+j.ChestUsed+" rương");return;}
                    if(!ready||FarmI(r,useData)!=0)return;
                    InventorySnapshot bag=bagReader==null?ReadInventorySnapshot(r,game):bagReader();j.ChestBefore=StoneChestCount(bag);if(j.ChestBefore<=0){FinishStoneChest(j,"Không còn Rương Tinh Thạch trong túi");return;}
                    InventoryItem stack=bag.Items.First(i=>i.TemplateId==161&&i.Quantity>0);j.ChestBatch=(int)Math.Min(1000,Math.Min(j.ChestRemaining,stack.Quantity));
                    r.Write(useData+24,BitConverter.GetBytes(j.ChestBatch));r.Write(useData+28,new byte[4]);r.Write(useData+4,BitConverter.GetBytes(161));r.Write(useData+8,new byte[4]);r.Write(useData+12,BitConverter.GetBytes(1));r.Write(useData,BitConverter.GetBytes(1));j.ChestUseOwned=true;j.ChestPhase=4;j.ChestAt=now;j.ChestStatus="Đang mở "+j.ChestBatch+" Rương Tinh Thạch trong một lượt";Log(j,"stone_chest_batch_queued",",\"quantity\":"+j.ChestBatch);return;
                }
                if(j.ChestPhase==4){
                    if(FarmI(r,useData)!=0)return;
                    if(FarmI(r,useData+8)!=2){FinishStoneChest(j,"Chưa gửi được sử dụng rương; kiểm tra ô túi");return;}
                    long amount=StoneChestCount(bagReader==null?ReadInventorySnapshot(r,game):bagReader()),delta=j.ChestBefore-amount;
                    if(delta>=j.ChestBatch){long used=Math.Min(delta,j.ChestRemaining);j.ChestUsed+=used;j.ChestRemaining-=used;j.ChestUseOwned=false;j.ChestPhase=3;j.ChestPollAt=now.AddSeconds(2);j.ChestStatus="Đã mở "+j.ChestUsed+" • còn "+j.ChestRemaining;Log(j,"stone_chest_used",",\"used\":"+j.ChestUsed+",\"remaining\":"+j.ChestRemaining);return;}
                    j.ChestStatus="Chờ xác nhận mở cả lượt: "+Math.Max(0,delta)+"/"+j.ChestBatch;
                    if((now-j.ChestAt).TotalSeconds>=30){if(delta>0){long used=Math.Min(delta,j.ChestRemaining);j.ChestUsed+=used;j.ChestRemaining-=used;}FinishStoneChest(j,"Chưa xác nhận đủ lượt mở; đã giảm "+Math.Max(0,delta)+"/"+j.ChestBatch+" rương; kiểm tra túi (không gửi dùng lặp)");}return;
                }
            }catch(Exception e){j.ChestStatus="Chờ đọc dữ liệu rương: "+e.Message;if(j.ChestAt!=DateTime.MinValue&&(now-j.ChestAt).TotalSeconds>=30)FinishStoneChest(j,"Dừng để kiểm tra dữ liệu rương; không tự mua lại");}
        }
    }
    internal static partial class NativeCode {
        internal static byte[] StoneChest(long data,long game){return StoneChestCore(data,game+0x16ee820,game+0x16f760,game+0x2e2aa0,game+0x1dd900,game+0x4e6b00);}
        internal static byte[] StoneChestCore(long data,long serviceSlot,long allocate,long ctor,long initialize,long requestCatalog){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x48,0x83,0xec,0x28,0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,1);a.J(0x85,"return");a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,24,1,0,0,0);a.Emit(0x83,0x7b,12,1);a.J(0x85,"done");
            a.Emit(0x83,0x7b,4,1);a.J(0x85,"buy");a.Emit(0xb9,7,0,0,0,0x33,0xd2);a.Call(requestCatalog);a.Emit(0xc7,0x43,24,2,0,0,0);a.Jump("sent");
            a.Label("buy");a.Emit(0x83,0x7b,4,2);a.J(0x85,"done");a.Emit(0x83,0x7b,8,1);a.J(0x8c,"done");a.Emit(0x81,0x7b,8);a.I(100000);a.J(0x8f,"done");a.Emit(0x83,0x7b,20,0);a.J(0x8c,"done");
            Slot(a,serviceSlot,"done",initialize);a.Emit(0x48,0x89,0xc1);a.Call(allocate);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0xc6,0x48,0x89,0xc1,0xba,0x79,0,0,0,0x45,0x33,0xc0);a.Call(ctor);
            // fi.rk / fw.rk: opcode 0x79, WriteInt(sale ID), WriteInt(quantity), flush.
            foreach(int offset in new[]{20,8}){a.Emit(0x48,0x8b,0x06,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf1,0x8b,0x53,(byte)offset,0xff,0x90,0x48,2,0,0);}
            a.Emit(0x48,0x8b,0x06,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x89,0xf1,0xff,0x90,0x98,2,0,0,0xc7,0x43,24,3,0,0,0);
            a.Label("sent");a.Emit(0xff,0x43,16);a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x28,0x5e,0x5b,0xc3);return a.Finish();
        }
    }
}

namespace TGLL {
 public sealed class CloakOffer {
  public int TemplateId,SaleId,CrystalPrice;public string Name;
  public override string ToString(){return Name+" • "+CrystalPrice+" tinh thạch";}
 }
 public static partial class NativeBackend {
  internal static readonly int[] CloakIds={128,129,130,131,132,133,535,345,124};
  public static CloakOffer[] DefaultCloakOffers(){int[] prices={300,500,700,900,2000,2500,3000,6000,4000};string[] names={"Áo Choàng Hokage Đệ Lục","Áo Choàng Hokage Đệ Ngũ","Áo Choàng Hokage Đệ Tứ","Áo Choàng Hokage Đệ Tam","Áo Choàng Hokage Đệ Nhị","Áo Choàng Hokage Đệ Nhất","Áo Choàng Hokage Làng Lá","Áo Choàng Tiên Nhân","Áo Choàng Hiền Nhân"};return CloakIds.Select((id,i)=>new CloakOffer{TemplateId=id,SaleId=314+i,CrystalPrice=prices[i],Name=names[i]}).ToArray();}
  internal static bool ValidCloakOffer(CloakOffer o){return o!=null&&CloakIds.Contains(o.TemplateId)&&o.SaleId>=0&&o.CrystalPrice>0&&DefaultCloakOffers().Any(x=>x.TemplateId==o.TemplateId&&String.Equals(x.Name.Normalize(NormalizationForm.FormC),o.Name==null?"":o.Name.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase));}
  internal static CloakOffer[] ReadCloakOffers(Remote r,long game){
   if(ReadDungeonMap(r,game)!=86)throw new Exception("Nhân vật chưa ở Trường Konoha Gakuen (map 86).");
   long fc=r.Ptr(r.Ptr(ResolvedClass(r,game+0x16ee888)+0xb8)+16),canvas=r.Ptr(r.Ptr(ResolvedClass(r,game+0x16ed658)+0xb8)),templates=r.Ptr(canvas+0x118),source=r.Ptr(fc+0xe8);var offers=new List<CloakOffer>();int count=FarmI(r,templates+24);
   foreach(long item in ReadVector(r,source)){
    if(item==0)continue;byte[] b=r.Read(item+16,84);int id=BitConverter.ToInt32(b,4);if(!CloakIds.Contains(id)||id<0||id>=count)continue;
    int price=BitConverter.ToInt32(b,64);bool only=price>0;foreach(int off in new[]{48,52,56,60,68,72,76})if(BitConverter.ToInt32(b,off)!=0)only=false;if(!only)continue;
    var offer=new CloakOffer{TemplateId=id,SaleId=BitConverter.ToInt32(b,0),CrystalPrice=price,Name=ReadLabel(r,r.Ptr(r.Ptr(templates+32+id*8)+24))};
    if(!ValidCloakOffer(offer)||!SameBytes(b,r.Read(item+16,84))||r.Ptr(fc+0xe8)!=source)throw new Exception("Dữ liệu cửa hàng thay đổi; chưa mua.");offers.Add(offer);
   }return offers.ToArray();
  }
  public static CloakOffer[] GetCloakOffers(string id){NativeJob j=Get(id);if(j==null||!j.TrainReady||j.ProcessId<=0)throw new Exception("Mở acc và chờ vào game.");using(var p=Process.GetProcessById(j.ProcessId))using(var r=new Remote(p)){long game=Module(p,"GameAssembly.dll").BaseAddress.ToInt64();return ReadCloakOffers(r,game);}}
  internal static long CountCloaks(InventorySnapshot bag,int id){if(bag==null||!bag.Ready)throw new Exception("Chưa đọc được túi.");return bag.Items.Where(x=>x.TemplateId==id).Sum(x=>(long)Math.Max(1,x.Quantity));}
  public static void StartCloakPurchase(string id,int templateId,int quantity,int expectedPrice){StartCloakCore(id,templateId,quantity,expectedPrice,false);}
  static void StartCloakCore(string id,int templateId,int quantity,int expectedPrice,bool ownedOnly){
   NativeJob j=Get(id);if(j==null||!j.Auto||!j.TrainReady||j.CloseRequested||j.CancelRequested)throw new Exception("Acc chưa sẵn sàng.");
   if(!CloakIds.Contains(templateId)||quantity<1||quantity>100||expectedPrice<=0)throw new Exception("Chọn đúng áo và số lượng 1–100.");
   if(j.CloakEnabled)throw new Exception("Tác vụ mua áo đang chạy.");
   if(j.FarmEnabled||j.IzanamiEnabled||j.SonCapEnabled||j.DungeonHighEnabled||j.ReceiveKeyEnabled||j.QuestEnabled||j.MailEnabled||j.MailBusy||j.GiftEnabled||j.ChestEnabled||AuxiliaryActionBlocked(j))throw new Exception("Dừng Up/các auto hoạt động, gom đồ, gift code hoặc rương trước khi mua áo.");
   j.CloakDone=false;j.CloakEquipSlot=-1;j.CloakId=templateId;j.CloakQuantity=ownedOnly?0:quantity;if(ownedOnly)j.CloakEquipAfter=true;j.CloakExpectedPrice=expectedPrice;j.CloakBought=0;j.CloakPhase=ownedOnly?9:0;j.CloakAt=DateTime.MinValue;j.CloakPollAt=DateTime.MinValue;j.CloakStatus=ownedOnly?"Dùng áo có HSD dài nhất trong túi; không mua thêm":"Đợi về Trường Konoha Gakuen";j.CloakEnabled=true;Log(j,"cloak_start",",\"template_id\":"+templateId+",\"quantity\":"+quantity+",\"expected_price\":"+expectedPrice);
  }
  public static void StopCloakPurchase(string id){NativeJob j=Get(id);if(j==null)return;j.CloakEnabled=false;j.CloakStatus=j.CloakPhase==8?"Đã dừng; lệnh mua có thể đã gửi, kiểm tra túi trước khi mua lại":"Đã dừng mua áo";}
  static void FinishCloak(NativeJob j,string status){j.CloakEnabled=false;j.CloakStatus=status;Log(j,"cloak_stopped",",\"status\":"+Quote(status)+",\"bought\":"+j.CloakBought);}
  internal static void QueueCloakAction(Remote r,long data,int kind){r.Write(data+40,BitConverter.GetBytes(kind));r.Write(data+36,new byte[4]);r.Write(data,BitConverter.GetBytes(1));}
  internal static void PumpCloak(Remote r,long data,long game,NativeJob j,Func<int> mapReader=null,Func<InventorySnapshot> bagReader=null,Func<CloakOffer[]> offerReader=null,DateTime? clock=null,Func<long> wornReader=null,Func<long> expiryReader=null){
   if(data==0)return;DateTime now=clock??DateTime.UtcNow;long use=data+256,buy=data+512,equip=data+768;
   bool enabled=j.CloakEnabled&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested;
   bool ready=enabled&&j.GameSettingsReady&&!j.ReceiveFlowActive&&!j.TravelActive&&!j.KeyReceiptPending&&!j.ReplayRequested&&!j.IzanamiEnabled&&!j.SonCapEnabled&&!j.FarmEnabled&&!j.DungeonHighEnabled&&!j.ReceiveKeyEnabled&&!j.QuestEnabled&&!j.ZoneSyncActive&&!j.MailEnabled&&!j.MailBusy&&!j.GiftWaiting&&!j.ChestEnabled;
   r.Write(data+12,BitConverter.GetBytes(ready?1:0));r.Write(use+12,BitConverter.GetBytes(ready?1:0));r.Write(buy+12,BitConverter.GetBytes(ready?1:0));r.Write(equip+12,BitConverter.GetBytes(ready?1:0));
   if(!enabled)return;if(now<j.CloakPollAt)return;j.CloakPollAt=now.AddSeconds(1);
   try{
    int map=mapReader==null?ReadDungeonMap(r,game):mapReader();
    if(j.CloakPhase>=4&&map!=86){FinishCloak(j,"Đã rời Trường Konoha Gakuen; dừng mua, kiểm tra túi.");return;}
    if(j.CloakPhase==0){
     if(!ready||FarmI(r,data)!=0||FarmI(r,use)!=0||FarmI(r,buy)!=0)return;
     if(map==86){j.CloakPhase=4;j.CloakAt=now;return;}
     InventorySnapshot bag=bagReader==null?ReadInventorySnapshot(r,game):bagReader();if(!bag.Ready)throw new Exception("Túi chưa sẵn sàng");if(!bag.Items.Any(x=>x.TemplateId==168&&x.Quantity>0)){FinishCloak(j,"Cần phù về làng ID 168 để về Trường Konoha Gakuen.");return;}
     r.Write(use+4,BitConverter.GetBytes(168));r.Write(use+8,new byte[4]);r.Write(use+24,BitConverter.GetBytes(1));r.Write(use,BitConverter.GetBytes(1));j.CloakPhase=1;j.CloakAt=now;j.CloakStatus="Đang dùng phù để về Trường Konoha Gakuen";return;
    }
    if(j.CloakPhase==1){if(FarmI(r,use)!=0)return;if(FarmI(r,use+8)!=2){FinishCloak(j,"Chưa dùng được phù; không thử lại tự động.");return;}j.CloakPhase=2;j.CloakAt=now;QueueCloakAction(r,data,2);return;}
    if(j.CloakPhase==2||j.CloakPhase==6){
     if(FarmI(r,data)!=0)return;if(FarmI(r,data+36)==1){j.CloakPhase=j.CloakPhase==2?3:7;j.CloakAt=now;return;}
     if((now-j.CloakAt).TotalSeconds>=30){FinishCloak(j,"Chưa thấy đúng menu trường/Áo choàng; dừng để kiểm tra.");return;}
     if(ready){QueueCloakAction(r,data,j.CloakPhase==2?2:3);j.CloakPollAt=now.AddSeconds(2);}return;
    }
    if(j.CloakPhase==3){if(map==86){j.CloakPhase=4;j.CloakAt=now;j.CloakStatus="Đã về trường; tìm Senju Tobirama";}else if((now-j.CloakAt).TotalSeconds>=60)FinishCloak(j,"Chưa xác nhận về map 86; dừng để kiểm tra.");return;}
    if(j.CloakPhase==4){if(!ready||FarmI(r,data)!=0)return;QueueCloakAction(r,data,1);j.CloakPhase=5;j.CloakAt=now;j.CloakStatus="Đang gọi Senju Tobirama • chọn Áo choàng";return;}
    if(j.CloakPhase==5){if(FarmI(r,data)!=0)return;if(FarmI(r,data+36)!=1){FinishCloak(j,"Chưa tìm được Senju Tobirama (NPC 92); chưa mua.");return;}QueueCloakAction(r,data,3);j.CloakPhase=6;j.CloakAt=now;return;}
    if(j.CloakPhase==7){
     if(!ready||FarmI(r,buy)!=0)return;var offers=offerReader==null?ReadCloakOffers(r,game):offerReader();var offer=offers.FirstOrDefault(x=>x.TemplateId==j.CloakId&&ValidCloakOffer(x));
     if(offer==null){j.CloakStatus="Chờ đúng danh mục Áo choàng";if((now-j.CloakAt).TotalSeconds>=20)FinishCloak(j,"Chưa xác minh được áo trong cửa hàng; chưa mua.");return;}
     if(offer.CrystalPrice!=j.CloakExpectedPrice){FinishCloak(j,"Giá hiện tại "+offer.CrystalPrice+" khác giá đã chọn "+j.CloakExpectedPrice+"; đọc lại giá trước khi mua.");return;}
     InventorySnapshot bag=bagReader==null?ReadInventorySnapshot(r,game):bagReader();j.CloakBefore=CountCloaks(bag,j.CloakId);j.CloakBeforeSlots=bag.Items.Where(x=>x.TemplateId==j.CloakId).Select(x=>x.Slot).ToArray();if(bag.Items.Length>=bag.Capacity){FinishCloak(j,"Túi đầy; chưa mua áo.");return;}
     r.Write(buy+4,BitConverter.GetBytes(2));r.Write(buy+8,BitConverter.GetBytes(1));r.Write(buy+20,BitConverter.GetBytes(offer.SaleId));r.Write(buy+24,new byte[4]);r.Write(buy,BitConverter.GetBytes(1));j.CloakPhase=8;j.CloakAt=now;j.CloakStatus="Đã xếp mua "+offer.Name+" • "+offer.CrystalPrice+" tinh thạch; chờ áo vào túi";Log(j,"cloak_buy_queued",",\"template_id\":"+offer.TemplateId+",\"sale_id\":"+offer.SaleId+",\"crystal_price\":"+offer.CrystalPrice);return;
    }
    if(j.CloakPhase==8){
     if(FarmI(r,buy)!=0)return;if(FarmI(r,buy+24)!=3){FinishCloak(j,"Chưa gửi mua; kiểm tra lại trường/menu.");return;}
     InventorySnapshot after=bagReader==null?ReadInventorySnapshot(r,game):bagReader();long count=CountCloaks(after,j.CloakId);
     if(count-j.CloakBefore==1){j.CloakBought++;Log(j,"cloak_buy_confirmed",",\"bought\":"+j.CloakBought);if(j.CloakBought>=j.CloakQuantity){if(j.CloakEquipAfter){InventoryItem fresh=LongestCloak(after,j.CloakId,now);if(fresh==null||fresh.Slot<0||fresh.Slot>255){FinishCloak(j,"Đã mua nhưng chưa xác minh được ô áo mới; giữ game để kiểm tra.");return;}j.CloakEquipSlot=fresh.Slot;j.CloakEquipExpiry=fresh.ExpiryUtcMs;j.CloakPhase=9;j.CloakAt=now;j.CloakStatus="Đã mua xong; đang chuẩn bị mặc áo mới";return;}FinishCloak(j,"Hoàn tất • đã mua "+j.CloakBought+" áo; giữ áo trong túi");return;}j.CloakPhase=7;j.CloakAt=now;j.CloakPollAt=now.AddSeconds(2);return;}
     if((now-j.CloakAt).TotalSeconds>=20)FinishCloak(j,"Chưa xác nhận áo vào túi; kiểm tra tinh thạch/thông báo, không mua lặp.");return;
    }
    if(j.CloakPhase==9){
     if(!ready||FarmI(r,equip)!=0)return;InventorySnapshot bag=bagReader==null?ReadInventorySnapshot(r,game):bagReader();InventoryItem best=LongestCloak(bag,j.CloakId,now);
     long wornExpiry=expiryReader==null?ReadWornCloakExpiry(r,game,j.CloakId):expiryReader();
     if(wornExpiry>UnixMs(now)&&(best==null||wornExpiry>=best.ExpiryUtcMs)){j.CloakDone=true;FinishCloak(j,"Áo đang mặc có HSD dài nhất: "+CloakExpiryText(wornExpiry)+"; chờ tắt game");return;}
     if(best==null||best.Slot<0||best.Slot>255){FinishCloak(j,"Không có áo đúng loại còn hạn hoặc chưa xác minh HSD; giữ game để kiểm tra.");return;}
     j.CloakEquipSlot=best.Slot;j.CloakEquipExpiry=best.ExpiryUtcMs;j.CloakWornBefore=wornExpiry;
     r.Write(equip+4,BitConverter.GetBytes(j.CloakId));r.Write(equip+32,BitConverter.GetBytes(best.Slot));r.Write(equip+40,BitConverter.GetBytes(best.ExpiryUtcMs));r.Write(equip+8,new byte[4]);r.Write(equip,BitConverter.GetBytes(1));j.CloakPhase=10;j.CloakAt=now;j.CloakStatus="Đã gửi Sử dụng áo có HSD dài nhất: "+CloakExpiryText(best.ExpiryUtcMs)+"; chờ xác nhận";Log(j,"cloak_equip_queued",",\"slot\":"+best.Slot+",\"expiry_utc_ms\":"+best.ExpiryUtcMs+",\"worn_expiry_before\":"+wornExpiry);return;
    }
    if(j.CloakPhase==10){if(FarmI(r,equip)!=0)return;if(FarmI(r,equip+8)!=2){FinishCloak(j,"Chưa gửi được Sử dụng áo; giữ game để kiểm tra.");return;}long wornExpiry=expiryReader==null?ReadWornCloakExpiry(r,game,j.CloakId):expiryReader();if(wornExpiry>=j.CloakEquipExpiry&&wornExpiry>UnixMs(now)){j.CloakDone=true;FinishCloak(j,"Đã xác nhận mặc áo • HSD "+CloakExpiryText(wornExpiry)+"; chờ tắt game");Log(j,"cloak_equipped",",\"template_id\":"+j.CloakId+",\"expiry_utc_ms\":"+wornExpiry);return;}if((now-j.CloakAt).TotalSeconds>=20)FinishCloak(j,"Chưa xác nhận HSD áo đang mặc; giữ game, không tự tắt.");return;}

   }catch(Exception e){j.CloakStatus="Chờ dữ liệu: "+e.Message;if(j.CloakAt==DateTime.MinValue)j.CloakAt=now;if((now-j.CloakAt).TotalSeconds>=30)FinishCloak(j,"Dừng: "+e.Message+"; kiểm tra túi trước khi mua lại.");}
  }
 }
 internal static partial class NativeCode {
  internal static byte[] CloakBundle(long data,long game,long actionCode,long useCode,long buyCode,long equipCode=0){
   Code64 a=new Code64();a.Emit(0x53,0x48,0x83,0xec,0x20);a.Call(useCode);a.Call(actionCode);
   a.Emit(0x48,0xbb);a.Q(data+512);Slot(a,game+0x16ee888,"block",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"block");a.Emit(0x48,0x8b,0x40,16,0x48,0x85,0xc0);a.J(0x84,"block");a.Emit(0x66,0x83,0xb8,0x34,1,0,0,86);a.J(0x84,"buy");
   a.Label("block");a.Emit(0xc7,0x43,12,0,0,0,0);a.Label("buy");a.Call(buyCode);if(equipCode!=0)a.Call(equipCode);a.Emit(0x48,0x83,0xc4,0x20,0x5b,0xc3);return a.Finish();
  }
  internal static byte[] CloakAction(long data,long game){
   Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x48,0x83,0xec,0x20);a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,1);a.J(0x85,"return");a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,36,0,0,0,0,0x83,0x7b,12,1);a.J(0x85,"done");
   Slot(a,game+0x16ee888,"done",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x70,16,0x48,0x85,0xf6);a.J(0x84,"done");
   a.Emit(0x83,0x7b,40,2);a.J(0x84,"menu");a.Emit(0x66,0x83,0xbe,0x34,1,0,0,86);a.J(0x85,"done");a.Emit(0x83,0x7b,40,1);a.J(0x85,"menu");
   a.Emit(0x48,0x8b,0xbe,0x58,1,0,0,0x48,0x85,0xff);a.J(0x84,"done");a.Emit(0x45,0x33,0xe4);
   a.Label("npcLoop");a.Emit(0x41,0x81,0xfc,128,0,0,0);a.J(0x83,"done");VectorCount(a);a.Emit(0x41,0x39,0xc4);a.J(0x8d,"done");VectorElement(a);a.Emit(0x4d,0x85,0xed);a.J(0x84,"npcNext");Slot(a,game+0x16ee910,"done",game+0x1dd900);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"npcNext");a.Emit(0x66,0x41,0x83,0x7d,80,92);a.J(0x85,"npcNext");a.Emit(0x41,0x80,0x7d,61,6);a.J(0x84,"npcNext");a.Emit(0x41,0x8b,0x45,56,0x89,0x43,56);
   a.Emit(0x4c,0x89,0xae,0xb0,2,0,0,0x48,0x8d,0x8e,0xb0,2,0,0,0x4c,0x89,0xea);a.Call(game+0x12d9e0);a.Emit(0x4c,0x89,0xe9,0x33,0xd2);a.Call(game+0x4e19f0);a.Jump("ok");
   a.Label("npcNext");a.Emit(0x41,0xff,0xc4);a.Jump("npcLoop");
   a.Label("menu");a.Emit(0x4c,0x8b,0xae,0x90,0,0,0,0x4d,0x85,0xed);a.J(0x84,"done");Slot(a,game+0x16efe08,"done",game+0x1dd900);a.Emit(0x49,0x39,0x45,0);a.J(0x85,"done");a.Emit(0x83,0x7b,40,2);a.J(0x85,"npcTag");a.Emit(0x41,0x83,0xbd,0xdc,0,0,0,0xfc);a.J(0x85,"done");a.Jump("menuReady");
   a.Label("npcTag");a.Emit(0x41,0x8b,0x85,0xdc,0,0,0,0x3b,0x43,56);a.J(0x85,"done");a.Label("menuReady");a.Emit(0x49,0x8b,0x8d,0xa8,0,0,0,0x48,0x85,0xc9);a.J(0x84,"done");a.Emit(0x33,0xd2);
   a.Label("options");a.Emit(0x81,0xfa,128,0,0,0);a.J(0x83,"done");a.Emit(0x3b,0x51,24);a.J(0x83,"done");a.Emit(0x48,0x8b,0x44,0xd1,32,0x83,0x7b,40,2);a.J(0x85,"cloakLabel");MatchMenuText(a,":-chatTrường Konoha Gakuen","schoolPlain");a.Jump("select");a.Label("schoolPlain");MatchMenuText(a,"Trường Konoha Gakuen","optionNext");a.Jump("select");
   a.Label("cloakLabel");a.Emit(0x83,0x7b,40,3);a.J(0x85,"done");MatchMenuText(a,":-chatÁo choàng","cloakPlain");a.Jump("select");a.Label("cloakPlain");MatchMenuText(a,"Áo choàng","optionNext");
   a.Label("select");a.Emit(0x49,0x8b,0x85,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x89,0x50,96,0x41,0xc7,0x85,0xc0,0,0,0,1,0,0,0,0x4c,0x89,0xe9,0x33,0xd2);a.Call(game+0x2d8f00);a.Jump("ok");a.Label("optionNext");a.Emit(0xff,0xc2);a.Jump("options");
   a.Label("ok");a.Emit(0xc7,0x43,36,1,0,0,0);a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x41,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
  }
 }
}

namespace TGLL { internal static partial class NativeCode {
        internal static byte[] EquipItem(long data,long canvasSlot,long fcSlot,long playerGetter,long itemSlot,long use){
            Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x55,0x48,0x83,0xec,0x20);a.Emit(0x48,0xbb);a.Q(data);
            a.Emit(0x83,0x3b,1);a.J(0x85,"return");a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,8,1,0,0,0,0xc7,0x43,28,0,0,0,0);a.Emit(0x83,0x7b,12,1);a.J(0x85,"done");
            a.Rax(canvasSlot);a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0xa8,1);a.J(0x85,"done");a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0x80,0xb8,1,0,0,0x48,0x85,0xc0);a.J(0x84,"done");a.Rcx(fcSlot);a.Emit(0x48,0x8b,0x09,0x48,0x39,0x08);a.J(0x85,"done");
            a.Emit(0x33,0xc9);a.Call(playerGetter);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x8b,0xb0,0x28,1,0,0,0x48,0x85,0xf6);a.J(0x84,"done");
            a.Emit(0x48,0x83,0x7e,24,0);a.J(0x8c,"invalid");a.Emit(0x48,0x81,0x7e,24,0,2,0,0);a.J(0x8f,"invalid");a.Emit(0x33,0xff);
            a.Label("next");a.Emit(0x3b,0x7e,24);a.J(0x8d,"missing");a.Emit(0x4c,0x8b,0x64,0xfe,32,0x4d,0x85,0xe4);a.J(0x84,"skip");a.Rax(itemSlot);a.Emit(0x48,0x8b,0,0x49,0x39,0x04,0x24);a.J(0x85,"skip");
            a.Emit(0x8b,0x43,4,0x41,0x39,0x44,0x24,20);a.J(0x85,"skip");
            // Use a freshly resolved bag slot, not the item template ID.
            a.Emit(0x41,0x8b,0x44,0x24,32,0x85,0xc0);a.J(0x88,"invalid");a.Emit(0x3b,0x46,24);a.J(0x83,"invalid");a.Emit(0x4c,0x39,0x64,0xc6,32);a.J(0x85,"invalid");a.Emit(0x89,0x43,16,0x83,0x7b,12,1);a.J(0x85,"done");
            a.Emit(0x8b,0x43,16,0x3b,0x43,32);a.J(0x85,"skip");a.Emit(0x48,0x8b,0x43,40,0x48,0x85,0xc0);a.J(0x8e,"expiryUnchecked");a.Emit(0x49,0x39,0x44,0x24,48);a.J(0x85,"invalid");a.Label("expiryUnchecked");
            a.Emit(0x4c,0x89,0xe1,0x33,0xd2);a.Call(use);a.Emit(0xff,0x43,20,0xc7,0x43,8,2,0,0,0);a.Jump("done");
            a.Label("skip");a.Emit(0xff,0xc7);a.Jump("next");a.Label("missing");a.Emit(0xc7,0x43,8,3,0,0,0);a.Jump("done");a.Label("invalid");a.Emit(0xc7,0x43,8,4,0,0,0);
            a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x20,0x5d,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
        }

}}
namespace TGLL {
 public static partial class NativeBackend {
  // Char.efd (+0x138), mảng trang bị đang mặc 19 ô; không dùng túi/kho để xác nhận.
  internal static long ReadWornCloak(Remote r,long game,int id){long cls=ResolvedClass(r,game+0x16ee330),player=r.Ptr(r.Ptr(cls+0xb8)+8),items=r.Ptr(player+0x138);int n;byte[] slots=InventoryArray(r,items,32,out n);if(n!=19)throw new Exception("equipment_array_not_ready");long found=0;for(int i=0;i<n;i++){long o=BitConverter.ToInt64(slots,i*8);if(o!=0&&r.Ptr(o)==ResolvedClass(r,game+0x16ee578)&&FarmI(r,o+20)==id)found=o;}if(r.Ptr(player+0x138)!=items||!SameBytes(slots,r.Read(items+32,slots.Length)))throw new Exception("equipment_changed");return found;}
 }
 internal static partial class NativeCode {
  internal static byte[] EquipPacket(long game){return EquipPacketCore(game+0x16ee820,game+0x16f760,game+0x2e2aa0,game+0x1dd900);}
  internal static byte[] EquipPacketCore(long serviceSlot,long allocate,long ctor,long initialize){Code64 a=new Code64();a.Emit(0x53,0x56,0x48,0x83,0xec,0x28,0x48,0x89,0xcb);Slot(a,serviceSlot,"done",initialize);a.Emit(0x48,0x89,0xc1);a.Call(allocate);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x89,0xc6,0x48,0x89,0xc1,0xba,0x74,0,0,0,0x45,0x33,0xc0);a.Call(ctor);a.Emit(0x48,0x8b,0x06,0x4c,0x8b,0x80,0x50,2,0,0,0x48,0x89,0xf1,0x8b,0x53,32,0xff,0x90,0x48,2,0,0,0x48,0x8b,0x06,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x89,0xf1,0xff,0x90,0x98,2,0,0);a.Label("done");a.Emit(0x48,0x83,0xc4,0x28,0x5e,0x5b,0xc3);return a.Finish();}
 }
}

namespace TGLL { public static partial class NativeBackend {
 internal static long UnixMs(DateTime utc){return (long)(utc.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
 public static string CloakExpiryText(long ms){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms).AddHours(7).ToString("dd/MM/yyyy HH:mm:ss");}
 internal static InventoryItem LongestCloak(InventorySnapshot bag,int id,DateTime now){if(bag==null||!bag.Ready)throw new Exception("inventory_not_ready");return bag.Items.Where(x=>x.TemplateId==id&&x.ExpiryUtcMs>UnixMs(now)).OrderByDescending(x=>x.ExpiryUtcMs).ThenBy(x=>x.Slot).FirstOrDefault();}
 internal static long ReadWornCloakExpiry(Remote r,long game,int id){long item=ReadWornCloak(r,game,id);if(item==0)return 0;long expiry=BitConverter.ToInt64(r.Read(item+48,8),0);if(ReadWornCloak(r,game,id)!=item||FarmI(r,item+20)!=id||BitConverter.ToInt64(r.Read(item+48,8),0)!=expiry)throw new Exception("equipment_changed");return expiry;}
 public static void UseLongestOwnedCloak(string id,int templateId){StartCloakCore(id,templateId,1,DefaultCloakOffers().First(x=>x.TemplateId==templateId).CrystalPrice,true);}
}}
namespace TGLL {
 public sealed class MarketRule { public int ItemId,Quantity,Price; }
 public sealed class MarketListing { public long Id,Address;public int ItemId,Quantity,RawQuantity,Price,Expires;public string Name,Seller; }
 public sealed class MarketSnapshot { public long Panel,Array;public int Page,Category,Filter;public MarketListing[] Items; }
 public sealed partial class NativeJob {
  public bool MarketEnabled,MarketBuying;internal bool MarketHeadless;internal long MarketQueryPanel;internal int MarketQueryFilter,MarketQueryCategory;internal bool MarketDrained=true;public string MarketStatus="";public MarketListing[] MarketResults=new MarketListing[0];
  public int MarketBought,MarketSold;public long MarketSpent;
  internal MarketRule[] MarketRules;internal int MarketMode,MarketPhase,MarketRuleIndex,MarketPage,MarketMaxBuys;
  internal long MarketBudget,MarketBeforeSilver,MarketBeforeCount,MarketArray,MarketFirstId;
  internal DateTime MarketAt,MarketNext;internal MarketListing MarketTarget;internal InventoryItem MarketItem;
  internal HashSet<long> MarketSeen=new HashSet<long>(),MarketPageStarts=new HashSet<long>();internal int[] MarketSlots;internal long MarketOldOwnMax,MarketOwnArray;
 }
 internal static partial class NativeCode {
  // Uses the same Message writer slots as hu.cjy/cjz/rk/ckg. No mouse/keyboard calls.
  static void MarketWrite(Code64 a,int offset,bool wide,int slot){
   a.Emit(0x48,0x8b,0x07,0x4c,0x8b,0x80);a.I(slot+8);a.Emit(0x48,0x89,0xf9);
   if(wide)a.Emit(0x48,0x8b,0x93);else a.Emit(0x8b,0x93);a.I(offset);a.Emit(0xff,0x90);a.I(slot);
  }
  internal static byte[] Market(long data,long game,long split){
   Code64 a=new Code64();a.Emit(0x53,0x56,0x57,0x41,0x54,0x48,0x83,0xec,0x28);
   a.Call(split);a.Emit(0x48,0xbb);a.Q(data);a.Emit(0x83,0x3b,1);a.J(0x85,"return");
   a.Emit(0xc7,0x03,2,0,0,0,0xc7,0x43,8,1,0,0,0,0x83,0x7b,12,1);a.J(0x85,"done");
   Slot(a,game+0x16ee888,"done",game+0x1dd900);a.Emit(0x48,0x8b,0x80,0xb8,0,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
   a.Emit(0x48,0x8b,0x70,16,0x48,0x85,0xf6);a.J(0x84,"done");
   a.Emit(0x48,0x8b,0x86,0x90,0,0,0,0x48,0x3b,0x43,16);a.J(0x85,"done");
   a.Emit(0x83,0x7b,4,1);a.J(0x84,"refresh");a.Emit(0x83,0x7b,4,5);a.J(0x84,"own");
   a.Emit(0x83,0x7b,4,3);a.J(0x84,"buy");a.Emit(0x83,0x7b,4,2);a.J(0x85,"done");
   // Confirm exact unlocked stack in the same bag slot immediately before listing.
   a.Emit(0x33,0xc9);a.Call(game+0x3672c0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");
   a.Emit(0x48,0x8b,0x80,0x28,1,0,0,0x48,0x85,0xc0);a.J(0x84,"done");
   a.Emit(0x8b,0x53,24,0x85,0xd2);a.J(0x88,"done");a.Emit(0x3b,0x50,24);a.J(0x83,"done");
   a.Emit(0x48,0x8b,0x44,0xd0,32,0x48,0x85,0xc0);a.J(0x84,"done");
   foreach(byte off in new byte[]{20,24,32}) {a.Emit(0x8b,0x90,off,0,0,0,0x3b,0x93);a.I(off==20?28:off==24?32:24);a.J(0x85,"done");}
   a.Emit(0x80,0x78,97,0);a.J(0x85,"done");
   MailMessage(a,game,0x63);MarketWrite(a,24,false,0x248);MarketWrite(a,36,false,0x228);MarketWrite(a,40,false,0x258);a.Jump("send");
   a.Label("buy");a.Emit(0x48,0x8b,0x86,0xc0,2,0,0,0x48,0x3b,0x43,48);a.J(0x85,"done");
   a.Emit(0x8b,0x53,56,0x85,0xd2);a.J(0x88,"done");a.Emit(0x3b,0x50,24);a.J(0x83,"done");
   a.Emit(0x48,0x8b,0x44,0xd0,32,0x48,0x3b,0x43,64);a.J(0x85,"done");
   a.Emit(0x48,0x8b,0x50,16,0x48,0x3b,0x53,72);a.J(0x85,"done");a.Emit(0x8b,0x50,32,0x3b,0x53,40);a.J(0x85,"done");
   a.Emit(0x48,0x8b,0x40,40,0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x8b,0x50,20,0x3b,0x53,28);a.J(0x85,"done");a.Emit(0x8b,0x50,24,0x3b,0x53,32);a.J(0x85,"done");
   a.Emit(0x33,0xc9);a.Call(game+0x3672c0);a.Emit(0x48,0x85,0xc0);a.J(0x84,"done");a.Emit(0x48,0x63,0x53,40,0x48,0x39,0x90,0xb8,0,0,0);a.J(0x8c,"done");
   MailMessage(a,game,0x62);MarketWrite(a,72,true,0x268);a.Jump("send");
   a.Label("refresh");MailMessage(a,game,0x65);MarketWrite(a,80,false,0x228);MarketWrite(a,84,false,0x228);MarketWrite(a,88,false,0x248);a.Jump("send");
   a.Label("own");MailMessage(a,game,0x64);
   a.Label("send");a.Emit(0xc7,0x43,8,2,0,0,0,0x48,0x8b,0x07,0x48,0x8b,0x90,0xa0,2,0,0,0x48,0x89,0xf9,0xff,0x90,0x98,2,0,0);a.Jump("done");
   a.Label("failed");a.Emit(0xc7,0x43,8,3,0,0,0);
   a.Label("done");a.Emit(0xc7,0x03,0,0,0,0);a.Label("return");a.Emit(0x48,0x83,0xc4,0x28,0x41,0x5c,0x5f,0x5e,0x5b,0xc3);return a.Finish();
  }
 }
 public static partial class NativeBackend {
  public static MarketRule[] ParseMarketRules(string text,bool sell){
   var result=new List<MarketRule>();var ids=new HashSet<int>();int line=0;
   foreach(string raw in (text??"").Split('\n')){line++;string s=raw.Trim();if(s.Length==0)continue;var parts=s.Split(',');int id,qty=1,price;
    if(parts.Length!=(sell?3:2)||!Int32.TryParse(parts[0].Trim(),out id)||id<0||!Int32.TryParse(parts[sell?2:1].Trim(),out price)||price<=0||(sell&&(!Int32.TryParse(parts[1].Trim(),out qty)||qty<1||qty>32767)))throw new Exception("Dòng "+line+": "+(sell?"ID,số lượng,giá cả lần bán":"ID,giá tối đa cả lần mua"));
    if(!ids.Add(id))throw new Exception("ID bị trùng ở dòng "+line);result.Add(new MarketRule{ItemId=id,Quantity=qty,Price=price});
   }if(result.Count==0)throw new Exception("Nhập ít nhất một ID vật phẩm.");return result.ToArray();
  }
  internal static MarketSnapshot ReadMarket(Remote r,long game,bool own=false,bool headless=false,int requestedPage=0){
   long fcClass=ResolvedClass(r,game+0x16ee888),fc=r.Ptr(r.Ptr(fcClass+0xb8)+16);if(fc==0||r.Ptr(fc)!=fcClass)throw new Exception("market_world_not_ready");long panel=r.Ptr(fc+0x90);
   long canvas=r.Ptr(r.Ptr(ResolvedClass(r,game+0x16ed658)+0xb8));if(canvas==0||r.Ptr(canvas+0x1b8)!=fc)throw new Exception("market_world_not_ready");
   bool marketPanel=false;if(panel!=0){byte[] cn=r.Read(r.Ptr(r.Ptr(panel)+16),4);marketPanel=cn[0]=='h'&&cn[1]=='u'&&cn[2]==0;}
   if(!headless&&!marketPanel)throw new Exception("Mở Chợ trong game trước khi rao bán.");
   int filter=0,category=0,page=requestedPage;
   if(marketPanel){filter=FarmI(r,r.Ptr(r.Ptr(r.Ptr(panel+0x190)+32)+0x88)+20);category=FarmI(r,fc+0x2a4);page=FarmI(r,panel+0x1a0);}
   if(filter<0||filter>255||category<0||category>255)throw new Exception("Bộ lọc Chợ chưa sẵn sàng.");
   long array=r.Ptr(fc+(own?0x2b8:0x2c0));var rows=ReadArray(r,array);if(rows.Length>1000)throw new Exception("market_invalid_count");
   long templates=r.Ptr(canvas+0x118);int count=FarmI(r,templates+24);var found=new List<MarketListing>();
   foreach(long row in rows){if(row==0)throw new Exception("market_loading");byte[] b=r.Read(row+16,32);long item=BitConverter.ToInt64(b,24);if(item==0)throw new Exception("market_item_missing");byte[] fields=r.Read(item+20,16);int id=BitConverter.ToInt32(fields,0),qty=BitConverter.ToInt32(fields,4),price=BitConverter.ToInt32(b,16);if(id<0||id>=count||qty<0||price<=0)throw new Exception("market_item_invalid");
    found.Add(new MarketListing{Address=row,Id=BitConverter.ToInt64(b,0),Seller=ReadLabel(r,BitConverter.ToInt64(b,8)),Price=price,Expires=BitConverter.ToInt32(b,20),ItemId=id,Quantity=Math.Max(1,qty),RawQuantity=qty,Name=ReadLabel(r,r.Ptr(r.Ptr(templates+32+id*8)+24))});
    if(!SameBytes(b,r.Read(row+16,32))||!SameBytes(fields,r.Read(item+20,16)))throw new Exception("market_changed");
   }
   if(r.Ptr(fc+(own?0x2b8:0x2c0))!=array||r.Ptr(fc+0x90)!=panel)throw new Exception("market_changed");return new MarketSnapshot{Panel=panel,Array=array,Page=page,Filter=filter,Category=category,Items=found.ToArray()};
  }
  public static void StartMarket(string id,string rules,bool sell,bool buy,int maxBuys,long budget){
   NativeJob j=Get(id);if(j==null)throw new Exception("Acc chưa mở.");MarketRule[] parsed=ParseMarketRules(rules,sell);if(!j.GameSettingsReady||j.CharacterName.Length==0)throw new Exception("Đợi nhân vật vào game xong trước khi chạy Auto chợ.");if(buy&&(maxBuys<1||budget<1))throw new Exception("Nhập giới hạn số lần mua và tổng bạc.");
   lock(j){if(j.MarketEnabled||!j.MarketDrained)throw new Exception("Auto chợ đang chạy hoặc đang đợi lệnh cũ kết thúc.");if(j.MailEnabled||j.MailBusy||j.InboxEnabled||j.InboxBusy||j.GiftEnabled||j.ChestEnabled||j.CloakEnabled||j.SonCapEnabled||j.IzanamiEnabled||j.DailyActive||j.QuestEnabled)throw new Exception("Dừng hoạt động khác trước khi chạy Auto chợ.");
    j.MarketRules=parsed;j.MarketHeadless=!sell;j.MarketMode=sell?2:1;j.MarketBuying=buy&&!sell;j.MarketMaxBuys=maxBuys;j.MarketBudget=budget;j.MarketBought=j.MarketSold=0;j.MarketSpent=0;j.MarketRuleIndex=j.MarketPage=j.MarketPhase=0;j.MarketFirstId=0;j.MarketSeen.Clear();j.MarketPageStarts.Clear();j.MarketResults=new MarketListing[0];j.MarketNext=DateTime.MinValue;j.MarketStatus="Đợi đọc dữ liệu Chợ";j.MarketEnabled=true;}
  }
  public static void StopMarket(string id){var j=Get(id);if(j==null)return;lock(j){j.MarketEnabled=false;j.MarketStatus="Đã dừng. Nếu vừa gửi lệnh, kiểm tra túi/Chợ trước khi chạy lại.";}}
  static void MarketHalt(NativeJob j,string text){j.MarketEnabled=false;j.MarketStatus=text;Log(j,"market_stopped",",\"status\":"+Quote(text));}
  static long MarketCount(InventorySnapshot bag,int id){return bag.Items.Where(x=>x.TemplateId==id).Sum(x=>(long)Math.Max(1,x.Quantity));}
  static void MarketQueue(Remote r,long d,MarketSnapshot s,int command,int page=0){r.Write(d+16,BitConverter.GetBytes(s.Panel));r.Write(d+80,BitConverter.GetBytes(s.Filter));r.Write(d+84,BitConverter.GetBytes(s.Category));r.Write(d+88,BitConverter.GetBytes(page));r.Write(d+4,BitConverter.GetBytes(command));r.Write(d+8,new byte[4]);r.Write(d+12,BitConverter.GetBytes(1));r.Write(d,BitConverter.GetBytes(1));}
  internal static bool MarketFits(MarketListing x,MarketRule rule,string owner,long remaining){return x!=null&&x.ItemId==rule.ItemId&&x.Price>0&&x.Price<=rule.Price&&x.Price<=remaining&&!String.Equals(x.Seller,owner,StringComparison.OrdinalIgnoreCase);}
  internal static void PumpMarket(Remote r,long d,long game,NativeJob j,Func<MarketSnapshot> reader=null,Func<InventorySnapshot> bagReader=null,Func<long> silverReader=null,Func<MarketSnapshot> ownReader=null,DateTime? clock=null){if(d==0)return;lock(j){try{
   bool ready=j.MarketEnabled&&!j.CloseRequested&&!j.CancelRequested&&!j.ReconnectRequested&&j.GameSettingsReady&&!AuxiliaryActionBlocked(j)&&!j.MailEnabled&&!j.MailBusy&&!j.ChestEnabled&&!j.CloakEnabled&&!j.QuestEnabled&&!j.SonCapEnabled&&!j.IzanamiEnabled&&!j.InboxEnabled&&!j.GiftEnabled;
   r.Write(d+12,BitConverter.GetBytes(ready?1:0));r.Write(d+4096+12,BitConverter.GetBytes(ready?1:0));j.MarketDrained=FarmI(r,d)==0&&FarmI(r,d+4096)==0;if(!ready){if(j.MarketEnabled)j.MarketStatus="Đợi dừng hoạt động/auto vật phẩm khác";return;}
   DateTime now=clock??DateTime.UtcNow;if(now<j.MarketNext||FarmI(r,d)!=0||FarmI(r,d+4096)!=0)return;j.MarketDrained=false;
   MarketSnapshot s=reader==null?ReadMarket(r,game,false,j.MarketHeadless,j.MarketPage):reader();InventorySnapshot bag=(j.MarketMode==2||j.MarketBuying||j.MarketPhase==3)?(bagReader==null?ReadInventorySnapshot(r,game):bagReader()):null;
   if(j.MarketPhase==3){
    if(MarketCount(bag,j.MarketTarget.ItemId)>=j.MarketBeforeCount+j.MarketTarget.Quantity&&(silverReader==null?ReadMailSilver(r,game):silverReader())==j.MarketBeforeSilver-j.MarketTarget.Price){j.MarketBought++;j.MarketSpent+=j.MarketTarget.Price;j.MarketSeen.Add(j.MarketTarget.Id);j.MarketPhase=0;j.MarketNext=now.AddSeconds(3);j.MarketStatus="Đã mua "+j.MarketBought+" lần • "+j.MarketSpent+" bạc";if(j.MarketBought>=j.MarketMaxBuys||j.MarketSpent>=j.MarketBudget)MarketHalt(j,"Đã đạt giới hạn mua • "+j.MarketBought+" lần, "+j.MarketSpent+" bạc");return;}
    if((now-j.MarketAt).TotalSeconds>20)MarketHalt(j,"Chưa xác nhận mua thành công; giữ nguyên, không tự mua lại.");return;
   }
   if(j.MarketPhase==4){var rule=j.MarketRules[j.MarketRuleIndex];var exact=bag.Items.FirstOrDefault(x=>x.TemplateId==rule.ItemId&&!x.Locked&&x.Quantity==rule.Quantity&&!j.MarketSlots.Contains(x.Slot));
    if(exact!=null&&MarketCount(bag,rule.ItemId)==j.MarketBeforeCount){j.MarketItem=exact;j.MarketPhase=5;}else {if((now-j.MarketAt).TotalSeconds>15)MarketHalt(j,"Chưa xác nhận tách vật phẩm; không rao bán chồng gốc.");return;}
   }
   if(j.MarketPhase==6){var rule=j.MarketRules[j.MarketRuleIndex];if(MarketCount(bag,rule.ItemId)==j.MarketBeforeCount-rule.Quantity){j.MarketOwnArray=(ownReader==null?ReadMarket(r,game,true):ownReader()).Array;MarketQueue(r,d,s,5);j.MarketPhase=7;j.MarketAt=now;return;}if((now-j.MarketAt).TotalSeconds>20)MarketHalt(j,"Chưa xác nhận rao bán; kiểm tra Chợ, không tự gửi lại.");return;}
   if(j.MarketPhase==7){var rule=j.MarketRules[j.MarketRuleIndex];var own=(ownReader==null?ReadMarket(r,game,true):ownReader());if(own.Array!=j.MarketOwnArray&&own.Items.Any(x=>x.Id>j.MarketOldOwnMax&&x.ItemId==rule.ItemId&&x.Quantity==rule.Quantity&&x.Price==rule.Price)){j.MarketSold++;j.MarketRuleIndex++;j.MarketPhase=0;j.MarketNext=now.AddSeconds(3);j.MarketStatus="Đã rao bán "+j.MarketSold+" chồng";return;}if((now-j.MarketAt).TotalSeconds>20)MarketHalt(j,"Chưa thấy món vừa rao trong Chợ của mình; không gửi lại.");return;}
   if(j.MarketMode==2){
    if(j.MarketRuleIndex>=j.MarketRules.Length){MarketHalt(j,"Hoàn tất rao bán "+j.MarketSold+" chồng");return;}
    if(j.MarketPhase==0){j.MarketOwnArray=(ownReader==null?ReadMarket(r,game,true):ownReader()).Array;MarketQueue(r,d,s,5);j.MarketAt=now;j.MarketPhase=8;return;}
    if(j.MarketPhase==8){var own=(ownReader==null?ReadMarket(r,game,true):ownReader());if(own.Array==j.MarketOwnArray){if((now-j.MarketAt).TotalSeconds>15)MarketHalt(j,"Chưa đọc được Chợ của mình; chưa rao bán.");return;}j.MarketOldOwnMax=own.Items.Length==0?0:own.Items.Max(x=>x.Id);j.MarketPhase=2;}var rule=j.MarketRules[j.MarketRuleIndex];
    if(j.MarketPhase!=5){
     var item=bag.Items.Where(x=>x.TemplateId==rule.ItemId&&!x.Locked&&Math.Max(1,x.Quantity)>=rule.Quantity&&x.Slot==x.BagIndex).OrderBy(x=>x.Quantity).FirstOrDefault();if(item==null){j.MarketStatus="Bỏ qua ID "+rule.ItemId+": thiếu vật phẩm không khóa";j.MarketRuleIndex++;j.MarketNext=now.AddSeconds(1);return;}
     if((silverReader==null?ReadMailSilver(r,game):silverReader())<((long)rule.Price+9)/10){MarketHalt(j,"Không đủ bạc trả phí rao bán 10%.");return;}j.MarketBeforeCount=MarketCount(bag,rule.ItemId);j.MarketItem=item;
     if(Math.Max(1,item.Quantity)>rule.Quantity){if(bag.Items.Length>=bag.Capacity){MarketHalt(j,"Túi đầy, không có ô tách vật phẩm.");return;}j.MarketSlots=bag.Items.Select(x=>x.Slot).ToArray();long sd=d+4096;byte[] wire=ExpectedMailWire("","","","",item.Slot,2,rule.Quantity);r.Write(sd+44,BitConverter.GetBytes(wire.Length));r.Write(sd+2560,wire);r.Write(sd+4,BitConverter.GetBytes(2));r.Write(sd+16,BitConverter.GetBytes(item.TemplateId));r.Write(sd+20,BitConverter.GetBytes(item.Slot));r.Write(sd+24,BitConverter.GetBytes(item.Quantity));r.Write(sd+32,BitConverter.GetBytes(rule.Quantity));r.Write(sd,BitConverter.GetBytes(1));j.MarketPhase=4;j.MarketAt=now;j.MarketStatus="Tách ID "+rule.ItemId+" x"+rule.Quantity;return;}
    }
    var chosen=bag.Items.FirstOrDefault(x=>x.Slot==j.MarketItem.Slot&&x.TemplateId==rule.ItemId&&Math.Max(1,x.Quantity)==rule.Quantity&&!x.Locked);if(chosen==null){MarketHalt(j,"Chồng vật phẩm đã thay đổi; chưa rao bán.");return;}
    r.Write(d+24,BitConverter.GetBytes(chosen.Slot));r.Write(d+28,BitConverter.GetBytes(chosen.TemplateId));r.Write(d+32,BitConverter.GetBytes(chosen.Quantity));r.Write(d+36,new byte[4]);r.Write(d+40,BitConverter.GetBytes(rule.Price));MarketQueue(r,d,s,2);j.MarketPhase=6;j.MarketAt=now;j.MarketStatus="Đang rao ID "+rule.ItemId+" x"+rule.Quantity+" • "+rule.Price+" bạc/cả lần";return;
   }
   if(j.MarketPhase==0){j.MarketArray=s.Array;j.MarketQueryPanel=s.Panel;j.MarketQueryFilter=s.Filter;j.MarketQueryCategory=s.Category;MarketQueue(r,d,s,1,j.MarketPage);j.MarketPhase=1;j.MarketAt=now;j.MarketStatus="Quét Chợ trang "+(j.MarketPage+1);return;}
   if(j.MarketPhase==1){if(j.MarketHeadless&&(s.Panel!=j.MarketQueryPanel||s.Filter!=j.MarketQueryFilter||s.Category!=j.MarketQueryCategory)){MarketHalt(j,"Giao diện/bộ lọc thay đổi trong lúc quét; dừng để tránh trộn trang.");return;}if(s.Array==j.MarketArray){if((now-j.MarketAt).TotalSeconds>15)MarketHalt(j,"Chợ chưa trả dữ liệu mới; chưa mua.");return;}
    var matches=s.Items.Where(x=>j.MarketRules.Any(rule=>x.ItemId==rule.ItemId&&x.Price<=rule.Price)).ToArray();j.MarketResults=j.MarketResults.Concat(matches).GroupBy(x=>x.Id).Select(x=>x.Last()).Take(1000).ToArray();
    if(j.MarketBuying){long remaining=j.MarketBudget-j.MarketSpent;var target=s.Items.FirstOrDefault(x=>!j.MarketSeen.Contains(x.Id)&&j.MarketRules.Any(rule=>MarketFits(x,rule,j.CharacterName,remaining)));
     if(target!=null){if(bag.Items.Length>=bag.Capacity){MarketHalt(j,"Túi đầy; dừng mua.");return;}long silver=(silverReader==null?ReadMailSilver(r,game):silverReader());if(silver<target.Price){MarketHalt(j,"Không đủ bạc mua.");return;}j.MarketTarget=target;j.MarketBeforeCount=MarketCount(bag,target.ItemId);j.MarketBeforeSilver=silver;r.Write(d+28,BitConverter.GetBytes(target.ItemId));r.Write(d+32,BitConverter.GetBytes(target.RawQuantity));r.Write(d+40,BitConverter.GetBytes(target.Price));r.Write(d+48,BitConverter.GetBytes(s.Array));r.Write(d+56,BitConverter.GetBytes(Array.IndexOf(s.Items,target)));r.Write(d+64,BitConverter.GetBytes(target.Address));r.Write(d+72,BitConverter.GetBytes(target.Id));MarketQueue(r,d,s,3);j.MarketPhase=3;j.MarketAt=now;j.MarketStatus="Đang mua ID "+target.ItemId+" • "+target.Price+" bạc";return;}
    }
    long first=s.Items.Length==0?0:s.Items[0].Id;bool end=s.Items.Length<30||!j.MarketPageStarts.Add(first)||j.MarketPage>=32766;
    if(j.MarketPage==0)j.MarketFirstId=first;
    if(end){j.MarketPage=0;j.MarketPageStarts.Clear();if(!j.MarketBuying){MarketHalt(j,"Quét xong: "+j.MarketResults.Length+" kết quả phù hợp");return;}j.MarketNext=now.AddSeconds(1);}else{j.MarketPage++;j.MarketNext=now.AddMilliseconds(500);}j.MarketPhase=0;
   }
  }catch(Exception e){if((e.Message=="market_loading"||e.Message=="market_changed"||e.Message=="market_item_missing"||e.Message=="market_item_invalid")&&j.MarketPhase==1&&(DateTime.UtcNow-j.MarketAt).TotalSeconds<15){j.MarketNext=DateTime.UtcNow.AddMilliseconds(150);j.MarketStatus="Đợi trang Chợ tải xong";return;}MarketHalt(j,e.Message);}}}
 }
}

namespace TGLL {
 public static partial class NativeBackend {
  public static bool UpSessionReady(NativeJob j){return j!=null&&j.TrainReady&&j.GameSettingsReady&&!j.CloseRequested&&!j.CancelRequested&&Array.IndexOf(new[]{"queued","starting","typing","submitted","selecting_character","character_submitted","waiting_map","waiting_popup","confirming_popup","closed","failed","cancelled"},j.State)<0;}
  public static void PrepareUpFarmDestination(string id,bool characterMode,int level,FarmSpot[] spots,bool quiet){
   NativeJob j=Get(id);if(j==null||!j.TrainReady)throw new Exception("Chờ nhân vật vào game.");
   FarmSpot[] copied=(spots??new FarmSpot[0]).Select(s=>new FarmSpot{MapId=s.MapId,MapName=s.MapName,MobId=s.MobId,MobName=s.MobName,Level=s.Level,X=s.X,Y=s.Y}).ToArray();
   if(level<1||level>500||copied.Length==0||copied.Any(s=>!OrdinaryFarmMap(s.MapId,0,s.MapName)||s.X<0||s.Y<0||s.X>32767||s.Y>32767||s.Level<1||s.Level>500)||SelectFarmSpot(copied,characterMode?j.CharacterLevel:level,characterMode)==null)throw new Exception("Chưa có bãi Up phù hợp.");
   j.FarmChoices=copied;j.FarmCharacterMode=characterMode;j.FarmLevel=level;j.FarmQuietZone=quiet;
  }
  public static void ConfigureUpActivities(string id,bool repeatDaily,bool key,bool dungeon,bool izanami,bool quest,bool soncap){
   NativeJob j=Get(id);if(j==null||j.CloseRequested||j.CancelRequested)return;
   lock(DailyGate){
    if(j.MailQueueOnly||j.CloakQueueOnly||j.DungeonQueueOnly||j.IzanamiQueueOnly||j.QuestQueueOnly)throw new Exception("Dừng hàng chờ riêng trước khi bật Up và hoạt động.");
    bool[] plan={key,dungeon,izanami,quest,soncap};
    if(!j.PersistentUp){
     if(j.ReceiveFlowActive||j.TravelActive)throw new Exception("Đợi thao tác NPC hiện tại hoàn tất; Up vẫn được giữ để thử lại.");
     int adopt=soncap&&j.SonCapEnabled&&Array.IndexOf(SonCapMaps,j.MapId)>=0?5:izanami&&j.IzanamiEnabled&&!j.SonCapEnabled&&j.IzanamiRoundEntered&&j.MapId==89?3:dungeon&&j.DungeonHighEnabled&&j.DungeonCycleWaiting&&j.DungeonLeftOrigin?2:quest&&j.QuestEnabled&&!j.QuestFinished?4:0;
     j.PersistentUp=true;j.DailyManaged=true;j.DailyPlan=new bool[5];j.DailyRunPlan=new bool[5];j.UpDay=DateTime.UtcNow.AddHours(7).Date;
     if(adopt>0){j.DailyActive=true;j.DailyStage=adopt;j.DailyExecuting=true;j.DailyRunPlan=(bool[])plan.Clone();j.DailyStatus="Tiếp tục hoạt động đang chạy; xong sẽ quay lại Up";
      if(adopt==3){j.DailyTeamIds=(string[])j.TeamJobIds.Clone();j.UpIzanamiFull=j.DailyTeamIds.Length==IzanamiTeamSize;j.IzanamiRoundsTarget=Int32.MaxValue;}
      if(adopt==5){j.UpSonCapTeamIds=(string[])j.TeamJobIds.Clone();j.UpSonCapFull=j.UpSonCapTeamIds.Length==SonCapTeamSize;}
     }else{j.DailyActive=false;j.DailyExecuting=false;ClearDailyActions(j);}
    }
    for(int n=0;n<5;n++)if(plan[n]&&!j.DailyPlan[n])j.UpCompleted[n]=false;
    j.DailyPlan=plan;j.UpRepeatDaily=repeatDaily;j.UpRequested=true;
   }
  }
  // Groups arrive in account-table order and include empty IDs for selected offline accounts.
  public static void ConfigureUpActivityTeams(string[][] izanamiGroups,string[][] soncapGroups){
   lock(DailyGate){
    foreach(NativeJob j in jobs.Values)if(j.PersistentUp&&!j.DailyExecuting){j.UpIzanamiFull=false;j.UpSonCapFull=false;}
    BindUpGroups(izanamiGroups,false);BindUpGroups(soncapGroups,true);
   }
  }
  static void BindUpGroups(string[][] groups,bool soncap){
   int size=soncap?SonCapTeamSize:IzanamiTeamSize;
   foreach(string[] group in groups??new string[0][]){
    foreach(string id in group){NativeJob j=Get(id??"");if(j==null||!j.PersistentUp||j.DailyExecuting)continue;
     if(soncap){j.UpSonCapTeamIds=(string[])group.Clone();j.UpSonCapFull=group.Length==size;}
     else{j.DailyTeamIds=(string[])group.Clone();j.UpIzanamiFull=group.Length==size;}
    }
   }
  }
  static bool UpTeamReady(NativeJob j,bool soncap){
   string[] ids=soncap?j.UpSonCapTeamIds:j.DailyTeamIds;
   return (soncap?j.UpSonCapFull:j.UpIzanamiFull)&&ids.Length==(soncap?SonCapTeamSize:IzanamiTeamSize)&&ids.All(id=>{NativeJob p=Get(id??"");return DailyAlive(p)&&p.PersistentUp&&p.UpRequested&&p.DailyPlan[soncap?4:2];});
  }
  static void StartUpTeamActivity(NativeJob j){
   bool soncap=j.DailyStage==5;string[] ids=soncap?j.UpSonCapTeamIds:j.DailyTeamIds;
   if(!UpTeamReady(j,soncap)){j.DailyStatus=(soncap?"Sơn Cáp":"Cấm thuật")+" — đợi đủ nhóm đã chọn";return;}
   NativeJob[] team=ids.Select(id=>Get(id)).ToArray();
   if(team.Any(p=>!p.DailyActive||p.DailyStage!=j.DailyStage)){j.DailyStatus="Đợi các thành viên xong hoạt động trước";return;}
   if(team.Any(p=>p.DailyExecuting)){
    j.DailyApplying=true;try{if(soncap){j.SonCapRebound=true;SetSonCap(j.Id,true);}else{SetIzanami(j.Id,true);j.IzanamiRoundsTarget=Int32.MaxValue;}j.TeamJobIds=ids;j.TeamManaged=true;j.DailyExecuting=true;}finally{j.DailyApplying=false;}return;
   }
   if(soncap&&jobs.Values.Count(p=>DailyAlive(p)&&p.SonCapEnabled)+team.Length>Math.Max(6,DailySonCapLimit)){j.DailyStatus="Sơn Cáp — đợi số tài khoản cho phép";return;}
   if(!soncap&&DailyOccupancy(3)+team.Length>Math.Max(1,DailyIzanamiLimit)){j.DailyStatus="Cấm thuật — đợi số tài khoản cho phép";return;}
   foreach(NativeJob p in team)p.DailyApplying=true;
   try{
    if(soncap){foreach(NativeJob p in team){p.IzanamiNoAttempts=false;p.SonCapRebound=true;SetSonCap(p.Id,true);}ConfigureSonCapTeams(ids);}
    else{ConfigureIzanamiTeam(ids);foreach(NativeJob p in team){if(!p.IzanamiRoundEntered){p.IzanamiNoAttempts=false;p.IzanamiRoundComplete=false;}SetIzanami(p.Id,true);p.IzanamiRoundsTarget=Int32.MaxValue;}}
    foreach(NativeJob p in team){p.TeamJobIds=ids;p.TeamManaged=true;p.DailyExecuting=true;p.DailyStatus=soncap?"Đang đi Sơn Cáp":"Đang đi cấm thuật";}
   }finally{foreach(NativeJob p in team)p.DailyApplying=false;}
  }
  static void ReturnUpToFarm(NativeJob j){
   FarmSpot s=j.DailyReturnSpot;
   ClearDailyActions(j);j.DailyActive=false;j.DailyExecuting=false;j.State="opened";j.Error="";
   try{
    if(s!=null)StartMapFarm(j.Id,false,s.Level,new[]{s},j.DailyReturnQuiet);
    else StartMapFarm(j.Id,j.FarmCharacterMode,j.FarmLevel,j.FarmChoices,j.FarmQuietZone);
    j.DailyStatus="Đã xong hoạt động — quay lại bãi Up";Log(j,"up_return_farm","");
   }catch(Exception e){j.DailyActive=true;j.DailyStage=6;j.DailyStatus="Up đang chờ: "+e.Message;}
  }
  static void PulseUpPriority(NativeJob j,DateTime now){
   if(!j.UpRequested)return;
   if(CurseDisplayStatus(j).Length>0){j.DailyStatus="Bị bùa — chờ nhập mã";return;}
   DateTime day=now.AddHours(7).Date;
   if(!j.DailyActive){
    if(day>j.UpDay&&j.UpRepeatDaily){j.UpCompleted=new bool[5];j.UpDay=day;}
    bool[] pending=(bool[])j.DailyPlan.Clone();
    for(int n=0;n<5;n++)pending[n]&=!j.UpCompleted[n];
    pending[2]&=UpTeamReady(j,false);pending[4]&=UpTeamReady(j,true);
    if(!pending.Any(b=>b)){
     if(!j.FarmEnabled&&!j.FarmStopPending&&j.FarmChoices.Length>0)ReturnUpToFarm(j);
     return;
    }
    if(j.ReceiveFlowActive||j.TravelActive||j.FarmStopPending)return;
    FarmSpot spot=j.FarmTarget??SelectFarmSpot(j.FarmChoices,j.FarmCharacterMode?j.CharacterLevel:j.FarmLevel,j.FarmCharacterMode);
    if(spot==null){j.DailyStatus="Up đang chờ cấu hình bãi để quay lại";return;}
    j.DailyReturnSpot=new FarmSpot{MapId=spot.MapId,MapName=spot.MapName,MobId=spot.MobId,MobName=spot.MobName,Level=spot.Level,X=spot.X,Y=spot.Y};j.DailyReturnQuiet=j.FarmQuietZone;
    Log(j,"up_activity_begin",",\"plan\":"+Quote(String.Join(",",pending.Select(b=>b?"1":"0"))));j.DailyRunPlan=pending;j.DailyStage=0;j.DailyExecuting=false;j.DailyActive=true;StopFarm(j);j.DailyStatus="Tạm dừng Up — chuẩn bị hoạt động đã chọn";return;
   }
   if(j.FarmStopPending||j.ReceiveFlowActive||j.TravelActive||j.SonCapStopPending)return;
   if(j.DailyStage==0){AdvanceDailyStage(j);return;}
   if(j.DailyStage>=6){ReturnUpToFarm(j);return;}
   if(!j.DailyPlan[j.DailyStage-1]){AdvanceDailyStage(j);return;}
   if(!j.DailyExecuting){StartDailyActivity(j);return;}
   if((j.DailyStage==3||j.DailyStage==5)&&!UpTeamReady(j,j.DailyStage==5)){
    bool outside=j.MapId>=0&&(j.DailyStage==3?j.MapId!=89&&!j.IzanamiRoundEntered:Array.IndexOf(SonCapMaps,j.MapId)<0);
    if(outside&&!DailyStageComplete(j)){AdvanceDailyStage(j);return;}
   }
   if(DailyStageComplete(j)){Log(j,"up_activity_complete",",\"stage\":"+j.DailyStage);j.UpCompleted[j.DailyStage-1]=true;AdvanceDailyStage(j);return;}
   if(j.DailyStage==5&&j.SonCapFinished&&!j.SonCapNoAttempts&&Array.IndexOf(SonCapMaps,j.MapId)<0){
    NativeJob[] peers=j.UpSonCapTeamIds.Select(id=>Get(id??"")).ToArray();
    if(peers.All(p=>DailyAlive(p)&&p.SonCapFinished&&Array.IndexOf(SonCapMaps,p.MapId)<0)){
     foreach(NativeJob p in peers)p.DailyApplying=true;
     try{foreach(NativeJob p in peers){SetSonCap(p.Id,false);p.SonCapRebound=true;SetSonCap(p.Id,true);}ConfigureSonCapTeams(j.UpSonCapTeamIds);}
     finally{foreach(NativeJob p in peers)p.DailyApplying=false;}
    }
   }
   if(j.Error.Length>0)j.DailyStatus="Hoạt động đang chờ kiểm tra: "+j.Error;
  }
 }
}


namespace TGLL {
 public sealed class BiMaSnapshot {
  public readonly long Count;public readonly DateTime ReadAt;public readonly string Session;
  internal BiMaSnapshot(long count,DateTime at,string session){Count=count;ReadAt=at;Session=session;}
 }
 public sealed partial class NativeJob {
  internal readonly object BiMaGate=new object();internal string BiMaAccountId="";internal long BiMaCount=-1;internal DateTime BiMaReadAt=DateTime.MinValue,BiMaPollAt=DateTime.MinValue;internal bool BiMaCloseLogged,BiMaSavePending;
 }
 public static partial class NativeBackend {
  public static string BiMaDataDirectory="";
  static readonly object biMaStoreGate=new object();
  static readonly Dictionary<string,BiMaSnapshot> biMaStore=new Dictionary<string,BiMaSnapshot>();
  static readonly Dictionary<string,string> biMaSessions=new Dictionary<string,string>();
  static bool BiMaKey(string key){return !String.IsNullOrEmpty(key)&&key.Length<=128&&System.Text.RegularExpressions.Regex.IsMatch(key,@"^[A-Za-z0-9_-]+$");}
  static BiMaSnapshot LoadBiMa(string key){
   BiMaSnapshot saved;if(biMaStore.TryGetValue(key,out saved))return saved;
   saved=new BiMaSnapshot(-1,DateTime.MinValue,"");
   try{if(BiMaDataDirectory.Length>0){string[] lines=File.ReadAllLines(Path.Combine(BiMaDataDirectory,key+".txt"));long count;DateTime at;
    if(lines.Length==3&&Int64.TryParse(lines[0],out count)&&count>=0&&DateTime.TryParse(lines[1],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out at))saved=new BiMaSnapshot(count,at,lines[2]);}}
   catch{}biMaStore[key]=saved;return saved;
  }
  public static BiMaSnapshot GetBiMaSnapshot(string key){if(!BiMaKey(key))return new BiMaSnapshot(-1,DateTime.MinValue,"");lock(biMaStoreGate)return LoadBiMa(key);}
  public static void BindBiMaAccount(string id,string key){
   if(!BiMaKey(key))return;NativeJob j=Get(id);if(j==null)return;
   lock(j.BiMaGate){if(j.BiMaAccountId==key)return;j.BiMaAccountId=key;lock(biMaStoreGate){biMaSessions[key]=j.Id;LoadBiMa(key);}if(j.BiMaCount>=0)PersistBiMa(j,false);}
  }
  static void PersistBiMa(NativeJob j,bool force){
   if(!BiMaKey(j.BiMaAccountId)||j.BiMaCount<0)return;
   lock(biMaStoreGate){string current;if(!biMaSessions.TryGetValue(j.BiMaAccountId,out current)||current!=j.Id)return;
    BiMaSnapshot old=LoadBiMa(j.BiMaAccountId);bool changed=force||j.BiMaSavePending||old.Count!=j.BiMaCount||old.Session!=j.Id;
    var next=new BiMaSnapshot(j.BiMaCount,j.BiMaReadAt,j.Id);biMaStore[j.BiMaAccountId]=next;if(!changed||BiMaDataDirectory.Length==0)return;
    string temporary="";try{Directory.CreateDirectory(BiMaDataDirectory);string path=Path.Combine(BiMaDataDirectory,j.BiMaAccountId+".txt");temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
     File.WriteAllLines(temporary,new[]{next.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),next.ReadAt.ToString("o"),next.Session},new UTF8Encoding(false));
     if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);j.BiMaSavePending=false;
    }catch{j.BiMaSavePending=true;j.LogError="bi_ma_save_failed";}finally{if(temporary.Length>0)try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
   }
  }
  internal static long ReadBiMaCount(Remote r,long game){
   long cc=ResolvedClass(r,game+0x16ed658),pc=ResolvedClass(r,game+0x16ee330),ic=ResolvedClass(r,game+0x16ee578),fcClass=ResolvedClass(r,game+0x16ee888);
   long cs=r.Ptr(cc+0xb8),ps=r.Ptr(pc+0xb8);if(cs==0||ps==0)throw new Exception("bi_ma_not_ready");
   long canvas=r.Ptr(cs),player=r.Ptr(ps+8);if(canvas==0||player==0||r.Ptr(player)!=pc)throw new Exception("bi_ma_not_ready");
   long world=r.Ptr(canvas+0x1b8);if(world==0||r.Ptr(world)!=fcClass)throw new Exception("bi_ma_not_ready");
   long bag=r.Ptr(player+0x128);int capacity;byte[] slots=InventoryArray(r,bag,512,out capacity);long count=0;var addresses=new List<long>();var records=new List<byte[]>();
   for(int n=0;n<capacity;n++){long item=BitConverter.ToInt64(slots,n*8);if(item==0)continue;if(r.Ptr(item)!=ic)throw new Exception("bi_ma_bag_changed");
    byte[] b=r.Read(item+20,8);int id=BitConverter.ToInt32(b,0),amount=BitConverter.ToInt32(b,4);if(id<0||amount<0)throw new Exception("bi_ma_bag_changed");if(id==1276)count+=amount;addresses.Add(item+20);records.Add(b);
   }
   for(int n=0;n<addresses.Count;n++)if(!SameBytes(records[n],r.Read(addresses[n],8)))throw new Exception("bi_ma_bag_changed");
   if(r.Ptr(cs)!=canvas||r.Ptr(ps+8)!=player||r.Ptr(player+0x128)!=bag||r.Ptr(canvas+0x1b8)!=world||BitConverter.ToInt32(r.Read(bag+24,4),0)!=capacity||!SameBytes(slots,r.Read(bag+32,slots.Length)))throw new Exception("bi_ma_bag_changed");return count;
  }
  internal static void AcceptBiMa(NativeJob j,long count,DateTime at){if(count<0)throw new ArgumentOutOfRangeException("count");lock(j.BiMaGate){j.BiMaCount=count;j.BiMaReadAt=at;PersistBiMa(j,false);}}
  internal static void PumpBiMa(Remote r,long game,NativeJob j){
   DateTime now=DateTime.UtcNow;if(!UpSessionReady(j)||(now-j.BiMaPollAt).TotalSeconds<10)return;j.BiMaPollAt=now;
   try{lock(j.BiMaGate){AcceptBiMa(j,ReadBiMaCount(r,game),now);}}catch{ /* Keep the last verified count, including zero. */ }
  }
  internal static void CaptureBiMaBeforeClose(int pid){
   NativeJob j=jobs.Values.FirstOrDefault(x=>x.ProcessId==pid);if(j==null)return;
   try{lock(j.BiMaGate){using(Process p=Process.GetProcessById(pid))using(Remote r=new Remote(p)){long game=0;foreach(ProcessModule m in p.Modules)if(m.ModuleName=="GameAssembly.dll"){game=m.BaseAddress.ToInt64();break;}if(game!=0)AcceptBiMa(j,ReadBiMaCount(r,game),DateTime.UtcNow);}}}catch{}
  }
  internal static void RecordBiMaClosed(int pid,string reason){foreach(NativeJob j in jobs.Values)if(j.ProcessId==pid)RecordBiMaClosed(j,reason);}
  internal static void RecordBiMaClosed(NativeJob j,string reason){lock(j.BiMaGate){if(j.BiMaCloseLogged)return;j.BiMaCloseLogged=true;PersistBiMa(j,true);
   Log(j,"bi_ma_at_close",",\"pid\":"+j.ProcessId+",\"account_id\":"+Quote(j.BiMaAccountId)+",\"character\":"+Quote(j.CharacterName)+",\"item_id\":1276,\"quantity\":"+(j.BiMaCount<0?"null":j.BiMaCount.ToString(System.Globalization.CultureInfo.InvariantCulture))+",\"read_at\":"+Quote(j.BiMaReadAt==DateTime.MinValue?"":j.BiMaReadAt.ToString("o"))+",\"reason\":"+Quote(reason));
  }}
 }
}

namespace TGLL {
 public sealed partial class NativeJob {
  public volatile bool BeastQuestEnabled,BeastNearRasa,BeastSoloReady,BeastStartupSandReady,BeastStartupSandStopped;internal int BeastStartupSandPid;
  internal long BeastRasaMenu;internal int BeastRasaTag;
  internal int BeastCombatMap=-1,BeastCombatZone=-1;internal bool BeastZoneWaiting,BeastZoneReady,BeastZoneMustConfirm,BeastZoneChangeSent;
  internal int[] BeastZoneIds=new int[0],BeastZoneCounts=new int[0];internal DateTime BeastZoneListAt,BeastZoneRetryAt;

 }
 public static partial class NativeBackend {
  static void ClearBeastTeam(NativeJob j){j.TeamManaged=false;j.TeamJobIds=new string[0];j.TeamConfirmed=false;if(!j.BeastZoneWaiting){j.ZoneSyncActive=false;j.ZoneStatus="";}j.ZonePrepared=false;if(j.FollowActive)j.FollowStopPending=true;j.FollowActive=false;j.FollowDistance=-1;j.TeamStatus="Linh thú: chạy riêng từng tài khoản";}
  public static void ConfigureBeastSolo(string id){NativeJob j=Get(id);if(j==null)throw new Exception("Chờ phiên game linh thú.");if(!j.BeastQuestEnabled)SetAutoBeast(id,true);ClearBeastTeam(j);}
  public static void SetAutoBeast(string id,bool enabled){
   NativeJob j=Get(id);if(j==null||!j.Auto||j.CloseRequested||j.CancelRequested||j.State=="failed"||j.State=="closed"||j.State=="cancelled")throw new Exception("Phiên game chưa sẵn sàng cho linh thú.");
   if(j.BeastQuestEnabled==enabled)return;
   if(enabled){
    j.BeastStartupSandStopped=false;
    if(j.ReceiveFlowActive||j.MailBusy||j.InboxBusy||j.MailEnabled||j.MailQueueOnly||j.CloakQueueOnly||j.SonCapEnabled)throw new Exception("Dừng hoạt động cũ trước khi chạy linh thú.");
    if(j.DailyManaged)CancelDailyPriority(j);StopFarm(j);
    SetAutoQuest(id,false);SetReceiveKey(id,false);SetDungeonHigh(id,false);SetIzanami(id,false);j.CloseAfterKey=false;j.DungeonQueueOnly=false;j.IzanamiQueueOnly=false;j.QuestQueueOnly=true;
    SetAutoQuest(id,true);ResetBeastCombatZone(j);ClearBeastTeam(j);j.BeastSoloReady=false;j.QuestRemaining=-1;j.QuestStatus="Linh thú: tự nhận, đánh đúng quái và trả nhiệm vụ tại Rasa";
   }else{ResetBeastCombatZone(j);j.QuestQueueOnly=false;j.QuestEnabled=false;j.QuestFinished=false;j.QuestPauseAuto=false;j.TrainEnabled=false;j.GameAutoWanted=false;j.QuestStatus="Đã dừng linh thú";}
   j.BeastQuestEnabled=enabled;j.BeastNearRasa=false;j.BeastRasaMenu=0;Log(j,"beast_configured",",\"enabled\":"+(enabled?"true":"false")+",\"mode\":\"solo\"");
  }
  internal static void CompleteBeastStartupSand(Remote r,long data,NativeJob j,Stopwatch timer){
   j.BeastStartupSandReady=true;j.BeastStartupSandStopped=false;j.BeastStartupSandPid=j.ProcessId;j.ReceiveFlowActive=false;j.IzanamiRequested=false;j.Error="";j.State="opened";j.Stage="beast_startup_sand_ready";j.QuestPhase=0;j.QuestAt=DateTime.UtcNow;j.QuestRetryAt=DateTime.MinValue;j.BeastNearRasa=false;j.QuestStatus="Linh thú: đã về Làng Cát, tiếp tục nhiệm vụ riêng";
   r.Write(data,BitConverter.GetBytes(12));timer.Stop();Log(j,"beast_startup_sand_ready",",\"pid\":"+j.ProcessId);
  }
  internal static void PumpBeastStartupSand(Remote r,long data,NativeJob j,Stopwatch timer,long game,Func<bool> inSand=null){
   if(!j.BeastQuestEnabled||j.CloseRequested||j.CancelRequested||!j.GameSettingsReady||!j.TrainReady||j.TravelData==0)return;
   if(j.BeastStartupSandPid!=j.ProcessId){j.BeastStartupSandReady=false;j.BeastStartupSandStopped=false;j.BeastStartupSandPid=j.ProcessId;}
   if(j.BeastStartupSandReady||j.BeastStartupSandStopped)return;
   if(j.TravelActive){PumpTravel(r,data,j,timer,game,inSand);return;}
   bool arrived=false;try{arrived=inSand!=null?inSand():ReadDungeonMap(r,game)==59&&SandNpcReady(r,game);}catch{}
   if(arrived){CompleteBeastStartupSand(r,data,j,timer);return;}
   j.NpcAction=3;j.TravelReady=false;j.GameAutoManaged=true;j.GameAutoWanted=false;j.QuestPauseAuto=true;j.TrainEnabled=false;j.QuestStatus="Linh thú: dùng phù 168 về Làng Cát lần đầu";
   BeginTravel(r,data,j,timer);Log(j,"beast_startup_sand_requested",",\"pid\":"+j.ProcessId+",\"template_id\":168");
  }
  static readonly object beastZoneGate=new object();
  internal static void ResetBeastCombatZone(NativeJob j){lock(beastZoneGate){j.BeastCombatMap=-1;j.BeastCombatZone=-1;}j.BeastZoneWaiting=false;j.BeastZoneReady=false;j.BeastZoneMustConfirm=false;j.BeastZoneChangeSent=false;j.BeastZoneIds=new int[0];j.BeastZoneCounts=new int[0];j.ZonePlanMap=-1;j.PlannedZone=-1;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;j.ZonePollAt=DateTime.MinValue;j.BeastZoneRetryAt=DateTime.MinValue;j.ZoneSyncActive=false;j.ZoneStatus="";}
  internal static int ReserveBeastCombatZone(NativeJob j,int map,int current,int[] ids,int[] counts){
   if(map<0||current<0||current>255)return -1;
   lock(beastZoneGate){
    DateTime now=DateTime.UtcNow;HashSet<int> used=new HashSet<int>();
    foreach(NativeJob p in jobs.Values)if(p!=j&&p.BeastQuestEnabled&&p.QuestEnabled&&!p.QuestFinished&&!p.CloseRequested&&!p.CancelRequested&&!p.ReconnectRequested&&p.State!="closed"&&p.State!="failed"&&p.State!="cancelled"&&p.GameSettingsReady&&p.MapId==map&&p.BeastCombatMap==map&&p.BeastCombatZone>=0)used.Add(p.BeastCombatZone);
    int target=j.BeastCombatMap==map&&!used.Contains(j.BeastCombatZone)?j.BeastCombatZone:-1;
    if(target<0&&!used.Contains(current))target=current;
    if(target<0&&ids!=null&&counts!=null&&ids.Length==counts.Length){var eligible=Enumerable.Range(0,ids.Length).Where(n=>!used.Contains(ids[n])).ToArray();target=ChooseQuietZone(eligible.Select(n=>ids[n]).ToArray(),eligible.Select(n=>counts[n]).ToArray(),current);}
    if(target>=0){j.BeastCombatMap=map;j.BeastCombatZone=target;}return target;
   }
  }
  internal static bool PrepareBeastCombatZone(NativeJob j,int map){
   if(j.BeastZoneReady&&j.BeastCombatMap==map&&j.MapId==map&&j.ZoneId==j.BeastCombatZone&&(DateTime.UtcNow-j.ZoneReadAt).TotalSeconds<4){j.ZoneSyncActive=false;return true;}
   if(j.BeastCombatMap!=map){ResetBeastCombatZone(j);j.BeastCombatMap=map;}
   j.BeastZoneReady=false;if(!j.BeastZoneWaiting)j.ZoneStatus="Linh thú: kiểm tra khu ở bãi, tránh khu acc khác đang đánh";j.BeastZoneWaiting=true;j.ZoneSyncActive=true;return false;
  }
  internal static void PumpBeastCombatZones(Remote r,long game,long data,NativeJob j){
   DateTime now=DateTime.UtcNow;
   if(!j.QuestEnabled||j.QuestFinished||j.CloseRequested||j.CancelRequested||j.ReconnectRequested){ResetBeastCombatZone(j);return;}
   if(j.BeastCombatMap>=0&&j.MapId>=0&&j.MapId!=j.BeastCombatMap&&(now-j.ZoneReadAt).TotalSeconds<4){ResetBeastCombatZone(j);return;}
   if(!j.BeastZoneWaiting){if(j.BeastZoneReady&&j.ZoneId!=j.BeastCombatZone){j.BeastZoneReady=false;j.BeastZoneWaiting=true;}else{j.ZoneSyncActive=false;return;}}
   j.ZoneSyncActive=true;if((now-j.ZonePollAt).TotalMilliseconds<750)return;j.ZonePollAt=now;
   try{
    if(j.MapId<0||j.MapId!=j.BeastCombatMap||j.ZoneId<0||(now-j.ZoneReadAt).TotalSeconds>=4){j.ZoneStatus="Linh thú: chờ dữ liệu map/khu tại bãi";return;}
    int state=BitConverter.ToInt32(r.Read(data,4),0);if(state>=1&&state<=3)return;
    if(now<j.BeastZoneRetryAt)return;
    if((now-j.BeastZoneListAt).TotalSeconds>30){j.BeastZoneIds=new int[0];j.BeastZoneCounts=new int[0];}
    int target=ReserveBeastCombatZone(j,j.MapId,j.ZoneId,j.BeastZoneIds,j.BeastZoneCounts);
    if(target<0){
     j.ZoneStatus="Linh thú: đọc danh sách khu để né các acc cùng bãi";
     if(j.ZoneListWaiting){long popup=ZonePopup(r,game);if(popup!=0&&popup==r.Ptr(data+32)&&r.Ptr(popup+0xe8)!=r.Ptr(data+24)){
      long array=r.Ptr(popup+0xe8);int length=array==0?0:BitConverter.ToInt32(r.Read(array+24,4),0);if(length<1||length>256)throw new Exception("zone_list_invalid");int[] ids=new int[length],counts=new int[length];
      for(int n=0;n<length;n++){long item=r.Ptr(array+32+n*8);if(item==0)throw new Exception("zone_list_changed");ids[n]=n;counts[n]=BitConverter.ToInt32(r.Read(item+16,4),0);}
      if(r.Ptr(popup+0xe8)!=array||ZonePopup(r,game)!=popup)throw new Exception("zone_list_changed");j.BeastZoneIds=ids;j.BeastZoneCounts=counts;j.BeastZoneListAt=now;j.ZoneListWaiting=false;j.ZoneAttempts=0;j.ZoneActionAt=DateTime.MinValue;target=ReserveBeastCombatZone(j,j.MapId,j.ZoneId,ids,counts);
     }else if((now-j.ZoneActionAt).TotalSeconds<10)return;else j.ZoneListWaiting=false;}
     if(target<0){
      if(j.BeastZoneIds.Length>0){j.ZoneStatus="Linh thú: các khu đang được acc khác giữ; đợi khu trống";j.BeastZoneRetryAt=now.AddSeconds(5);return;}
      if(j.ZoneAttempts>=3){j.ZoneStatus="Linh thú: chưa đọc được khu; đợi 30 giây thử lại";if((now-j.ZoneActionAt).TotalSeconds<30)return;j.ZoneAttempts=0;}
      QueueZone(r,data,j,1,-1);j.ZoneListWaiting=true;j.BeastZoneMustConfirm=true;return;
     }
    }
    if(j.PlannedZone!=target){j.PlannedZone=target;j.ZoneAttempts=0;j.ZoneActionAt=DateTime.MinValue;j.BeastZoneChangeSent=false;Log(j,"beast_zone_reserved",",\"map\":"+j.MapId+",\"zone\":"+target);}
    if(j.ZoneId==target&&(!j.BeastZoneMustConfirm||j.BeastZoneChangeSent&&state==4)){j.BeastZoneReady=true;j.BeastZoneWaiting=false;j.ZoneSyncActive=false;j.ZoneStatus="Linh thú: khu riêng "+target;Log(j,"beast_zone_confirmed",",\"map\":"+j.MapId+",\"zone\":"+target);return;}
    j.BeastZoneMustConfirm=true;j.ZoneStatus="Linh thú: chuyển khu "+j.ZoneId+" → "+target+", tránh acc khác; đợi xác nhận";
    if((now-j.ZoneActionAt).TotalSeconds<8)return;
    if(j.ZoneAttempts>=3){if((now-j.ZoneActionAt).TotalSeconds<30)return;ResetBeastCombatZone(j);j.BeastCombatMap=j.MapId;j.BeastZoneWaiting=true;return;}
    QueueZone(r,data,j,2,target);j.BeastZoneChangeSent=true;
   }catch(Exception e){j.ZoneStatus="Linh thú: chưa xác nhận khu riêng • "+e.Message;}
  }
  internal static bool ShouldCloseBeastRasaMenu(NativeJob j){return j.QuestPhase==0||j.QuestPhase==1||j.QuestPhase==6;}
  internal static void PumpBeastSoloParty(Remote r,long data,NativeJob j,long party,DateTime now){
   j.BeastSoloReady=party==0||j.TeamMembers.Length==0;j.TeamConfirmed=false;
   if(j.BeastSoloReady){j.TeamStatus="Linh thú: chạy riêng từng tài khoản";return;}
   j.TrainEnabled=false;j.QuestPauseAuto=true;j.GameAutoManaged=true;j.GameAutoWanted=false;j.TeamStatus="Linh thú: rời nhóm cũ; đợi server xác nhận";
   int pending=BitConverter.ToInt32(r.Read(data,4),0);if((pending==0||pending==6||pending==7)&&(now-j.TeamActionAt).TotalSeconds>=5){r.Write(data+32,BitConverter.GetBytes(party));TeamCommand(r,data,j,8,"");Log(j,"beast_solo_leave","");}
  }
  static void ReadBeastRasa(Remote r,long game,NativeJob j,long fc,long player){
   int px=BitConverter.ToInt16(r.Read(player+16,2),0),py=BitConverter.ToInt16(r.Read(player+18,2),0);j.BeastNearRasa=false;j.BeastRasaMenu=0;
   long npcClass=ResolvedClass(r,game+0x16ee910);
   foreach(long npc in ReadVector(r,r.Ptr(fc+0x158))){if(npc!=0&&r.Ptr(npc)==npcClass&&BitConverter.ToInt16(r.Read(npc+80,2),0)==98){
    int x=BitConverter.ToInt16(r.Read(npc+16,2),0),y=BitConverter.ToInt16(r.Read(npc+18,2),0);j.BeastNearRasa=Math.Abs(x-px)<=120&&Math.Abs(y-py)<=100;
    long popup=r.Ptr(fc+0x90);int tag=BitConverter.ToInt32(r.Read(npc+56,4),0);if(popup!=0&&r.Ptr(popup)==ResolvedClass(r,game+0x16efe08)&&BitConverter.ToInt32(r.Read(popup+0xdc,4),0)==tag&&Array.IndexOf(ReadVector(r,r.Ptr(fc+0x88)),popup)>=0){j.BeastRasaMenu=popup;j.BeastRasaTag=tag;}break;
   }}
  }
 }
}

namespace TGLL {
    public static partial class NativeBackend {
        // Timers belong to the fault episode, not to the command being retried.
        internal static string WatchedActivity(NativeJob j){
            if(j.BeastQuestEnabled||j.MailQueueOnly||j.CloakQueueOnly)return "";
            if(j.SonCapEnabled&&!j.SonCapFinished&&!j.SonCapNoAttempts)return "soncap";
            if(j.IzanamiEnabled&&!j.SonCapEnabled&&!j.IzanamiRunFinished)return "izanami";
            if(j.DungeonHighEnabled&&!j.DungeonFinished)return "dungeon";
            if(!j.DungeonHighEnabled&&j.ReceiveKeyEnabled&&j.State!="key_submitted"&&j.State!="key_received")return "key";
            if((j.QuestEnabled||j.QuestFaultStopped)&&!j.QuestFinished)return "quest";
            // Up may be waiting for enough team members before enabling its activity.
            if(j.DailyActive&&j.DailyStage>=1&&j.DailyStage<=5)return "daily_wait";
            return "";
        }
        static void ClearActivityFault(NativeJob j){j.ActivityHealthyAt=DateTime.MinValue;j.ActivityFaultAt=DateTime.MinValue;j.ActivityRetryIssued=false;j.ActivityRetryPending=false;j.ActivityWatchStatus="";}
        internal static string ActivityFaultReason(NativeJob j,string kind,DateTime now){
            if(!j.GameSettingsReady)return "game_not_ready";
            if(j.MapId<0||j.ZoneReadAt==DateTime.MinValue||(now-j.ZoneReadAt).TotalSeconds>=30)return "map_data_unavailable";
            if(!String.IsNullOrEmpty(j.Error))return j.Error;
            if(kind=="quest"){
                if(j.QuestFaultStopped)return "quest_stopped";
                if(j.QuestPhase==6&&(j.QuestMobId<0||j.QuestKind!=1||!j.TrainEnabled))return "quest_combat_not_ready";
                if(j.QuestSnapshotAt==DateTime.MinValue||(now-j.QuestSnapshotAt).TotalSeconds>=30)return "quest_data_unavailable";
            }
            if(kind=="dungeon"&&j.DungeonStopped)return "dungeon_stopped";
            if(kind=="dungeon"&&j.DungeonCycleWaiting&&j.DungeonLeftOrigin&&!j.GameAutoEnabled)return "dungeon_auto_not_enabled";
            if(kind=="soncap"){
                if(!String.IsNullOrEmpty(j.SonCapFailure))return j.SonCapFailure;
                if(!String.IsNullOrEmpty(j.SonCapReadError))return j.SonCapReadError;
                if(j.SonCapStage>=0&&!j.SonCapMoving&&j.SonCapAlive>0&&!j.GameAutoEnabled)return "soncap_auto_not_enabled";
            }
            if(kind=="izanami"&&j.MapId==89&&!j.GameAutoEnabled)return "izanami_auto_not_enabled";
            return "";
        }
        internal static string ActivityProgressKey(NativeJob j,string kind){
            // Only server/game observations count. Retry counters, command phases and status text do not.
            if(kind=="quest")return j.MapId+":"+j.QuestRemaining+":"+j.QuestCompleted+":"+j.QuestObservedCount+":"+j.QuestObservedTotal+":"+j.QuestMobId;
            if(kind=="soncap")return j.MapId+":"+j.SonCapStage+":"+j.SonCapAlive+":"+j.GameAutoEnabled;
            if(kind=="izanami")return j.MapId+":"+j.IzanamiStage+":"+j.IzanamiRoundsCompleted+":"+j.GameAutoEnabled;
            return j.MapId+":"+(kind=="dungeon"?j.GameAutoEnabled.ToString():"");
        }
        internal static bool IsActivityVillage(int map){return map==59||map==60||map==68||map==69||map==75||map==85||map==102;}
        internal static bool FreshActivityVillage(NativeJob j,DateTime now){return IsActivityVillage(j.MapId)&&j.ZoneReadAt!=DateTime.MinValue&&(now-j.ZoneReadAt).TotalSeconds>=0&&(now-j.ZoneReadAt).TotalSeconds<5;}
        static bool ConfirmedActivityHealthy(NativeJob j,string kind,DateTime now){
            if(!j.GameSettingsReady||j.SettingsReadAt==DateTime.MinValue||(now-j.SettingsReadAt).TotalSeconds>=5||
               j.ZoneReadAt==DateTime.MinValue||(now-j.ZoneReadAt).TotalSeconds>=5||IsActivityVillage(j.MapId))return false;
            if(kind=="dungeon")return j.DungeonCycleWaiting&&j.DungeonLeftOrigin&&!j.DungeonStopped&&j.GameAutoEnabled;
            if(kind=="izanami")return j.MapId==89&&j.GameAutoEnabled;
            if(kind=="soncap")return Array.IndexOf(SonCapMaps,j.MapId)>=0&&j.SonCapAlive>=0&&j.GameAutoEnabled;
            return kind=="quest"&&j.QuestPhase==6&&j.TrainEnabled&&j.QuestMobId>=0&&j.QuestKind==1&&
                   j.QuestSnapshotAt!=DateTime.MinValue&&(now-j.QuestSnapshotAt).TotalSeconds<5;
        }
        internal static bool ConfirmActivityCloseMap(NativeJob j,Func<int> read){
            if(!j.ActivityTimeoutClosed&&(j.CloseRequested||j.CancelRequested||WatchedActivity(j).Length==0))return false;
            try{return IsActivityVillage(read());}catch{return false;}
        }
        public static void PulseActivityWatchdog(){PulseActivityWatchdog(DateTime.UtcNow,null);}
        internal static void PulseActivityWatchdog(DateTime now,Action<NativeJob> close){foreach(NativeJob j in jobs.Values)ObserveActivityWatchdog(j,now,close);}
        internal static void ObserveActivityWatchdog(NativeJob j,DateTime now,Action<NativeJob> close){
            if(j.ActivityTimeoutClosed){
                // A failed close holds its queue slot and can be retried, without reopening the game.
                if(j.ProcessId>0&&(j.ActivityTimeoutCloseOperation==null||j.ActivityTimeoutCloseOperation.Completed)&&
                   (now-j.ActivityCloseAt).TotalSeconds>=5&&(close!=null||ManagedGames.IsRunning(j.ProcessId)))CloseTimedOutActivity(j,now,close);
                return;
            }
            if(!j.Auto||j.ProcessId<=0||j.CloseRequested||j.CancelRequested||j.ReconnectRequested||j.State=="closed"||j.State=="cancelled"){
                ClearActivityFault(j);j.ActivityVillageAt=DateTime.MinValue;return;
            }
            string kind=WatchedActivity(j);
            if(kind.Length==0){ClearActivityFault(j);j.ActivityVillageAt=DateTime.MinValue;j.ActivityWatchKind="";return;}
            bool fresh=j.MapId>=0&&j.ZoneReadAt!=DateTime.MinValue&&(now-j.ZoneReadAt).TotalSeconds<5;
            if(fresh){
                if(IsActivityVillage(j.MapId)){if(j.ActivityVillageAt==DateTime.MinValue)j.ActivityVillageAt=now;}
                else j.ActivityVillageAt=DateTime.MinValue;
            }
            string progress=ActivityProgressKey(j,kind),reason=ActivityFaultReason(j,kind,now);
            bool changed=j.ActivityWatchKind!=kind||j.ActivityProgress!=progress;
            // Changing the selected activity does not extend a continuous stay in a village.
            if(j.ActivityWatchKind!=kind)ClearActivityFault(j);
            j.ActivityWatchKind=kind;j.ActivityProgress=progress;
            if(reason.Length>0){j.ActivityHealthyAt=DateTime.MinValue;if(j.ActivityFaultAt==DateTime.MinValue){
                j.ActivityFaultAt=now;Log(j,"activity_watchdog_fault",",\"activity\":"+Quote(kind)+",\"reason\":"+Quote(reason)+",\"map\":"+j.MapId+",\"settings_error\":"+Quote(j.SettingsError));
            }}
            else if(j.ActivityFaultAt!=DateTime.MinValue){
                if(changed)ClearActivityFault(j);
                else if(ConfirmedActivityHealthy(j,kind,now)){
                    if(j.ActivityHealthyAt==DateTime.MinValue)j.ActivityHealthyAt=now;
                    if((now-j.ActivityHealthyAt).TotalSeconds>=30){Log(j,"activity_watchdog_recovered",",\"activity\":"+Quote(kind));ClearActivityFault(j);}
                }else j.ActivityHealthyAt=DateTime.MinValue;
            }
            DateTime since=j.ActivityFaultAt;
            if(j.ActivityVillageAt!=DateTime.MinValue&&(since==DateTime.MinValue||j.ActivityVillageAt<since))since=j.ActivityVillageAt;
            if(since==DateTime.MinValue)return;
            double seconds=(now-since).TotalSeconds;
            string cause=j.ActivityVillageAt==since?"village_wait":reason.Length>0?reason:"awaiting_recovery";
            if(seconds>=600){
                if(!FreshActivityVillage(j,now)){
                    j.ActivityWatchStatus="Auto lỗi/chờ quá 10 phút: giữ game, chưa xác nhận ở map làng";
                    return;
                }
                if(j.ActivityTimeoutCloseOperation!=null&&!j.ActivityTimeoutCloseOperation.Completed)return;
                if(j.ActivityCloseAt!=DateTime.MinValue&&(now-j.ActivityCloseAt).TotalSeconds<5)return;
                j.ActivityRetryPending=false;
                CloseTimedOutActivity(j,now,close,cause,(int)seconds);return;
            }
            if(seconds>=300&&!j.ActivityRetryIssued){
                j.ActivityRetryIssued=true;j.ActivityRetryPending=true;j.ActivityWatchStatus="Auto lỗi/chờ quá 5 phút: đang chạy lại";
                Log(j,"activity_timeout_retry",",\"activity\":"+Quote(kind)+",\"reason\":"+Quote(cause)+",\"elapsed_seconds\":"+(int)seconds);
            }
        }
        static void MarkActivityTimeoutClose(NativeJob j,string cause,int seconds){
            j.ActivityTimeoutClosed=true;j.ActivityRetryPending=false;
            j.ActivityWatchStatus="Auto lỗi/chờ quá 10 phút ở map làng: tắt game";
            Log(j,"activity_timeout_close",",\"activity\":"+Quote(j.ActivityWatchKind)+",\"reason\":"+Quote(cause)+",\"map\":"+j.MapId+",\"elapsed_seconds\":"+seconds);
        }
        static void CloseTimedOutActivity(NativeJob j,DateTime now,Action<NativeJob> close,string cause="retry_close",int seconds=600){
            j.ActivityCloseAt=now;
            if(close!=null){
                if(!FreshActivityVillage(j,now))return;
                MarkActivityTimeoutClose(j,cause,seconds);j.CloseRequested=true;j.CancelRequested=true;
                j.ActivityTimeoutCloseOperation=new CloseBatchOperation{Completed=true};close(j);return;
            }
            var op=new CloseBatchOperation();j.ActivityTimeoutCloseOperation=op;
            var worker=new Thread(delegate(){try{
                // Re-read the real game map immediately before closing: the UI snapshot can already be stale.
                using(Process p=Process.GetProcessById(j.ProcessId))using(var r=new Remote(p)){
                    long game=0;foreach(ProcessModule m in p.Modules)if(m.ModuleName=="GameAssembly.dll")game=m.BaseAddress.ToInt64();
                    int map=-1;
                    if(game==0||!ConfirmActivityCloseMap(j,delegate{map=ReadDungeonMap(r,game);return map;})){
                        j.ActivityWatchStatus="Chưa xác nhận ở map làng; giữ game";
                        Log(j,"activity_timeout_close_deferred",",\"map\":"+map);return;
                    }
                    j.MapId=map;j.ZoneReadAt=DateTime.UtcNow;MarkActivityTimeoutClose(j,cause,seconds);
                }
                op.Failed=CloseGame(j.Id).Failed;
            }catch(ArgumentException){if(!ManagedGames.IsRunning(j.ProcessId)){j.ActivityTimeoutClosed=true;j.CloseRequested=true;j.CancelRequested=true;}else op.Failed++;}
             catch{op.Failed++;j.ActivityWatchStatus="Chưa xác nhận/tắt được game; giữ lượt để thử lại";}
             finally{op.Completed=true;}});
            worker.IsBackground=true;worker.Start();
        }
        internal static bool PrepareActivityRetry(NativeJob j,DateTime now){
            if(!j.ActivityRetryPending||j.CloseRequested||j.CancelRequested)return false;
            j.ActivityRetryPending=false;string kind=WatchedActivity(j);if(kind.Length==0)return false;
            j.Error="";j.ReplayRequested=false;j.ReceiveFlowActive=false;j.ReceiveKeyRequested=false;j.KeyReceiptPending=false;
            j.PartyReady=false;j.TeamAttempts=0;j.TeamAttemptCommand=0;j.TeamFriendRequested=false;j.TeamConfirmed=false;
            j.ZonePrepared=false;j.ZoneAttempts=0;j.ZoneListWaiting=false;j.ZoneActionAt=DateTime.MinValue;
            if(kind=="quest"){
                j.QuestFaultStopped=false;j.QuestEnabled=true;j.QuestPauseAuto=true;j.QuestPhase=0;j.QuestInFlight=false;
                j.QuestBusyAt=DateTime.MinValue;j.QuestBusyRetries=0;j.QuestRetryAt=DateTime.MinValue;j.QuestAt=now;
                j.TrainEnabled=false;j.QuestStatus="Thử lại nhiệm vụ sau 5 phút bị lỗi";
            }else if(kind=="dungeon"){
                j.DungeonStopped=false;j.DungeonFinished=false;j.DungeonEntryRetries=0;j.DungeonKeyChecked=false;
                if(j.MapId==59){j.DungeonCycleWaiting=false;j.DungeonRepeatRequested=false;j.DungeonStartAt=now;j.NpcAction=1;}else if(!j.DungeonCycleWaiting)j.ReceiveKeyRequested=true;
                else if(j.DungeonLeftOrigin){j.GameAutoManaged=true;j.GameAutoWanted=true;}
                // In the dungeon keep the current run and observe its return; never submit entry from inside.
            }else if(kind=="key"){j.ReceiveKeyRequested=true;}
            else if(kind=="soncap"||kind=="izanami"){
                j.IzanamiTravelStopped=false;
                if(j.MapId==59){
                    j.IzanamiRoundEntered=false;j.IzanamiRoundComplete=false;j.IzanamiTravelReady=true;
                    j.TravelReady=true;j.EntryAutoWaiting=false;j.IzanamiRequested=IsIzanamiLeader(j);
                    if(kind=="soncap"){j.SonCapStage=-1;j.SonCapMoving=false;j.SonCapEntrySubmitted=false;}
                }
                if(kind=="soncap"){
                    j.SonCapFailure="";j.SonCapReadError="";j.SonCapPollAt=DateTime.MinValue;
                    j.SonCapRouteRetries=0;j.SonCapRightAttempts=0;j.SonCapStopPending=j.SonCapMoving;
                    j.SonCapMoving=false;j.SonCapEmptyAt=DateTime.MinValue;j.SonCapAutoRetryAt=DateTime.MinValue;
                    j.SonCapAutoConfirmed=false;j.SonCapAutoSeenAt=DateTime.MinValue;
                }else{j.IzanamiMapSeenAt=DateTime.MinValue;j.IzanamiMapPollAt=DateTime.MinValue;}
            }
            j.State="opened";return true;
        }
        static void PumpActivityRetry(Remote r,long data,long questData,NativeJob j,Stopwatch npcTimer){
            if(!j.ActivityRetryPending||j.ReconnectRequested)return;
            if(WatchedActivity(j)!=j.ActivityWatchKind){j.ActivityRetryPending=false;return;}
            // Run on the session worker, alongside the native commands it resets.
            // A quest command still executing must finish before its buffer can be reused.
            if(j.ActivityWatchKind=="quest"&&BitConverter.ToInt32(r.Read(questData,4),0)==99)return;
            if(!PrepareActivityRetry(j,DateTime.UtcNow))return;
            CancelTravel(r,j);npcTimer.Reset();r.Write(data,BitConverter.GetBytes(12));
            if(j.ActivityWatchKind=="quest")r.Write(questData,new byte[4]);
            if(j.ActivityWatchKind=="dungeon"&&j.MapId==59)npcTimer.Start();
        }
    }
}
