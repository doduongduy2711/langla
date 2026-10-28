using System;using System.IO;using System.Threading;using System.Windows.Forms;using System.Management.Automation;using System.Management.Automation.Runspaces;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]
class QltkHost {
 [STAThread] static int Main(string[] args){
  if(args.Length==1&&args[0]=="--runtime-check")return Environment.Is64BitProcess&&Environment.Version.Major==4?0:2;
  bool uiTest=args.Length==1&&args[0]=="--ui-test";
  string modules=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","Modules");
  Environment.SetEnvironmentVariable("PSModulePath",modules+";"+(Environment.GetEnvironmentVariable("PSModulePath")??""));
  string root=AppDomain.CurrentDomain.BaseDirectory;string script=Path.Combine(root,"QLTK","AccountManager.ps1");if(!File.Exists(script))script=Path.Combine(root,"AccountManager.ps1");
  if(!File.Exists(script)){MessageBox.Show("Giải nén đầy đủ QLTK trước khi mở.","QLTK");return 1;}
  try{using(Runspace runspace=RunspaceFactory.CreateRunspace()){
   runspace.ApartmentState=ApartmentState.STA;runspace.ThreadOptions=PSThreadOptions.UseCurrentThread;runspace.Open();
   using(PowerShell ps=PowerShell.Create()){
    ps.Runspace=runspace;ps.AddCommand("Set-ExecutionPolicy").AddParameter("Scope","Process").AddParameter("ExecutionPolicy","Bypass").AddParameter("Force");ps.Invoke();ps.Commands.Clear();ps.Streams.Error.Clear();
    ps.AddCommand(script);if(uiTest)ps.AddParameter("UiTest");var output=ps.Invoke();if(uiTest)foreach(var item in output)Console.WriteLine(item);
    if(ps.HadErrors){foreach(ErrorRecord error in ps.Streams.Error){if(uiTest)Console.Error.WriteLine(error.Exception.Message);else MessageBox.Show(error.Exception.Message,"QLTK - Lỗi");}return 1;}
   }
  }return 0;}catch(Exception e){if(uiTest)Console.Error.WriteLine(e.Message);else MessageBox.Show(e.Message,"QLTK - Lỗi");return 1;}
 }
}
