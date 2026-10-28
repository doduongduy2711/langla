using System;using TGLL;
class ManagerStatusCacheTests {
 class Job {public string State="opened",QuestStatus="running";public int QuestRemaining=3,Hp=100;public DateTime CurseReadAt=DateTime.UtcNow;}
 static void C(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Main(){
  var job=new Job();var row=new object();C(ManagerStatusCache.Changed("a",job,row,""),"initial status missing");
  for(int n=0;n<100;n++){job.Hp--;job.CurseReadAt=DateTime.UtcNow;C(!ManagerStatusCache.Changed("a",job,row,""),"unchanged activity status repainted on a poll");}
  job.QuestRemaining=2;C(ManagerStatusCache.Changed("a",job,row,""),"quest progress not published");C(!ManagerStatusCache.Changed("a",job,row,""),"progress painted twice");
  job.State="failed";C(ManagerStatusCache.Changed("a",job,row,""),"error transition missing");
  C(ManagerStatusCache.Changed("a",job,row,"blocked"),"Up blocked status missing");C(ManagerStatusCache.Changed("a",job,new object(),"blocked"),"rebuilt row missing status");
  C(ManagerStatusCache.Changed("a",new Job(),row,""),"reopened account missing status");
  ManagerStatusCache.Clear();C(ManagerStatusCache.Changed("b",job,row,""),"second account missing");job.CurseReadAt=DateTime.UtcNow.AddSeconds(-10);C(ManagerStatusCache.Changed("b",job,row,""),"stale curse status not cleared");
  Console.WriteLine("PASS: unchanged activity state stays quiet across 100 polls; progress, failed/reopened jobs, rebuilt rows, blocked Up and stale curse transitions publish once. No game action.");
 }
}
