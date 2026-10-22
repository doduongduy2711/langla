using System;using TGLL;
class InboxReuseTests{
 static void C(bool ok,string message){if(!ok)throw new Exception(message);}
 static int Main(){try{
  C(NativeBackend.InboxDeletionConfirmed(new[]{1},new InboxLetter[0]),"empty inbox must confirm deletion");
  var old=new InboxLetter{Id=1,Read=true,HasAttachment=false};C(!NativeBackend.InboxDeletionConfirmed(new[]{1},new[]{old}),"old empty read letter still present must block");
  var incoming=new InboxLetter{Id=1,Read=false,HasAttachment=true,TemplateId=568,Quantity=1};C(NativeBackend.InboxDeletionConfirmed(new[]{1},new[]{incoming}),"new attached letter reusing ID must not block old deletion");C(!NativeBackend.InboxCanDelete(incoming),"new attached letter must never be deleted");
  C(!NativeBackend.InboxDeletionConfirmed(new[]{1,2},new[]{incoming,new InboxLetter{Id=2,Read=true}}),"partially deleted batch falsely confirmed");
  C(NativeBackend.InboxDeletionConfirmed(new[]{1},new[]{incoming,new InboxLetter{Id=2,Read=true}}),"unrelated old letter blocked target deletion");
  Console.WriteLine("PASS: old letter removal, reused ID with attached new mail preserved, actual undeleted empty mail holds and partial-delete batch verification.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
