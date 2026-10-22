using System;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class QuestCloseTests{
 static void C(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static void Main(){var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var j=new NativeJob("synthetic.exe","fixture","unused",null,true);jobs[j.Id]=j;j.ProcessId=1;j.State="opened";j.QuestEnabled=false;j.QuestFinished=true;j.QuestRemaining=0;int closes=0;Action<NativeJob> close=p=>closes++;
  NativeBackend.QuestCloseAfterEnabled=false;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"unchecked must keep game");
  NativeBackend.QuestCloseAfterEnabled=true;j.QuestFinished=false;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"timeout or unfinished task must not close");j.QuestFinished=true;
  j.QuestEnabled=true;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"resumed active task must not close");j.QuestEnabled=false;
  j.UpRequested=true;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"Up must keep game");j.UpRequested=false;j.DailyManaged=true;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"daily priority must keep game");j.DailyManaged=false;
  j.QuestQueueOnly=true;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"queue owns its own close");j.QuestQueueOnly=false;
  j.ReconnectRequested=true;NativeBackend.PulseQuestAutoClose(close);C(closes==0,"reconnecting must not close");j.ReconnectRequested=false;
  NativeBackend.PulseQuestAutoClose(close);NativeBackend.PulseQuestAutoClose(close);C(closes==1,"confirmed completion outside queue must close exactly once");Console.WriteLine("PASS: finished direct quest closes once; unchecked, unfinished/timeout, active task, Up, daily, queued and reconnect preserved. No real game closed.");
 }
}
