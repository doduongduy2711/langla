using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Text;using TGLL;
class CaptchaTests {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Tick();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Click(IntPtr button,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate byte Contains(IntPtr self,IntPtr value,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr NewString(IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Input(IntPtr self,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void SetText(IntPtr self,IntPtr text,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Confirm(IntPtr self,int action,IntPtr popup,IntPtr button,IntPtr info);
 static void C(bool value,string message){if(!value)throw new Exception(message);}
 static void L(Remote r,long p,long value){r.Write(p,BitConverter.GetBytes(value));}
 static void I(Remote r,long p,int value){r.Write(p,BitConverter.GetBytes(value));}
 static int R(Remote r,long p){return BitConverter.ToInt32(r.Read(p,4),0);}
 static long S(Remote r,string text){long p=r.Allocate(24+text.Length*2);I(r,p+16,text.Length);r.Write(p+20,Encoding.Unicode.GetBytes(text));return p;}
 static void Stub(Remote r,long p,Delegate d){byte[] b=new byte[14];b[0]=255;b[1]=37;Array.Copy(BitConverter.GetBytes(Marshal.GetFunctionPointerForDelegate(d).ToInt64()),0,b,6,8);r.Write(p,b);}
 static void Main(){
  C(NativeBackend.IsCurseNotice("Vui lòng giải Capcha có ở dưới game lên quái vật, nếu không thua Capcha thì thoát game vào lại"),"image prompt not detected");
  C(NativeBackend.IsCurseNotice("Mã xác nhận: Mã xác nhận:"),"live captcha title not detected");
  C(!NativeBackend.IsCurseNotice("Gửi thư thành công"),"ordinary popup detected");
  C(NativeBackend.CaptchaCodeValid("942B")&&!NativeBackend.CaptchaCodeValid("94-B")&&!NativeBackend.CaptchaCodeValid("94B")&&!NativeBackend.CaptchaCodeValid("94 B"),"invalid OCR code accepted");
  string bitmap=System.IO.Path.GetTempFileName();try{
   var colors=new CaptchaFrame{Width=2,Height=2,PixelOffset=40,Raw=new byte[]{1,2,3,255,4,5,6,255,7,8,9,255,10,11,12,255}};
   NativeBackend.SaveCaptchaBitmap(colors,bitmap);byte[] bmp=System.IO.File.ReadAllBytes(bitmap);C(bmp[54]==3&&bmp[55]==2&&bmp[56]==1&&bmp[62]==9,"Unity bottom-up RGBA bitmap flipped or wrong colors");
   string samples=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"qltk-captcha-tests-"+Guid.NewGuid().ToString("N"));try{bool created;string first=NativeBackend.SaveCaptchaSample(colors,samples,out created);C(created,"new sample not archived");string second=NativeBackend.SaveCaptchaSample(colors,samples,out created);C(!created&&first==second&&System.IO.Directory.GetFiles(samples).Length==1,"duplicate sample stored");string annotation=NativeBackend.SaveCaptchaLabelFile(samples,first," a2b4 ");C(System.IO.File.ReadAllText(annotation).Trim()=="A2B4","manual label not normalized");NativeBackend.SaveCaptchaLabelFile(samples,first,"c3d5");C(System.IO.File.ReadAllText(annotation).Trim()=="C3D5","label correction not saved atomically");bool rejected=false;try{NativeBackend.SaveCaptchaLabelFile(samples,first,"AB!");}catch(ArgumentException){rejected=true;}C(rejected&&System.IO.File.ReadAllText(annotation).Trim()=="C3D5","invalid label changed stored result");rejected=false;try{NativeBackend.SaveCaptchaLabelFile(samples,bitmap,"AB12");}catch(ArgumentException){rejected=true;}C(rejected,"annotation wrote outside sample folder");System.IO.File.Delete(annotation);colors.Raw[0]++;NativeBackend.SaveCaptchaSample(colors,samples,out created);C(created&&System.IO.Directory.GetFiles(samples).Length==2,"new image not stored");colors.Raw[0]--;}finally{if(System.IO.Directory.Exists(samples))System.IO.Directory.Delete(samples,true);}
   colors.PixelOffset=32;NativeBackend.SaveCaptchaBitmap(colors,bitmap);bmp=System.IO.File.ReadAllBytes(bitmap);C(bmp[54]==8&&bmp[55]==9&&bmp[56]==255,"packed top-down bitmap flipped or wrong colors");
  }finally{System.IO.File.Delete(bitmap);}
  using(var r=new Remote(Process.GetCurrentProcess())){
   long game=r.Allocate(0x1700000),data=r.Allocate(4096),code=r.Allocate(4096),fcClass=r.Allocate(2048),stat=r.Allocate(32),fc=r.Allocate(1024),dialogClass=r.Allocate(2048),popup=r.Allocate(512),vector=r.Allocate(32),vectorClass=r.Allocate(1024);
   long image=r.Allocate(128),pixmap=r.Allocate(96),pixels=r.Allocate(64),button=r.Allocate(160),input=r.Allocate(64),inputClass=r.Allocate(1024);
   L(r,game+0x16ee888,fcClass);L(r,fcClass+0xb8,stat);L(r,stat+16,fc);L(r,fc,fcClass);L(r,game+0x16eeba8,dialogClass);L(r,popup,dialogClass);L(r,fc+0x90,popup);L(r,fc+0x88,vector);L(r,vector,vectorClass);
   L(r,popup+0xc8,image);L(r,image+24,pixmap);I(r,pixmap+16,2);I(r,pixmap+20,1);L(r,pixmap+32,pixels);L(r,pixels+24,2);byte[] raw={255,10,20,30,255,40,50,60};r.Write(pixels+32,raw);
   L(r,popup+0xe8,button);I(r,button+0x80,190);L(r,button+0x48,popup);L(r,popup+0x50,vector);long label=S(r,"Xác nhận");L(r,button+0x78,label);L(r,input,inputClass);
   int confirms=0,sets=0;bool member=true;Tick tick=null;string entered="";
   Contains contains=delegate(IntPtr self,IntPtr value,IntPtr info){return (byte)(member?1:0);};
   NewString text=delegate(IntPtr p){return new IntPtr(S(r,Marshal.PtrToStringAnsi(p)));};
   Input get=delegate(IntPtr self,IntPtr info){C(self.ToInt64()==popup,"wrong popup input getter");return new IntPtr(input);};
   SetText set=delegate(IntPtr self,IntPtr value,IntPtr info){C(self.ToInt64()==input,"wrong input setter");sets++;entered=Encoding.Unicode.GetString(r.Read(value.ToInt64()+20,R(r,value.ToInt64()+16)*2));};
   Click click=delegate(IntPtr self,IntPtr info){throw new Exception("focus-dependent button wrapper called");};
   Confirm submit=delegate(IntPtr self,int action,IntPtr context,IntPtr sender,IntPtr info){C(self.ToInt64()==popup&&action==190&&context==IntPtr.Zero&&sender.ToInt64()==button,"wrong popup confirmation event");confirms++;tick();};
   Confirm confirm=delegate(IntPtr self,int action,IntPtr p,IntPtr b,IntPtr info){C(self.ToInt64()==fc&&action==-7&&p.ToInt64()==popup&&b.ToInt64()==button,"wrong game confirmation dispatch");confirms++;tick();};
   L(r,vectorClass+0x208,Marshal.GetFunctionPointerForDelegate(contains).ToInt64());L(r,dialogClass+0x488,Marshal.GetFunctionPointerForDelegate(submit).ToInt64());L(r,dialogClass+0x598,Marshal.GetFunctionPointerForDelegate(get).ToInt64());L(r,inputClass+0x3c8,Marshal.GetFunctionPointerForDelegate(set).ToInt64());L(r,fcClass+0x488,Marshal.GetFunctionPointerForDelegate(confirm).ToInt64());Stub(r,game+0x161150,text);Stub(r,game+0x3d7d50,click);r.Protect(game,0x1700000,0x20);
   byte[] bytes=NativeCode.Captcha(data,game);C(bytes.Length<4096,"captcha hook too large");r.Write(code,bytes);r.Protect(code,4096,0x20);tick=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(code),typeof(Tick));
   Action queue=delegate{I(r,data+80,32);I(r,data+8,1);L(r,data+16,popup);L(r,data+24,image);L(r,data+32,pixmap);L(r,data+40,pixels);I(r,data+48,2);r.Write(data+52,BitConverter.GetBytes(NativeBackend.CaptchaHash(raw)));L(r,data+56,button);r.Write(data+64,Encoding.ASCII.GetBytes("942B\0"));I(r,data+72,2);I(r,data+76,1);I(r,data,1);};
   queue();tick();C(confirms==1&&sets==1&&entered=="942B"&&R(r,data+4)==2,"code not filled/confirmed exactly once");tick();C(confirms==1,"confirmation repeated on next frame");
   queue();r.Write(pixels+32,new byte[]{1});tick();C(confirms==1&&sets==1,"changed image was submitted");r.Write(pixels+32,raw);
   queue();r.Write(data+65,new byte[]{59});tick();C(confirms==1&&sets==1,"malformed code submitted");
   queue();r.Write(button+0x51,new byte[]{1});tick();C(confirms==1,"disabled button event submitted");r.Write(button+0x51,new byte[]{0});
   queue();member=false;tick();C(confirms==1,"dismissed popup submitted");member=true;
   queue();I(r,data+8,0);tick();C(confirms==1,"disabled request submitted");
   queue();L(r,button+0x78,S(r,"Khác"));tick();C(confirms==1,"wrong button label submitted");L(r,button+0x78,label);
   queue();I(r,pixmap+16,1);I(r,pixmap+20,2);tick();C(confirms==1,"changed dimensions submitted");
   long stored=r.Allocate(16032),mailList=r.Allocate(32),items=r.Allocate(40);L(r,vector+16,mailList);L(r,mailList+16,items);I(r,mailList+24,1);L(r,items+24,1);L(r,items+32,popup);L(r,popup+0xd0,input);L(r,popup+0xb0,S(r,"Vui lòng giải Capcha có ở dưới game"));I(r,pixmap+16,100);I(r,pixmap+20,40);L(r,pixmap+32,stored);L(r,stored+24,4000);r.Write(stored+32,new byte[16000]);
   long childVector=r.Allocate(32),childList=r.Allocate(32),childItems=r.Allocate(40);L(r,childVector,vectorClass);L(r,childVector+16,childList);L(r,childList+16,childItems);I(r,childList+24,1);L(r,childItems+24,1);L(r,childItems+32,button);L(r,popup+0x50,childVector);
   var snapshot=NativeBackend.ReadCaptchaFrame(r,game);C(snapshot!=null&&snapshot.Width==100&&snapshot.Height==40&&snapshot.Raw.Length==16000,"native captcha pixels not extracted");
   long subclass=r.Allocate(2048);L(r,subclass+0x58,dialogClass);L(r,subclass+0x488,Marshal.GetFunctionPointerForDelegate(submit).ToInt64());L(r,subclass+0x598,Marshal.GetFunctionPointerForDelegate(get).ToInt64());L(r,popup,subclass);L(r,popup+0xd0,0);L(r,popup+0xb0,S(r,"Mã xác nhận:"));L(r,pixmap+32,0);L(r,pixmap+40,stored);L(r,stored+24,16000);
   snapshot=NativeBackend.ReadCaptchaFrame(r,game);C(snapshot!=null&&snapshot.PixelOffset==40,"live subclass byte RGBA captcha not extracted");
   var manualJob=new NativeJob(System.Reflection.Assembly.GetExecutingAssembly().Location,"fixture","",null,true){CaptchaFrame=snapshot,CaptchaData=data,CurseActive=true,CurseReadAt=DateTime.UtcNow,State="opened"};var review=NativeBackend.GetCaptchaReview(manualJob);C(review!=null&&review.CanSubmit,"manual review unavailable");C(!NativeBackend.QueueManualCaptcha(manualJob,"stale-image","ab12"),"stale image accepted");C(NativeBackend.QueueManualCaptcha(manualJob,review.Key,"ab12")&&manualJob.CaptchaCode=="AB12"&&manualJob.CaptchaManual,"manual request lost or not uppercase");C(!NativeBackend.QueueManualCaptcha(manualJob,review.Key,"AB12"),"duplicate manual request accepted");
   NativeBackend.ConfigureCaptcha(false,"missing-python","missing-script",System.IO.Path.Combine(System.IO.Path.GetTempPath(),"qltk-manual-tests","jobs"));I(r,data,0);typeof(NativeBackend).GetMethod("PumpCaptcha",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{r,game,manualJob});C(R(r,data)==1&&R(r,data+8)==1&&Encoding.ASCII.GetString(r.Read(data+64,4))=="AB12","manual request required auto OCR or runtime");I(r,data,0);
   manualJob.CaptchaManual=false;manualJob.CaptchaAttemptKey="";manualJob.CurseReadAt=DateTime.UtcNow.AddSeconds(-10);C(!NativeBackend.QueueManualCaptcha(manualJob,review.Key,"AB12"),"expired live image accepted");manualJob.CurseReadAt=DateTime.UtcNow;manualJob.CloseRequested=true;C(!NativeBackend.QueueManualCaptcha(manualJob,review.Key,"AB12"),"closing game accepted manual request");
   I(r,pixmap+16,2);I(r,pixmap+20,1);r.Write(stored+32,raw);queue();I(r,data+80,40);L(r,data+40,stored);tick();C(confirms==2&&sets==2&&R(r,data+4)==2,"subclass RGBA confirm button was not dispatched");
   queue();I(r,data+80,40);L(r,data+40,stored);I(r,stored+24,2);tick();C(confirms==2,"short RGBA byte buffer submitted");I(r,stored+24,16000);
   queue();I(r,data+80,40);L(r,data+40,stored);I(r,button+0x80,-7);tick();C(confirms==2,"close button used as confirmation");I(r,button+0x80,190);
   L(r,popup,dialogClass);I(r,pixmap+16,100);I(r,pixmap+20,40);L(r,pixmap+32,stored);L(r,popup+0xd0,input);
   L(r,popup+0xb0,S(r,"Thông báo khác"));C(NativeBackend.ReadCaptchaFrame(r,game)==null,"ordinary image popup treated as captcha");
   I(r,button+0x80,-7);long canvasClass=r.Allocate(256),canvasStatic=r.Allocate(32),canvas=r.Allocate(512),popupData=r.Allocate(4096),popupCode=r.Allocate(4096);L(r,game+0x16ed658,canvasClass);L(r,canvasClass+0xb8,canvasStatic);L(r,canvasStatic,canvas);L(r,canvas+0x1b8,fc);
   r.Write(popupCode,NativeCode.PrepareMap(popupData,game+0x16ed658,game+0x16ee888,game+0x16eeba8,game+0x1dd900,0));r.Protect(popupCode,4096,0x20);var generic=(Tick)Marshal.GetDelegateForFunctionPointer(new IntPtr(popupCode),typeof(Tick));I(r,popupData,15);generic();C(confirms==2&&R(r,popupData)==15,"generic startup confirmation sent empty captcha");
   L(r,popup+0xc8,0);generic();C(confirms==3&&R(r,popupData)==17,"ordinary confirmation stopped working");GC.KeepAlive(generic);
   GC.KeepAlive(submit);GC.KeepAlive(click);GC.KeepAlive(contains);GC.KeepAlive(text);GC.KeepAlive(get);GC.KeepAlive(set);GC.KeepAlive(confirm);GC.KeepAlive(tick);
  }
  Console.WriteLine("PASS: captcha prompt, strict code, native input/action-190 popup callback without hover/focus, disabled-button guard, subclass and RGBA bytes, one-shot/reentry, changed image/dimensions, dismissed popup, disabled request and wrong button guards. Manual annotation persistence, case normalization, stale/duplicate/closed-session guards and submission with OCR disabled passed. No real game input.");
 }
}
