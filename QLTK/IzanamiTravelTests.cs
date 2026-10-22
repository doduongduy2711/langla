using System;using System.Diagnostics;using System.Reflection;using TGLL;
class IzanamiTravelTests {
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static void Int(Remote r,long p,int value){r.Write(p,BitConverter.GetBytes(value));}
    static int Read(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
    static NativeJob Job(Remote r){return new NativeJob("fixture","fixture",null,null,true){ReceiveKeyEnabled=false,IzanamiEnabled=true,TravelData=r.Allocate(4096),TrainReady=true,GameSettingsReady=true};}
    static void Arrive(Remote r,long data,NativeJob j,Stopwatch timer){
        Int(r,j.TravelData,0);Int(r,j.TravelData+8,2);NativeBackend.PumpTravel(r,data,j,timer,0);
        Check(Read(r,j.TravelData+64)==2,"Village menu gate");
        Int(r,j.TravelData+64,3);NativeBackend.PumpTravel(r,data,j,timer,0);
        Int(r,j.TravelData+64,4);NativeBackend.PumpTravel(r,data,j,timer,0);
        Int(r,data,16);NativeBackend.PumpTravel(r,data,j,timer,0,()=>false);
        Check(!j.IzanamiTravelReady&&j.TravelActive,"Entered before confirmed arrival");
        NativeBackend.PumpTravel(r,data,j,timer,0,()=>true);
        Check(j.IzanamiTravelReady&&!j.TravelActive&&!j.ReceiveFlowActive&&Read(r,data)==12&&!timer.IsRunning,"Arrival did not hand control back to team");
    }
    static int Main(string[] args){
        NativeBackend.LogDirectory=args[0];
        using(var r=new Remote(Process.GetCurrentProcess())){
            long data=r.Allocate(4096);var timer=new Stopwatch();var leader=Job(r);
            NativeBackend.PumpIzanamiTravel(r,data,leader,timer);
            Check(leader.TravelActive&&leader.NpcAction==2&&Read(r,leader.TravelData+4)==168,"Izanami did not use item");
            Int(r,leader.TravelData,0);NativeBackend.PumpIzanamiTravel(r,data,leader,timer);
            Check(Read(r,leader.TravelData)==0,"Repeated item use while in flight");
            Arrive(r,data,leader,timer);NativeBackend.PumpIzanamiTravel(r,data,leader,timer);
            Check(!leader.TravelActive,"Repeated item after arrival");
            var member=Job(r);member.TeamManaged=true;member.TeamJobIds=new[]{leader.Id,member.Id};
            NativeBackend.PumpIzanamiTravel(r,data,member,timer);Arrive(r,data,member,timer);
            Check(!member.IzanamiRequested,"Member attempted Onoki entry");
            var missing=Job(r);NativeBackend.PumpIzanamiTravel(r,data,missing,timer);
            Int(r,missing.TravelData,0);Int(r,missing.TravelData+8,3);NativeBackend.PumpTravel(r,data,missing,timer,0);
            Check(missing.IzanamiTravelStopped&&missing.Error=="travel_item_missing"&&missing.State=="izanami_unavailable","Missing item not stopped");
            NativeBackend.PumpIzanamiTravel(r,data,missing,timer);Check(!missing.TravelActive,"Missing item retried continuously");
            var disabled=Job(r);NativeBackend.PumpIzanamiTravel(r,data,disabled,timer);disabled.IzanamiEnabled=false;
            NativeBackend.PumpTravel(r,data,disabled,timer,0);Check(!disabled.TravelActive&&!disabled.ReceiveFlowActive,"Untick did not cancel travel");
            var combat=Job(r);combat.IzanamiRoundEntered=true;NativeBackend.PumpIzanamiTravel(r,data,combat,timer);
            Check(!combat.TravelActive,"Teleported out during combat");
            var busy=Job(r);busy.ReceiveKeyRequested=true;NativeBackend.PumpIzanamiTravel(r,data,busy,timer);
            Check(!busy.TravelActive,"Interrupted key request");
        }
        Console.WriteLine("PASS: leader/member travel, item once, menu/arrival gate, only leader entry, missing item stop, untick, combat and pending key.");return 0;
    }
}
