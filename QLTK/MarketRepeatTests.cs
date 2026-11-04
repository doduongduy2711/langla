using System;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using TGLL;
class MarketRepeatTests {
 static void C(bool ok,string text){if(!ok)throw new Exception(text);}
 sealed class Fixture : IDisposable {
  public Remote R=new Remote(Process.GetCurrentProcess());public long D;public NativeJob J;
  public List<InventoryItem> Bag=new List<InventoryItem>();public List<MarketListing> Own=new List<MarketListing>();
  public int Sends,Splits;public bool Ack=true;public long Silver=100000;long array=100,nextId=1000;DateTime now=DateTime.UtcNow;
  public Fixture(int limit,string rules){D=R.Allocate(8192);J=new NativeJob("fixture.exe","fixture","",null,true){GameSettingsReady=true,MarketEnabled=true,MarketMode=2,MarketMaxSells=limit,MarketRules=NativeBackend.ParseMarketRules(rules,true)};}
  int I(long p){return BitConverter.ToInt32(R.Read(p,4),0);}void Clear(long p){R.Write(p,new byte[4]);}
  public void Add(int id,int qty,bool locked=false){int slot=Bag.Count;Bag.Add(new InventoryItem{TemplateId=id,Quantity=qty,Slot=slot,BagIndex=slot,Locked=locked});}
  public void Step(){
   NativeBackend.PumpMarket(R,D,0,J,()=>new MarketSnapshot{Panel=1,Array=1,Items=new MarketListing[0]},()=>new InventorySnapshot(Bag.ToArray(),40,true,"",now),()=>Silver,()=>new MarketSnapshot{Array=array,Items=Own.ToArray()},now);
   if(I(D)==1){int command=I(D+4);if(command==5)array++;if(command==2){Sends++;if(Ack){int slot=I(D+24);var item=Bag.Single(x=>x.Slot==slot);Own.Add(new MarketListing{Id=nextId++,ItemId=item.TemplateId,Quantity=item.Quantity,Price=I(D+40)});Bag.Remove(item);}}Clear(D);}
   if(I(D+4096)==1){Splits++;long sd=D+4096;var source=Bag.Single(x=>x.Slot==I(sd+20));int qty=I(sd+32);source.Quantity-=qty;int slot=Bag.Count==0?0:Bag.Max(x=>x.Slot)+1;Bag.Add(new InventoryItem{TemplateId=source.TemplateId,Quantity=qty,Slot=slot,BagIndex=slot});Clear(sd);}
   now=now.AddSeconds(4);
  }
  public void Finish(){for(int n=0;n<100&&J.MarketEnabled;n++)Step();C(!J.MarketEnabled,"fixture never stopped");}
  public void Dispose(){R.Dispose();}
 }
 static void Main(){
  NativeBackend.LogDirectory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"QLTK-market-repeat-"+Guid.NewGuid().ToString("N"));
  using(var f=new Fixture(3,"277,5,100")){for(int n=0;n<5;n++)f.Add(277,5);f.Finish();C(f.Sends==3&&f.J.MarketSold==3&&f.Bag.Count==2,"listing cap did not stop at three confirmed listings");}
  using(var f=new Fixture(20,"277,5,100")){f.Add(277,12);f.Finish();C(f.Sends==2&&f.J.MarketSold==2&&f.Splits==2&&f.Bag.Single().Quantity==2,"split/repeat did not stop at insufficient remainder");}
  using(var f=new Fixture(9,"277,5,100\n498,1,200")){f.Add(277,5);f.Add(277,5);f.Add(277,5);f.Add(498,1);f.Finish();C(f.Sends==4&&f.J.MarketSold==4,"exhausted final rule prevented cycling back to available earlier rule");C(f.Own[0].ItemId==277&&f.Own[1].ItemId==498,"multiple rules did not retain listing order");}
  using(var f=new Fixture(3,"277,5,100")){f.Add(277,5,true);f.Add(277,2);f.Finish();C(f.Sends==0&&f.Splits==0,"locked or insufficient stock listed");}
  using(var f=new Fixture(3,"277,5,100")){f.Add(277,5);f.Ack=false;f.Finish();C(f.Sends==1&&f.J.MarketSold==0,"unconfirmed listing was repeated/counted");}
  using(var f=new Fixture(3,"277,5,100")){f.Add(277,5);f.Silver=0;f.Finish();C(f.Sends==0,"listing sent without fee funds");}
  using(var f=new Fixture(3,"277,5,100")){f.Add(277,5);f.J.CancelRequested=true;f.Step();C(f.Sends==0,"cancelled activity listed");}
  Console.WriteLine("PASS: capped confirmed repeat listings, exact splits until stock depleted, multi-rule rotation and exhausted-rule skip, locked stock, fee funds, cancelled session and unconfirmed-send no-retry. No game transaction.");
 }
}
