using System;using System.Collections.Generic;using TGLL;
class SonCapFocusTests {
 static void C(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){
  var calls=new List<string>();Func<uint,uint,bool,bool> attach=(a,b,v)=>{calls.Add(b+":"+v);return true;};
  C(SonCapPortalInput.FocusAttached(1,2,3,attach,()=>true),"activation failed");C(String.Join(",",calls)=="2:True,3:True,3:False,2:False","success cleanup order");
  calls.Clear();C(!SonCapPortalInput.FocusAttached(1,2,3,attach,()=>false),"false focus accepted");C(calls.Count==4,"false result leaked attachment");
  calls.Clear();try{SonCapPortalInput.FocusAttached(1,2,3,attach,()=>{throw new Exception("fixture");});}catch(Exception e){C(e.Message=="fixture","exception lost");}C(calls.Count==4,"exception leaked attachment");
  calls.Clear();SonCapPortalInput.FocusAttached(1,2,2,attach,()=>true);C(calls.Count==2,"duplicate attachment");
  calls.Clear();SonCapPortalInput.FocusAttached(1,1,1,attach,()=>true);C(calls.Count==0,"self attachment");
  calls.Clear();SonCapPortalInput.FocusAttached(1,2,3,(a,b,v)=>{calls.Add(b+":"+v);return false;},()=>false);C(calls.Count==2,"detached unsuccessful attachment");
  Console.WriteLine("PASS: focus attachment cleanup on success/failure/exception, no duplicate/self attachment, failed attachment handling; no real input sent.");
 }
}
