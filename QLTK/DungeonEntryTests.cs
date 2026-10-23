using System;using System.Diagnostics;using TGLL;
class DungeonEntryTests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static InventorySnapshot Bag(int keys,bool ready=true){return new InventorySnapshot(keys==0?new InventoryItem[0]:new[]{new InventoryItem{Name="Chìa khóa địa cung (cao cấp)",Quantity=keys}},50,ready,"",DateTime.UtcNow);}
 static NativeJob Job(){return new NativeJob("synthetic.exe","fixture","unused",null,true){DungeonHighEnabled=true,DungeonOriginMap=59,DungeonKeyCount=1,DungeonReturnConfirmed=true};}
 static void Main(){
  var now=DateTime.UtcNow;var j=Job();NativeBackend.TrackDungeonEntrySubmission(j,now);
  Check(j.DungeonReturnConfirmed&&!j.DungeonFinished&&j.DungeonCycleWaiting,"submission erased actual return or finished prematurely");
  Check(!NativeBackend.RecheckReturnedDungeonKeys(j,59,()=>Bag(1)),"remaining key marked exhausted");
  Check(!NativeBackend.RecheckReturnedDungeonKeys(j,59,()=>Bag(0,false))&&j.DungeonKeyCount==1,"invalid inventory erased key count");
  Check(!NativeBackend.RecheckReturnedDungeonKeys(j,59,()=>{throw new Exception("snapshot unavailable");})&&j.DungeonKeyCount==1,"read error marked exhausted");
  Check(NativeBackend.RecheckReturnedDungeonKeys(j,59,()=>Bag(0))&&j.DungeonKeyCount==0,"completed round / stale one key did not refresh to zero");
  var first=Job();first.DungeonReturnConfirmed=false;NativeBackend.TrackDungeonEntrySubmission(first,now);
  Check(!NativeBackend.RecheckReturnedDungeonKeys(first,59,()=>Bag(0)),"unconfirmed first entry marked exhausted");
  NativeBackend.ObserveDungeonCycle(null,0,j,new Stopwatch(),70,now);
  Check(j.DungeonLeftOrigin&&!j.DungeonReturnConfirmed&&!NativeBackend.RecheckReturnedDungeonKeys(j,70,()=>Bag(0)),"active dungeon released its slot");
  var refused=Job();NativeBackend.TrackDungeonEntrySubmission(refused,now);refused.DungeonResumeProbe=false;
  Check(!NativeBackend.ObserveDungeonProbeRefusal(refused,59,"Địa cung này không phù hợp với cấp độ của bạn"),"unrelated refusal marked no keys");
  Check(!NativeBackend.ObserveDungeonProbeRefusal(refused,70,"Bạn không đủ chìa khóa địa cung"),"wrong map accepted rejection");
  Check(NativeBackend.ObserveDungeonProbeRefusal(refused,59,"Bạn không đủ chìa khóa địa cung")&&refused.DungeonKeyCount==0&&refused.DungeonProbeRefused&&!refused.DungeonCycleWaiting,"fresh server denial with stale key count ignored");
  var reconnect=Job();reconnect.DungeonReturnConfirmed=false;NativeBackend.TrackDungeonEntrySubmission(reconnect,now);reconnect.DungeonResumeProbe=true;reconnect.DungeonKeyCount=0;
  Check(NativeBackend.ObserveDungeonProbeRefusal(reconnect,59,"Bạn không có chìa khóa địa cung"),"resume denial handling regressed");
  Console.WriteLine("PASS: completed return survives submission, fresh keys before retry timeout, read-error/first-entry/active-map guards, server no-key refusal for normal and resume entries.");
 }
}
