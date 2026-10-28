using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using TGLL;
class ProxyHttpTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Round(string response,bool auth,bool expect){
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();string request="";Exception serverError=null;
  var server=new Thread(delegate(){try{using(var client=listener.AcceptTcpClient()){client.ReceiveTimeout=5000;var stream=client.GetStream();var builder=new StringBuilder();while(builder.Length<16000){int b=stream.ReadByte();if(b<0)break;builder.Append((char)b);if(builder.ToString().EndsWith("\r\n\r\n"))break;}request=builder.ToString();byte[] bytes=Encoding.ASCII.GetBytes(response);stream.Write(bytes,0,bytes.Length);}}catch(Exception e){serverError=e;}});server.IsBackground=true;server.Start();
  var config=new SocksConfig("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,auth?"user":"",auth?"pass":"","http");bool success=false;
  try{using(var client=SocksRelay.Authenticate(config)){SocksRelay.ConnectHttp(client,config,new byte[]{2,0,0x1f,0x90,1,2,3,4,0,0,0,0,0,0,0,0});success=true;Check(client.GetStream().ReadByte()==88,"CONNECT parser consumed first tunneled byte");}}catch(IOException){if(expect)throw;}finally{server.Join(6000);listener.Stop();}
  if(serverError!=null)throw serverError;Check(success==expect,"HTTP result mismatch");Check(request.StartsWith("CONNECT 1.2.3.4:8080 HTTP/1.1\r\nHost: 1.2.3.4:8080\r\n"),"Wrong CONNECT authority");Check(request.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n")==auth,"Basic auth mismatch");
 }
 static void Main(){Round("HTTP/1.1 200 Connection Established\r\nHeader: value\r\n\r\nX",false,true);Round("HTTP/1.0 200 OK\r\n\r\nX",true,true);Round("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n",true,false);Round("HTTP/1.1 502 Bad Gateway\r\n\r\n",false,false);Check(new SocksConfig("localhost",1234,"","").Protocol=="socks5","Legacy SOCKS constructor changed");Console.WriteLine("PASS: HTTP CONNECT with/without Basic auth, exact destination, bounded header, 407/502 rejection and preserved first tunnel byte; legacy SOCKS default. Loopback only.");}
}
