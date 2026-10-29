using System;using System.IO;using System.Threading;using System.Windows.Forms;using System.Management.Automation;using System.Management.Automation.Runspaces;using System.Linq;using System.Security.Cryptography;using System.Text;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]
class QltkHost {
 [STAThread] static int Main(string[] args){
  if(args.Length==1&&args[0]=="--runtime-check")return Environment.Is64BitProcess&&Environment.Version.Major==4?0:2;
  bool uiTest=args.Length==1&&args[0]=="--ui-test";
  bool backend=args.Contains("--backend-only");string dataDirectory=null;int frontendPort=0;
  for(int n=0;n<args.Length-1;n++){if(args[n]=="--data-dir")dataDirectory=Path.GetFullPath(args[n+1]);if(args[n]=="--socket-port")Int32.TryParse(args[n+1],out frontendPort);}

  string modules=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","Modules");
  Environment.SetEnvironmentVariable("PSModulePath",modules+";"+(Environment.GetEnvironmentVariable("PSModulePath")??""));
  string root=AppDomain.CurrentDomain.BaseDirectory;string script=Path.Combine(root,"QLTK","AccountManager.ps1");if(!File.Exists(script))script=Path.Combine(root,"AccountManager.ps1");
  if(!File.Exists(script)){MessageBox.Show("Giải nén đầy đủ QLTK trước khi mở.","QLTK");return 1;}
  if(dataDirectory==null)dataDirectory=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(script)),"Data");
  Mutex controller=null;bool ownsController=false;
  try{if(!uiTest){string normalized=Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-","");controller=new Mutex(false,"Local\\TGLL.Manager."+key);try{ownsController=controller.WaitOne(0);}catch(AbandonedMutexException){ownsController=true;}if(!ownsController){if(!backend)MessageBox.Show("Bộ điều phối của thư mục Data này đã chạy. Mở cửa sổ C# để kết nối phiên hiện tại.","QLTK");return 3;}}
   using(Runspace runspace=RunspaceFactory.CreateRunspace()){
   runspace.ApartmentState=ApartmentState.STA;runspace.ThreadOptions=PSThreadOptions.UseCurrentThread;runspace.Open();
   using(PowerShell ps=PowerShell.Create()){
    ps.Runspace=runspace;ps.AddCommand("Set-ExecutionPolicy").AddParameter("Scope","Process").AddParameter("ExecutionPolicy","Bypass").AddParameter("Force");ps.Invoke();ps.Commands.Clear();ps.Streams.Error.Clear();
    ps.AddCommand(script);if(uiTest)ps.AddParameter("UiTest");if(backend)ps.AddParameter("BackendOnly");ps.AddParameter("DataDirectory",dataDirectory);if(frontendPort>0)ps.AddParameter("FrontendPort",frontendPort);var output=ps.Invoke();if(uiTest)foreach(var item in output)Console.WriteLine(item);
    if(ps.HadErrors){foreach(ErrorRecord error in ps.Streams.Error){if(uiTest||backend)Console.Error.WriteLine(error.Exception.Message);else MessageBox.Show(error.Exception.Message,"QLTK - Lỗi");}return 1;}
   }
  }return 0;}catch(Exception e){if(uiTest||backend)Console.Error.WriteLine(e.Message);else MessageBox.Show(e.Message,"QLTK - Lỗi");return 1;}finally{if(controller!=null){if(ownsController)controller.ReleaseMutex();controller.Dispose();}}
 }
}
