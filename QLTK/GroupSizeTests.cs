using System;using System.Linq;using System.Reflection;using System.Collections.Concurrent;using TGLL;
class GroupSize122 {
 static void C(bool b,string s){if(!b)throw new Exception(s);}
 static void Main(){var jobs=(ConcurrentDictionary<string,NativeJob>)typeof(NativeBackend).GetField("jobs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var a=Enumerable.Range(0,10).Select(i=>new NativeJob("synthetic.exe","fixture"+i,"unused",null,true)).ToArray();foreach(var j in a)jobs[j.Id]=j;var ids=a.Select(j=>j.Id).ToArray();
 foreach(int size in new[]{1,2,4,6}){NativeBackend.IzanamiTeamSize=size;foreach(var j in a)j.DailyManaged=false;NativeBackend.ConfigureIzanamiTeams(ids);for(int i=0;i<a.Length;i++){var expected=ids.Skip(i/size*size).Take(size).ToArray();C(a[i].TeamJobIds.SequenceEqual(expected),"ordinary grouping order "+size);}
 foreach(var j in a){j.DailyManaged=true;j.DailyPlan[2]=true;j.DailyActive=false;}NativeBackend.ConfigureDailyTeams(ids);for(int i=0;i<a.Length;i++)C(a[i].DailyTeamIds.SequenceEqual(ids.Skip(i/size*size).Take(size)),"daily grouping order "+size);
 a[0].DailyActive=true;var old=a[0].DailyTeamIds;NativeBackend.IzanamiTeamSize=size==1?4:1;NativeBackend.ConfigureDailyTeams(ids);C(a[0].DailyTeamIds.SequenceEqual(old),"active daily team changed");a[0].DailyActive=false;
 }
 bool bad=false;try{NativeBackend.IzanamiTeamSize=7;}catch(ArgumentOutOfRangeException){bad=true;}C(bad,"invalid size accepted");Console.WriteLine("PASS: team sizes 1/2/4/6, stable row order, final partial team, daily grouping and active-team preservation.");}
}
