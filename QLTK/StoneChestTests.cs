using System;using System.Collections.Generic;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class StoneChest131Tests{
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Alloc(IntPtr cls);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Constructor(IntPtr self,byte op,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void WriteInt(IntPtr self,int value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Flush(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Catalog(int category,IntPtr info);
 static void Check(bool b,string s){if(!b)throw new Exception(s);}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}static void P(Remote r,long a,long v){r.Write(a,BitConverter.GetBytes(v));}static int Read(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static InventorySnapshot Bag(int amount){return new InventorySnapshot(new[]{new InventoryItem{TemplateId=161,Quantity=amount},new InventoryItem{TemplateId=162,Quantity=999}},144,true,"",DateTime.UtcNow);}
 static NativeJob Job(){var j=new NativeJob("fixture","synthetic","",null,true);j.GameSettingsReady=true;j.TrainReady=true;j.ChestEnabled=true;j.ChestQuantity=3;j.ChestOpenAfter=true;return j;}
 static int Main(){try{NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/chest-test-logs");using(Remote r=new Remote(Process.GetCurrentProcess())){
 long data=r.Allocate(4096),use=r.Allocate(4096),slot=r.Allocate(8),cls=r.Allocate(1024),service=r.Allocate(32);P(r,slot,cls);P(r,service,cls);
 int catalogs=0,buys=0;byte opcode=0;List<int> writes=new List<int>();Tick tick=null;
 Alloc allocate=delegate{return new IntPtr(service);};Constructor ctor=delegate(IntPtr self,byte op,IntPtr info){Check(self.ToInt64()==service,"service instance");opcode=op;};
 WriteInt write=delegate(IntPtr self,int value,IntPtr info){Check(Read(r,data)==2,"in-flight state");tick();Check(Read(r,data)==2,"reentry held");writes.Add(value);};Flush flush=delegate{buys++;};Catalog catalog=delegate(int cat,IntPtr info){Check(cat==7,"locked gold catalog only");catalogs++;};
 P(r,cls+0x248,Marshal.GetFunctionPointerForDelegate(write).ToInt64());P(r,cls+0x298,Marshal.GetFunctionPointerForDelegate(flush).ToInt64());
 byte[] code=NativeCode.StoneChestCore(data,slot,Marshal.GetFunctionPointerForDelegate(allocate).ToInt64(),Marshal.GetFunctionPointerForDelegate(ctor).ToInt64(),0,Marshal.GetFunctionPointerForDelegate(catalog).ToInt64());long executable=r.Allocate(4096);r.Write(executable,code);r.Protect(executable,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(executable),typeof(Tick));
 I(r,data+12,1);I(r,data+4,1);I(r,data,1);tick();tick();Check(catalogs==1&&Read(r,data+24)==2,"catalog once");
 I(r,data+4,2);I(r,data+8,3);I(r,data+20,50);I(r,data,1);tick();tick();Check(buys==1&&opcode==0x79&&writes.Count==2&&writes[0]==50&&writes[1]==3&&Read(r,data+24)==3,"bulk purchase packet sale then quantity, once");
 I(r,data+12,0);I(r,data,1);tick();Check(buys==1,"gate cancels queued buy");I(r,data+12,1);I(r,data+8,0);I(r,data,1);tick();Check(buys==1,"invalid quantity blocked");
 r.Write(data,new byte[64]);r.Write(use,new byte[64]);var j=Job();int amount=4;var offer=new ChestOffer{SaleId=50,TemplateId=161,LockedGoldPrice=199,LockedOnly=true,Name="Rương Tinh Thạch"};DateTime now=DateTime.UtcNow;
 Action pump=delegate{NativeBackend.PumpStoneChest(r,data,use,0,j,()=>Bag(amount),()=>offer,now);now=now.AddSeconds(1);};
 pump();Check(j.ChestPhase==1&&Read(r,data+4)==1,"request catalog before buying");tick();offer.LockedOnly=false;pump();Check(j.ChestPhase==1&&Read(r,data)==0,"regular gold rejected");offer.LockedOnly=true;offer.Name="Rương đá";pump();Check(j.ChestPhase==1,"wrong name rejected");offer.Name="Rương Tinh Thạch";offer.TemplateId=277;pump();Check(j.ChestPhase==1,"wrong ID rejected");offer.TemplateId=161;amount=5;pump();Check(j.ChestPhase==2&&j.ChestBefore==5&&Read(r,data+20)==50&&Read(r,data+8)==3,"fresh inventory and live SKU");tick();int priorBuys=buys;
 for(int n=0;n<4;n++){pump();tick();}Check(buys==priorBuys&&j.ChestPhase==2,"pending receipt never repurchased");amount=8;pump();Check(j.ChestBought==3&&j.ChestRemaining==3&&j.ChestPhase==3,"receipt confirmed exact delta");
 pump();Check(j.ChestUseOwned&&Read(r,use+4)==161&&Read(r,use)==1&&Read(r,use+24)==3,"use correct chest ID and batch");j.AutoItemEnabled=true;j.AutoItemId=162;NativeBackend.PumpAutoItem(r,use,j);Check(Read(r,use+4)==161,"generic item does not overwrite chest request");
 j.TravelActive=true;pump();Check(Read(r,use+12)==0,"pending use paused for NPC");j.TravelActive=false;pump();Check(Read(r,use+12)==1,"resume ready gate");I(r,use,0);I(r,use+8,2);
 pump();Check(j.ChestPhase==4&&Read(r,use)==0,"wait for item reduction, no repeated use");amount=7;pump();Check(j.ChestUsed==0&&j.ChestPhase==4&&Read(r,use)==0,"partial receipt waits for whole batch without repeated use");amount=5;pump();Check(j.ChestUsed==3&&j.ChestRemaining==0,"full batch confirmed");
 now=now.AddSeconds(2);pump();Check(!j.ChestEnabled&&j.ChestUsed==3&&amount==5,"only purchased quantity opened, old chests retained");
 j=Job();j.ChestUseOnly=true;amount=2;pump();Check(j.ChestRemaining==2&&j.ChestPhase==3,"use all existing");pump();j.ChestEnabled=false;pump();Check(j.ChestUseOwned&&Read(r,use+12)==0,"stop cancels queued shared use while retaining ownership");I(r,use,0);pump();Check(!j.ChestUseOwned,"ownership released after drain");
 r.Write(data,new byte[64]);r.Write(use,new byte[64]);j=Job();amount=0;pump();tick();pump();tick();now=now.AddSeconds(21);pump();Check(!j.ChestEnabled&&j.ChestBought==0&&Read(r,data)==0,"unconfirmed purchase holds without resend");
 j=Job();j.ChestUseOnly=true;amount=0;pump();pump();Check(!j.ChestEnabled&&Read(r,use)==0,"empty inventory exits without use");
 GC.KeepAlive(allocate);GC.KeepAlive(ctor);GC.KeepAlive(write);GC.KeepAlive(flush);GC.KeepAlive(catalog);
 Console.WriteLine("PASS: locked catalog/category7, opcode121 sale50+quantity, reentry/once-only/gate, currency/name/ID validation, fresh baseline, receipt timeout, purchased-only opening, generic item isolation, pause/stop and empty bag; "+code.Length+" bytes.");
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
