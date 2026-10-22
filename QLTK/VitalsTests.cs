using System;using TGLL;
class VitalsTests {
 static void C(bool b,string m){if(!b)throw new Exception(m);}static byte[] B(params int[] values){byte[] b=new byte[16];for(int i=0;i<4;i++)Array.Copy(BitConverter.GetBytes(values[i]),0,b,4*i,4);return b;}
 static void Main(){var j=new NativeJob("synthetic.exe","fixture","unused",null,true);j.Vitals=NativeBackend.ParseVitals(B(5000,2900,6500,3500),DateTime.UtcNow);C(NativeBackend.VitalsDisplay(j)=="HP: 5000 / 6500\r\nMP: 2900 / 3500","HP/MP order or display wrong");
  j.Vitals=NativeBackend.ParseVitals(B(0,0,6500,3500),DateTime.UtcNow);C(NativeBackend.VitalsDisplay(j).Contains("HP: 0 / 6500"),"zero vitals hidden");C(NativeBackend.ParseVitals(B(-1,2,3,4),DateTime.UtcNow)==null&&NativeBackend.ParseVitals(new byte[2],DateTime.UtcNow)==null,"invalid snapshot accepted");
  j.Vitals=NativeBackend.ParseVitals(B(1,2,3,4),DateTime.UtcNow.AddSeconds(-4));C(NativeBackend.VitalsDisplay(j).Contains("—"),"stale values shown");j.Vitals=NativeBackend.ParseVitals(B(1,2,3,4),DateTime.UtcNow);j.CloseRequested=true;C(NativeBackend.VitalsDisplay(j).Contains("—"),"closed values shown");Console.WriteLine("PASS: HP/MP snapshot order, current/max display, zero, invalid/stale/closed state; no gameplay changed.");
 }
}
