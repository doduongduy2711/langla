using System;
using System.Collections.Generic;
using System.Reflection;
namespace TGLL {
 public static class ManagerStatusCache {
  static readonly string[] Names={"State","Error","StartAttempts","CloseRequested","CancelRequested","ReadyPhase","NpcCount","IzanamiEnabled","IzanamiRequested","IzanamiStage","IzanamiRoundsCompleted","IzanamiRoundsTarget","IzanamiRoundComplete","IzanamiNoAttempts","TeamStatus","SonCapEnabled","SonCapStage","SonCapFinished","SonCapFailure","SonCapStatus","SonCapRegroupAfterReturn","DailyActive","DailyStage","DailyStatus","QuestEnabled","QuestFinished","QuestRemaining","QuestStatus","BeastQuestEnabled","FarmEnabled","DungeonHighEnabled","CurseActive","CurseReadAt","CaptchaStatus","ActivityWatchStatus"};
  sealed class Snapshot {internal object Job;internal WeakReference Row;internal string Extra;internal object[] Values;}
  static readonly Dictionary<Type,MemberInfo[]> Plans=new Dictionary<Type,MemberInfo[]>();
  static readonly Dictionary<string,Snapshot> States=new Dictionary<string,Snapshot>();
  static MemberInfo[] Plan(Type type){
   MemberInfo[] found;if(Plans.TryGetValue(type,out found))return found;
   var members=new List<MemberInfo>();foreach(string name in Names){var field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(field!=null){members.Add(field);continue;}var property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public);if(property!=null&&property.CanRead&&property.GetIndexParameters().Length==0)members.Add(property);}
   found=members.ToArray();Plans[type]=found;return found;
  }
  // Called on the manager thread, using cached job data only; no process/memory/network reads.
  public static bool Changed(string account,object job,object row,string extra){
   if(job==null){States.Remove(account);return true;}
   var members=Plan(job.GetType());if(members.Length==0)return true;
   var values=new object[members.Length];for(int n=0;n<members.Length;n++){
    var field=members[n] as FieldInfo;object value=field!=null?field.GetValue(job):((PropertyInfo)members[n]).GetValue(job,null);
    if(members[n].Name=="CurseReadAt"&&value is DateTime)value=(DateTime.UtcNow-(DateTime)value).TotalSeconds<=5;
    values[n]=value;
   }
   Snapshot old;bool changed=!States.TryGetValue(account,out old)||!Object.ReferenceEquals(old.Job,job)||!Object.ReferenceEquals(old.Row.Target,row)||old.Extra!=extra||old.Values.Length!=values.Length;
   if(!changed)for(int n=0;n<values.Length;n++)if(!Object.Equals(old.Values[n],values[n])){changed=true;break;}
   if(changed){if(States.Count>10000)States.Clear();States[account]=new Snapshot{Job=job,Row=new WeakReference(row),Extra=extra,Values=values};}
   return changed;
  }
  public static void Clear(){States.Clear();}
 }
}
