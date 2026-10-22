using System;using System.Reflection;using System.Collections.Concurrent;using System.Diagnostics;using TGLL;
class UpClose121 {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){
 var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 var j=new NativeJob("synthetic.exe","fixture","unused",null,true);jobs[j.Id]=j;j.UpRequested=true;j.State="opened";j.TrainReady=true;
 NativeBackend.IzanamiCloseAfterEnabled=true;
 C(!NativeBackend.ShouldCloseIzanamiLimit(j),"pending Up closes before farm ready");C(!NativeBackend.CanAutoCloseBatch(new[]{j.Id}),"queue closes Up");
 C(NativeBackend.BeginAutoCloseBatch(new[]{j.Id}).Error=="up_keeps_game_open"&&!j.CloseRequested,"automatic batch closed protected account");
 NativeBackend.SetCloseAfterKey(j.Id,true);C(!j.CloseAfterKey,"key preference not suppressed");
 j.CloseAfterKey=true;bool closed=false;j.KeyReceiptPending=true;j.KeyNoticeBefore="";
 NativeBackend.PumpKeyReceipt(null,0,j,new Stopwatch(),0,()=>new InventorySnapshot(new InventoryItem[0],1,true,"",DateTime.UtcNow),()=>closed=true,()=>"Má»—i ngÃ y nháº­n 1 láº§n");
 C(!closed&&!j.CloseRequested,"late key confirmation closes Up");
 NativeBackend.StopMapFarm(j.Id);C(!j.UpRequested&&NativeBackend.CanAutoCloseBatch(new[]{j.Id}),"manual stop leaves protection stale");
 var reopened=new NativeJob("synthetic.exe","fixture","unused",null,true);jobs[reopened.Id]=reopened;reopened.UpRequested=true;
 C(!NativeBackend.CanAutoCloseBatch(new[]{reopened.Id}),"new session lacks protection");
 Console.WriteLine("PASS: pending Up, new session, refusal, queue close, key close and manual stop policy.");
 }
}
