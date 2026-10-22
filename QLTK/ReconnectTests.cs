using System;
using TGLL;
class ReconnectTests {
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static NativeJob Job(){return new NativeJob("fixture.exe","fixture",null,null,true);}
    static int Main(string[] args){
        NativeBackend.LogDirectory=args[0];
        Check(NativeBackend.IsDisconnectNotice("Mất kết nối với máy chủ."),"Vietnamese popup");
        Check(NativeBackend.IsDisconnectNotice("Mat ket noi\nvoi may chu"),"Wrapped popup");
        Check(!NativeBackend.IsDisconnectNotice("Bạn đã nhận chìa khóa trong ngày"),"Daily notice");
        Check(!NativeBackend.IsDisconnectNotice("Không đủ MP"),"Combat notice");
        var now=DateTime.UtcNow;var j=Job();j.QuestEnabled=true;j.GameAutoWanted=true;
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now),"Immediate restart");
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(1)),"Unstable restart");
        Check(NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(2)),"No confirmed restart");
        Check(j.State=="reconnecting"&&j.Error=="game_disconnected","Missing state");
        Check(j.QuestEnabled&&j.GameAutoWanted&&!j.CloseRequested&&!j.CancelRequested,"Lost queue or auto intent");
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(9)),"Duplicate restart");
        j=Job();NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now);
        NativeBackend.ObserveDisconnect(j,"",now.AddSeconds(1));
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(3)),"Cleared popup triggered restart");
        j=Job();j.CloseRequested=true;
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(8)),"Manual close reopened");
        j=Job();j.CancelRequested=true;
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(8)),"Manual cancel reopened");
        NativeBackend.AutoReconnectEnabled=false;j=Job();
        Check(!NativeBackend.ObserveDisconnect(j,"Mất kết nối với máy chủ",now.AddSeconds(8)),"Disabled feature restarted");
        Console.WriteLine("PASS: disconnect text, stability, cleared popup, deduplication, queue/auto intent, close/cancel and disabled feature.");return 0;
    }
}
