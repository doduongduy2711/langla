namespace TGLL {
    public static class ClientPollingTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        static byte[] ReadExact(Stream stream,int count){
            var data=new byte[count];int offset=0;
            while(offset<count){int n=stream.Read(data,offset,count-offset);if(n==0)throw new IOException("Unexpected EOF");offset+=n;}
            return data;
        }
        static async Task EchoHttpProxy(TcpClient client){
            using(client){NetworkStream stream=client.GetStream();var header=new StringBuilder();var b=new byte[1];
                while(!header.ToString().EndsWith("\r\n\r\n",StringComparison.Ordinal)){int n=await stream.ReadAsync(b,0,1).ConfigureAwait(false);if(n==0)return;header.Append((char)b[0]);}
                byte[] reply=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n");await stream.WriteAsync(reply,0,reply.Length).ConfigureAwait(false);
                var buffer=new byte[1024];int count;
                while((count=await stream.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))>0)await stream.WriteAsync(buffer,0,count).ConfigureAwait(false);
            }
        }
        static async Task AcceptEchoClients(TcpListener listener,int count){
            var connections=new List<Task>();
            for(int i=0;i<count;i++)connections.Add(EchoHttpProxy(await listener.AcceptTcpClientAsync().ConfigureAwait(false)));
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
        static int TestIdleConnections(){
            var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();Task server=AcceptEchoClients(proxy,32);
            var relays=new List<SocksRelay>();var games=new List<TcpClient>();IntPtr map=Marshal.AllocHGlobal(2048);
            int growth;
            using(var remote=new Remote(Process.GetCurrentProcess())){
                remote.Write(map.ToInt64(),new byte[2048]);int before=Process.GetCurrentProcess().Threads.Count;
                try{
                    var config=new SocksConfig("127.0.0.1",((IPEndPoint)proxy.LocalEndpoint).Port,"","","http");
                    for(int i=0;i<32;i++){
                        var relay=new SocksRelay(remote,map.ToInt64(),config);relays.Add(relay);var game=new TcpClient();games.Add(game);game.ReceiveTimeout=5000;
                        game.Connect(IPAddress.Loopback,relay.Port);int port=((IPEndPoint)game.Client.LocalEndPoint).Port;
                        byte[] slot=new byte[32];slot[0]=1;slot[4]=(byte)(port>>8);slot[5]=(byte)port;slot[10]=48;slot[11]=57;slot[12]=127;slot[15]=1;remote.Write(map.ToInt64(),slot);
                        game.GetStream().WriteByte(42);Assert(game.GetStream().ReadByte()==42,"Idle connection handshake failed");
                    }
                    Thread.Sleep(100);growth=Process.GetCurrentProcess().Threads.Count-before;
                    Assert(growth<20,"Established idle connections still reserve threads");
                    foreach(var game in games)game.Close();Assert(server.Wait(5000),"Idle connections did not close");
                }finally{foreach(var game in games)game.Close();foreach(var relay in relays)relay.Dispose();proxy.Stop();Marshal.FreeHGlobal(map);}
            }
            return growth;
        }
        static void TestRelay(string protocol,bool authenticated){
            var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();
            byte[] payload=Enumerable.Range(0,32768).Select(n=>(byte)(n%251)).ToArray();
            Task server=Task.Factory.StartNew(delegate(){
                using(TcpClient client=proxy.AcceptTcpClient()){
                    client.ReceiveTimeout=5000;client.SendTimeout=5000;Stream stream=client.GetStream();
                    if(protocol=="http"){
                        var header=new StringBuilder();while(!header.ToString().EndsWith("\r\n\r\n",StringComparison.Ordinal)){int b=stream.ReadByte();Assert(b>=0&&header.Length<16384,"HTTP handshake truncated");header.Append((char)b);}
                        Assert(header.ToString().StartsWith("CONNECT 127.0.0.1:12345 "),"Wrong HTTP destination");
                        Assert(header.ToString().Contains("Proxy-Authorization:")==authenticated,"HTTP credentials changed");
                        byte[] reply=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n");stream.Write(reply,0,reply.Length);
                    }else{
                        byte[] hello=ReadExact(stream,3);Assert(hello[0]==5&&hello[2]==(authenticated?2:0),"SOCKS auth method changed");
                        stream.Write(new byte[]{5,hello[2]},0,2);
                        if(authenticated){Assert(stream.ReadByte()==1,"Wrong SOCKS auth version");int n=stream.ReadByte();Assert(Encoding.UTF8.GetString(ReadExact(stream,n))=="test","SOCKS username changed");n=stream.ReadByte();Assert(Encoding.UTF8.GetString(ReadExact(stream,n))=="test","SOCKS password changed");stream.Write(new byte[]{1,0},0,2);}
                        byte[] request=ReadExact(stream,10);Assert(request[1]==1&&request[4]==127&&request[8]==48&&request[9]==57,"Wrong SOCKS destination");
                        stream.Write(new byte[]{5,0,0,1,127,0,0,1,0,0},0,10);
                    }
                    byte[] received=ReadExact(stream,payload.Length);Assert(received.SequenceEqual(payload),"Uplink corrupted");stream.Write(received,0,received.Length);
                    Assert(stream.ReadByte()==-1,"Client half-close not forwarded");stream.Write(new byte[]{1,2,3,4},0,4);
                }
            });
            IntPtr map=Marshal.AllocHGlobal(2048);
            try{using(var remote=new Remote(Process.GetCurrentProcess())){
                remote.Write(map.ToInt64(),new byte[2048]);
                var config=new SocksConfig("127.0.0.1",((IPEndPoint)proxy.LocalEndpoint).Port,authenticated?"test":"",authenticated?"test":"",protocol);
                using(var relay=new SocksRelay(remote,map.ToInt64(),config))using(var game=new TcpClient()){
                    game.ReceiveTimeout=5000;game.SendTimeout=5000;game.Connect(IPAddress.Loopback,relay.Port);
                    int port=((IPEndPoint)game.Client.LocalEndPoint).Port;byte[] slot=new byte[32];slot[0]=1;slot[4]=(byte)(port>>8);slot[5]=(byte)port;slot[10]=48;slot[11]=57;slot[12]=127;slot[15]=1;remote.Write(map.ToInt64(),slot);
                    NetworkStream stream=game.GetStream();stream.Write(payload,0,payload.Length);game.Client.Shutdown(SocketShutdown.Send);
                    Assert(ReadExact(stream,payload.Length).SequenceEqual(payload),"Downlink corrupted");
                    Assert(ReadExact(stream,4).SequenceEqual(new byte[]{1,2,3,4}),"Half-close lost final response");
                    Assert(server.Wait(5000),"Proxy worker did not finish");
                }
            }}finally{proxy.Stop();Marshal.FreeHGlobal(map);}
        }
        public static string Run(string logDirectory){
            // All workers share one budget even with many immediately due clients.
            int budgetCalls=0;bool endBudget=false;
            var budgetWatch=Stopwatch.StartNew();
            using(var paced=new ClientPollScheduler(4)){
                for(int i=0;i<128;i++)paced.Add(delegate(){Interlocked.Increment(ref budgetCalls);return !Volatile.Read(ref endBudget);},delegate(){return 10;});
                Thread.Sleep(400);
                long observedMs=budgetWatch.ElapsedMilliseconds;
                int observed=Volatile.Read(ref budgetCalls);
                Console.WriteLine("Shared dispatch budget: "+observed+" callbacks / "+observedMs+" ms with 128 clients.");
                Assert(observed>=40,"Global pacing starved ready clients");
                Assert(observed<=observedMs/2+8,"Workers exceeded the shared dispatch budget");
                Volatile.Write(ref endBudget,true);
            }
            const int clients=200,workers=4;
            int active=0,peak=0,overlap=0,finished=0;
            int[] running=new int[clients],calls=new int[clients];
            var complete=new ManualResetEventSlim(false);
            var watch=Stopwatch.StartNew();
            using(var scheduler=new ClientPollScheduler(workers)){
                for(int i=0;i<clients;i++){
                    int id=i;
                    scheduler.Add(delegate(){
                        if(Interlocked.Increment(ref running[id])!=1)Interlocked.Increment(ref overlap);
                        int current=Interlocked.Increment(ref active),old;
                        do{old=peak;if(current<=old)break;}while(Interlocked.CompareExchange(ref peak,current,old)!=old);
                        Thread.Sleep(2);int n=Interlocked.Increment(ref calls[id]);
                        Interlocked.Decrement(ref active);Interlocked.Decrement(ref running[id]);
                        if(n==3&&Interlocked.Increment(ref finished)==clients)complete.Set();
                        return n<3;
                    },delegate(){return 25;});
                }
                Assert(complete.Wait(10000),"Clients starved or callbacks stopped");
                Assert(peak<=workers&&peak>1,"Global poll concurrency exceeded limit");
                Assert(overlap==0,"Same client polled concurrently");
                Assert(calls.All(n=>n==3),"Client missed or duplicated callback");
                Assert(SpinWait.SpinUntil(delegate(){return scheduler.Count==0;},1000),"Completed clients retained");
                scheduler.Add(delegate(){throw new Exception("expected test fault");},delegate(){return 10;});
                Assert(SpinWait.SpinUntil(delegate(){return scheduler.Count==0;},1000),"Faulted client retained");
                var alive=new ManualResetEventSlim(false);
                scheduler.Add(delegate(){alive.Set();return false;},delegate(){return 10;});
                Assert(alive.Wait(1000),"Callback error killed worker");
            }
            Assert(NativeBackend.ClientPollIntervalMs(10,1)==10,"Small-session latency changed");
            Assert(NativeBackend.ClientPollIntervalMs(10,84)==50,"Fast poll not limited under load");
            Assert(NativeBackend.ClientPollIntervalMs(150,84)==250,"Maintenance not reduced under load");
            Assert(NativeBackend.ClientPollIntervalMs(150,200)==350,"Large-session cadence incorrect");
            Assert(NativeBackend.SettingsSnapshotIntervalMs(84)==3000,"Settings scan not reduced under load");
            // A stalled client must not receive a backlog of callbacks when it resumes.
            long lastEnd=0;int rounds=0;var spaced=new ManualResetEventSlim(false);
            using(var scheduler=new ClientPollScheduler(1)){
                scheduler.Add(delegate(){
                    long now=watch.ElapsedMilliseconds;
                    if(rounds>0)Assert(now-Interlocked.Read(ref lastEnd)>=60,"Catch-up burst after slow callback");
                    Thread.Sleep(90);Interlocked.Exchange(ref lastEnd,watch.ElapsedMilliseconds);
                    if(++rounds==3){spaced.Set();return false;}return true;
                },delegate(){return 60;});
                Assert(spaced.Wait(3000),"Slow-callback scheduling failed");
            }
            NativeBackend.LogDirectory=logDirectory;
            var job=new NativeJob("test","test",null,null,false);
            using(var remote=new Remote(Process.GetCurrentProcess())){
                long data=Marshal.AllocHGlobal(4096).ToInt64(),label=Marshal.AllocHGlobal(64).ToInt64();
                try{
                    remote.Write(data,new byte[4096]);remote.Write(label,new byte[64]);
                    remote.Write(label+16,BitConverter.GetBytes(3));remote.Write(label+20,Encoding.Unicode.GetBytes("one"));
                    byte[] menu=new byte[24];Array.Copy(BitConverter.GetBytes(2),0,menu,0,4);Array.Copy(BitConverter.GetBytes(1),0,menu,4,4);
                    remote.Write(data+2176,menu);remote.Write(data+2240,BitConverter.GetBytes(label));
                    NativeBackend.ReadDiagnostics(remote,data,job);Assert(job.MenuOptions[0]=="one","Menu first read failed");
                    remote.Write(data+2240,BitConverter.GetBytes(1L));
                    NativeBackend.ReadDiagnostics(remote,data,job);Assert(job.MenuOptions[0]=="one","Unchanged menu was reread");
                    remote.Write(data+2240,BitConverter.GetBytes(label));remote.Write(label+20,Encoding.Unicode.GetBytes("two"));
                    remote.Write(data+2176,BitConverter.GetBytes(4));
                    NativeBackend.ReadDiagnostics(remote,data,job);Assert(job.MenuOptions[0]=="two","New menu generation ignored");
                    remote.Write(data+2188,BitConverter.GetBytes(1));NativeBackend.ReadDiagnostics(remote,data,job);
                    Assert(job.MenuSelectedIndex==1,"Selection update ignored");
                }finally{Marshal.FreeHGlobal(new IntPtr(label));Marshal.FreeHGlobal(new IntPtr(data));}
            }
            TestRelay("http",false);TestRelay("http",true);TestRelay("socks5",false);TestRelay("socks5",true);
            int connectedGrowth=TestIdleConnections();
            int idleGrowth;
            using(var remote=new Remote(Process.GetCurrentProcess())){
                var relays=new List<SocksRelay>();IntPtr map=Marshal.AllocHGlobal(2048);remote.Write(map.ToInt64(),new byte[2048]);
                int before=Process.GetCurrentProcess().Threads.Count;
                try{
                    for(int i=0;i<100;i++)relays.Add(new SocksRelay(remote,map.ToInt64(),new SocksConfig("127.0.0.1",1,"","")));
                    idleGrowth=Process.GetCurrentProcess().Threads.Count-before;
                    Assert(idleGrowth<20,"Idle relays still allocate one thread per client");
                }finally{foreach(var relay in relays)relay.Dispose();Marshal.FreeHGlobal(map);}
            }
            return "PASS: 200 simulated clients, 600 callbacks; peak concurrency="+peak+"; no overlaps/starvation; exception isolation; no catch-up bursts; load intervals; menu cache invalidation; HTTP/SOCKS5 auth + duplex + half-close; 32 connected idle relays thread growth="+connectedGrowth+"; 100 idle listeners thread growth="+idleGrowth+".";
        }
    }
}
