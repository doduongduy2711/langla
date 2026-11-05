using System;using System.Diagnostics;using System.Runtime.InteropServices;using TGLL;
class Cloak132Tests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Count(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Element(IntPtr self,int index,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Barrier(IntPtr at,IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void ActionFn(IntPtr self,IntPtr info);
 static void Check(bool b,string s){if(!b)throw new Exception(s);}
 static void I(Remote r,long a,int v){r.Write(a,BitConverter.GetBytes(v));}static void P(Remote r,long a,long v){r.Write(a,BitConverter.GetBytes(v));}static int R(Remote r,long a){return BitConverter.ToInt32(r.Read(a,4),0);}
 static long S(Remote r,string text){long p=r.Allocate(1024);I(r,p+16,text.Length);r.Write(p+20,System.Text.Encoding.Unicode.GetBytes(text));return p;}
 static void Replace(byte[] b,long before,long after){byte[] find=BitConverter.GetBytes(before),replace=BitConverter.GetBytes(after);for(int i=0;i<=b.Length-8;i++){bool same=true;for(int n=0;n<8;n++)if(b[i+n]!=find[n])same=false;if(same){Array.Copy(replace,0,b,i,8);i+=7;}}}
 static NativeJob Job(){var j=new NativeJob("fixture","synthetic","",null,true);j.ReceiveKeyEnabled=false;j.GameSettingsReady=true;j.TrainReady=true;j.CloakEnabled=true;j.CloakId=131;j.CloakQuantity=2;j.CloakExpectedPrice=900;return j;}
 static InventorySnapshot Bag(int amount,bool travel){var items=new System.Collections.Generic.List<InventoryItem>();for(int n=0;n<amount;n++)items.Add(new InventoryItem{TemplateId=131,Quantity=0,Equipment=true});if(travel)items.Add(new InventoryItem{TemplateId=168,Quantity=1});return new InventorySnapshot(items.ToArray(),144,true,"",DateTime.UtcNow);}
 static int Main(){try{NativeBackend.LogDirectory=System.IO.Path.GetFullPath("work/cloak-test-logs");using(var r=new Remote(Process.GetCurrentProcess())){
 long game=r.Allocate(0x1700000),data=r.Allocate(4096),fcClass=r.Allocate(256),stat=r.Allocate(32),fc=r.Allocate(1024),npcClass=r.Allocate(512),npc=r.Allocate(256),vector=r.Allocate(32),vclass=r.Allocate(1024),hyClass=r.Allocate(256),hy=r.Allocate(512),array=r.Allocate(128),list=r.Allocate(128);
 P(r,game+0x16ee888,fcClass);P(r,fcClass+0xb8,stat);P(r,stat+16,fc);P(r,fc,fcClass);I(r,fc+0x134,86);P(r,game+0x16ee910,npcClass);P(r,npc,npcClass);I(r,npc+80,92);I(r,npc+56,11);P(r,fc+0x158,vector);P(r,vector,vclass);
 int npcCalls=0,menuCalls=0;Tick tick=null;Count count=delegate{return 1;};Element element=delegate{return new IntPtr(npc);};Barrier barrier=delegate{};ActionFn interact=delegate(IntPtr p,IntPtr info){Check(p.ToInt64()==npc&&R(r,data)==2,"Tobirama identity / consume before interact");npcCalls++;tick();};ActionFn menu=delegate(IntPtr p,IntPtr info){Check(p.ToInt64()==hy&&R(r,data)==2,"menu identity / consume before selection");menuCalls++;tick();};
 P(r,vclass+0x1e8,Marshal.GetFunctionPointerForDelegate(count).ToInt64());P(r,vclass+0x258,Marshal.GetFunctionPointerForDelegate(element).ToInt64());
 byte[] code=NativeCode.CloakAction(data,game);Replace(code,game+0x12d9e0,Marshal.GetFunctionPointerForDelegate(barrier).ToInt64());Replace(code,game+0x4e19f0,Marshal.GetFunctionPointerForDelegate(interact).ToInt64());Replace(code,game+0x2d8f00,Marshal.GetFunctionPointerForDelegate(menu).ToInt64());long exe=r.Allocate(4096);r.Write(exe,code);r.Protect(exe,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(exe),typeof(Tick));
 I(r,data+12,1);NativeBackend.QueueCloakAction(r,data,1);tick();tick();Check(npcCalls==1&&R(r,data+56)==11&&R(r,data+36)==1,"NPC template92 and runtimeID11, once only");
 I(r,npc+80,102);NativeBackend.QueueCloakAction(r,data,1);tick();Check(npcCalls==1&&R(r,data+36)==0,"other NPC rejected");I(r,npc+80,92);
 P(r,game+0x16efe08,hyClass);P(r,hy,hyClass);P(r,fc+0x90,hy);P(r,hy+0xa8,array);P(r,hy+0xb8,list);I(r,array+24,3);P(r,array+32,S(r,":-chatTanto"));P(r,array+40,S(r,":-chatÁo choàng"));P(r,array+48,S(r,":-chatThú nuôi"));I(r,hy+0xdc,11);NativeBackend.QueueCloakAction(r,data,3);tick();tick();Check(menuCalls==1&&R(r,list+96)==1,"matches cloak by text even when row changed");
 I(r,hy+0xdc,12);NativeBackend.QueueCloakAction(r,data,3);tick();Check(menuCalls==1,"wrong NPC menu tag rejected");I(r,hy+0xdc,-4);P(r,array+40,S(r,":-chatTrường Konoha Gakuen"));NativeBackend.QueueCloakAction(r,data,2);tick();Check(menuCalls==2&&R(r,list+96)==1,"school menu by label and travel tag");
 I(r,fc+0x134,85);I(r,hy+0xdc,11);P(r,array+40,S(r,":-chatÁo choàng"));NativeBackend.QueueCloakAction(r,data,3);tick();Check(menuCalls==2,"cloak menu forbidden outside school");I(r,fc+0x134,86);
 r.Write(data,new byte[1024]);var j=Job();int map=85,amount=1;bool travel=true;DateTime now=DateTime.UtcNow;var offers=new[]{NativeBackend.DefaultCloakOffers()[3]};Action pump=delegate{NativeBackend.PumpCloak(r,data,0,j,()=>map,()=>Bag(amount,travel),()=>offers,now);now=now.AddSeconds(1);};
 pump();Check(j.CloakPhase==8&&R(r,data+512+20)==317&&R(r,data+512+8)==1,"direct buy outside school, no travel or NPC");Check(R(r,data)==0&&R(r,data+256)==0,"no UI actions queued");
 I(r,data+512,0);I(r,data+512+24,3);for(int n=0;n<3;n++)pump();Check(j.CloakBought==0&&R(r,data+512)==0,"no repeat while receipt pending");amount++;pump();Check(j.CloakBought==1&&j.CloakPhase==7,"nonstacking equipment quantity0 counted as one");now=now.AddSeconds(2);pump();Check(R(r,data+512)==1,"next purchase only after confirmation");I(r,data+512,0);I(r,data+512+24,3);amount++;pump();Check(!j.CloakEnabled&&j.CloakBought==2,"all requested cloaks confirmed");
 r.Write(data,new byte[1024]);j=Job();j.CloakPhase=7;j.CloakExpectedPrice=800;pump();Check(!j.CloakEnabled&&R(r,data+512)==0,"changed price blocked");
 map=85;j=Job();j.CloakPhase=7;pump();I(r,data+512,0);I(r,data+512+24,3);now=now.AddSeconds(21);pump();Check(!j.CloakEnabled&&j.CloakBought==0,"uncertain buy stopped without resending");
 r.Write(data,new byte[1024]);j=Job();map=85;travel=false;pump();Check(j.CloakPhase==8&&R(r,data+256)==0,"purchase needs no travel item");
 r.Write(data,new byte[1024]);j=Job();offers=new CloakOffer[0];pump();Check(j.CloakEnabled&&R(r,data+512)==0,"missing catalog never sends");now=now.AddSeconds(21);pump();Check(!j.CloakEnabled,"missing catalog bounded wait");offers=new[]{NativeBackend.DefaultCloakOffers()[3]};
 int buyCalls=0;Tick noOp=delegate{throw new Exception("UI/travel callback must never run");};Tick buyStub=delegate{if(R(r,data+512)==1&&R(r,data+512+12)==1)buyCalls++;I(r,data+512,0);};
 byte[] bundle=NativeCode.CloakBundle(data,game,Marshal.GetFunctionPointerForDelegate(noOp).ToInt64(),Marshal.GetFunctionPointerForDelegate(noOp).ToInt64(),Marshal.GetFunctionPointerForDelegate(buyStub).ToInt64());long bundleCode=r.Allocate(4096);r.Write(bundleCode,bundle);r.Protect(bundleCode,4096,0x20);Tick bundleTick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(bundleCode),typeof(Tick));
 P(r,fc+0xe8,vector);I(r,npc+16,317);I(r,npc+20,131);I(r,npc+80,900);P(r,data+512+32,npc);I(r,data+512+20,317);I(r,data+512+40,131);I(r,data+512+44,900);
 I(r,data+512,1);I(r,data+512+12,1);I(r,fc+0x134,85);bundleTick();Check(buyCalls==1,"direct packet allowed outside school with verified catalog");
 I(r,npc+80,901);I(r,data+512,1);I(r,data+512+12,1);bundleTick();Check(buyCalls==1&&R(r,data+512+12)==0,"main thread rejects price changed after worker read");I(r,npc+80,900);
 I(r,npc+64,1);I(r,data+512,1);I(r,data+512+12,1);bundleTick();Check(buyCalls==1,"other currency blocked");I(r,npc+64,0);
 P(r,data+512+32,npc+8);I(r,data+512,1);I(r,data+512+12,1);bundleTick();Check(buyCalls==1,"stale catalog identity blocked");

 j.CloakEnabled=true;j.GameAutoWanted=true;Check(!NativeBackend.GameAutoDesired(j),"combat paused during cloak purchase");j.CloakEnabled=false;Check(NativeBackend.GameAutoDesired(j),"combat preference restored after purchase");
 GC.KeepAlive(noOp);GC.KeepAlive(buyStub);GC.KeepAlive(count);GC.KeepAlive(element);GC.KeepAlive(barrier);GC.KeepAlive(interact);GC.KeepAlive(menu);Console.WriteLine("PASS: headless purchase outside school, no travel/menu callbacks, exact SKU/price/currency/catalog identity, receipt before repeat, missing catalog timeout, uncertain receipt stop, combat pause; "+code.Length+" action bytes.");
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
