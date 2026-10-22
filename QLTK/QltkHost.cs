using System;using System.IO;using System.Threading;using System.Windows.Forms;using System.Management.Automation;using System.Management.Automation.Runspaces;
class QltkHost {
 [STAThread] static int Main(string[] args){
  string root=AppDomain.CurrentDomain.BaseDirectory;string script=Path.Combine(root,"QLTK","AccountManager.ps1");if(!File.Exists(script))script=Path.Combine(root,"AccountManager.ps1");
  if(!File.Exists(script)){MessageBox.Show("Giải nén đầy đủ QLTK trước khi mở.","QLTK");return 1;}
  try{using(Runspace runspace=RunspaceFactory.CreateRunspace()){
   runspace.ApartmentState=ApartmentState.STA;runspace.ThreadOptions=PSThreadOptions.UseCurrentThread;runspace.Open();
   using(PowerShell ps=PowerShell.Create()){
    ps.Runspace=runspace;ps.AddCommand("Set-ExecutionPolicy").AddParameter("Scope","Process").AddParameter("ExecutionPolicy","Bypass").AddParameter("Force");ps.Invoke();ps.Commands.Clear();ps.Streams.Error.Clear();
    ps.AddCommand(script);if(args.Length==1&&args[0]=="--ui-test")ps.AddParameter("UiTest");ps.Invoke();
    if(ps.HadErrors){foreach(ErrorRecord error in ps.Streams.Error)MessageBox.Show(error.Exception.Message,"QLTK - Lỗi");return 1;}
   }
  }return 0;}catch(Exception e){MessageBox.Show(e.Message,"QLTK - Lỗi");return 1;}
 }
}
