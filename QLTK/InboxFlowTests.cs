using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TGLL;
class InboxFlowTests {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Alloc(IntPtr klass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Ctor(IntPtr message,int opcode,IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Number(IntPtr message,int value,IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Send(IntPtr message,IntPtr info);
    static void C(bool b,string text){if(!b)throw new Exception(text);}
    static void L(Remote r,long p,long n){r.Write(p,BitConverter.GetBytes(n));}
    static void I(Remote r,long p,int n){r.Write(p,BitConverter.GetBytes(n));}
    static int R(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
    static void Stub(Remote r,long p,Delegate d){byte[] b=new byte[14];b[0]=255;b[1]=37;Array.Copy(BitConverter.GetBytes(Marshal.GetFunctionPointerForDelegate(d).ToInt64()),0,b,6,8);r.Write(p,b);}
    static void Main(){
        DateTime now=DateTime.UtcNow;
        var receiver=new NativeJob("synthetic.exe","receiver","unused",null,true){CharacterName="Receiver",State="opened",GameSettingsReady=true,InboxSupported=true,InboxEnabled=true,InboxObservedAt=now,InboxPendingAttachments=0};
        receiver.InboxLoaded=new InboxLetter[0];
        var sender=new NativeJob("synthetic.exe","sender","unused",null,true){MailRecipient="Receiver"};sender.MailReceiverJobId=receiver.Id;
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)=="","empty receiver should be ready");
        sender.MailReceiverAwaiting=true;sender.MailReceiverReceivedBefore=10;sender.MailReceiverDeletedBefore=10;
        receiver.InboxReceived=11;receiver.InboxDeleted=10;
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!=""&&sender.MailReceiverAwaiting,"receive without delete released next letter");
        receiver.InboxDeleted=11;receiver.InboxLoaded=new[]{new InboxLetter{Id=1,Read=true}};
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","nonempty mailbox released next letter");
        receiver.InboxLoaded=new InboxLetter[0];receiver.InboxBusy=true;
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","busy receiver released next letter");
        receiver.InboxBusy=false;
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)==""&&!sender.MailReceiverAwaiting,"verified receive/delete did not release next letter");
        receiver.InboxEnabled=false;C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","disabled inbox did not pause sender");receiver.InboxEnabled=true;
        receiver.InboxObservedAt=now.AddSeconds(-6);C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","stale empty inbox accepted");receiver.InboxObservedAt=now;
        C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,false)!="","offline receiver accepted");
        receiver.CloseRequested=true;C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","closed receiver accepted");receiver.CloseRequested=false;
        receiver.ReconnectRequested=true;C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","reconnecting receiver accepted");receiver.ReconnectRequested=false;
        receiver.CharacterName="Other";C(NativeBackend.MailReceiverBlockReason(sender,receiver,now,true)!="","wrong character accepted");receiver.CharacterName="Receiver";
        C(NativeBackend.MailReceiverBlockReason(sender,null,now,true)!="","missing receiver accepted");
        var external=new NativeJob("synthetic.exe","external","unused",null,true);C(NativeBackend.MailReceiverBlockReason(external,null,now,false)=="","explicit external recipient behavior changed");
        receiver.InboxBusy=true;receiver.InboxPhase=4;NativeBackend.ResyncInboxDelete(receiver,now);
        C(receiver.InboxEnabled&&!receiver.InboxBusy&&receiver.InboxPhase==0&&receiver.InboxLoaded==null&&receiver.InboxNeedsRefresh&&receiver.InboxPendingAttachments==-1,"delete timeout disabled inbox instead of refreshing");
        receiver.InboxPhase=5;receiver.InboxBatch=new[]{new InboxLetter{Id=3,HasAttachment=false}};NativeBackend.ResyncInboxDelete(receiver,now);
        C(receiver.InboxPhase==3&&receiver.InboxBatch.Length==1&&receiver.InboxEnabled,"read-ack retry lost the received batch or disabled inbox");
        using(var r=new Remote(Process.GetCurrentProcess())){
            long game=r.Allocate(0x1700000),data=r.Allocate(4096),code=r.Allocate(4096);
            long fcClass=r.Allocate(256),statics=r.Allocate(32),fc=r.Allocate(512),vec=r.Allocate(32),list=r.Allocate(32),array=r.Allocate(64);
            long mailClass=r.Allocate(256),letter=r.Allocate(96),other=r.Allocate(96),msgClass=r.Allocate(256),packet=r.Allocate(48),vtable=r.Allocate(1024);
            L(r,game+0x16ee888,fcClass);L(r,fcClass+0xb8,statics);L(r,statics+16,fc);L(r,fc,fcClass);L(r,fc+0xf0,vec);L(r,vec+16,list);L(r,list+16,array);I(r,list+24,1);L(r,array+24,4);L(r,array+32,letter);
            L(r,game+0x16ef960,mailClass);L(r,letter,mailClass);I(r,letter+16,7);r.Write(letter+80,new byte[]{1});L(r,other,mailClass);I(r,other+16,7);r.Write(other+80,new byte[]{1});
            L(r,game+0x16ee820,msgClass);L(r,packet,vtable);
            int opcode=0,sends=0;var numbers=new List<int>();Tick tick=null;
            Alloc allocate=delegate(IntPtr klass){C(klass.ToInt64()==msgClass,"wrong message class");return new IntPtr(packet);};
            Ctor ctor=delegate(IntPtr p,int op,IntPtr info){opcode=op;numbers.Clear();};
            Number number=delegate(IntPtr p,int value,IntPtr info){numbers.Add(value);};
            Send send=delegate(IntPtr p,IntPtr info){sends++;tick();};
            Stub(r,game+0x16f760,allocate);Stub(r,game+0x2e2aa0,ctor);L(r,vtable+0x248,Marshal.GetFunctionPointerForDelegate(number).ToInt64());L(r,vtable+0x298,Marshal.GetFunctionPointerForDelegate(send).ToInt64());r.Protect(game,0x1700000,0x20);
            byte[] bytes=NativeCode.Inbox(data,game);C(bytes.Length<4096,"inbox hook too large");r.Write(code,bytes);r.Protect(code,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(Tick));
            Action<long> queue=delegate(long p){I(r,data+4,3);I(r,data+12,1);I(r,data+16,7);L(r,data+32,p);I(r,data,1);tick();};
            queue(letter);C(sends==1&&opcode==0x58&&numbers.Count==2&&numbers[0]==1&&numbers[1]==7,"empty read mail was not deleted by exact ID");tick();C(sends==1,"delete repeated on reentry");
            L(r,letter+48,other);queue(letter);C(sends==1,"attached item deleted");L(r,letter+48,0);
            foreach(int off in new[]{56,60,64,68,72}){I(r,letter+off,1);queue(letter);C(sends==1,"money attachment deleted");I(r,letter+off,0);}
            r.Write(letter+80,new byte[]{0});queue(letter);C(sends==1,"unread mail deleted");r.Write(letter+80,new byte[]{1});
            I(r,letter+16,8);queue(letter);C(sends==1,"changed mail ID deleted");I(r,letter+16,7);
            L(r,array+32,other);queue(letter);C(sends==1,"stale letter pointer absent from current inbox deleted reused ID");
            I(r,list+24,4097);queue(letter);C(sends==1,"invalid inbox vector accepted");
            GC.KeepAlive(allocate);GC.KeepAlive(ctor);GC.KeepAlive(number);GC.KeepAlive(send);GC.KeepAlive(tick);
        }
        Console.WriteLine("PASS: sender pauses for receiver failure/stale/busy/nonempty inbox and waits for receive plus delete; delete resync stays enabled; native exact-ID delete excludes attachments, money, unread mail, changed/reused IDs and stale pointers. No real mail action.");
    }
}
