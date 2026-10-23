using System;using TGLL;
class ClosePolicy120 {
 static void Check(bool ok){if(!ok)throw new Exception("Close policy failed");}
 static void Main(){var j=new NativeJob("synthetic.exe","fixture","unused",null,true);
 NativeBackend.IzanamiCloseAfterEnabled=false;Check(NativeBackend.ShouldCloseIzanamiLimit(j));
 NativeBackend.IzanamiCloseAfterEnabled=true;Check(NativeBackend.ShouldCloseIzanamiLimit(j));
 j.DailyManaged=true;Check(!NativeBackend.ShouldCloseIzanamiLimit(j));
 j.DailyManaged=false;j.IzanamiQueueOnly=true;Check(!NativeBackend.ShouldCloseIzanamiLimit(j));
 Console.WriteLine("PASS: server refusal closes regardless of completion checkbox; daily workflow and queue excluded from native close.");}
}
