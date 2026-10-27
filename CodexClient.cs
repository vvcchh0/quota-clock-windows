using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuotaClock
{
    // One owned stdio connection; no ports, credential-file reads, or model requests.
    public sealed class CodexClient : IDisposable
    {
        readonly object gate=new object();
        readonly SemaphoreSlim serial=new SemaphoreSlim(1,1);
        Process active;
        bool disposed;
        int nextId=2,starts;
        readonly int timeoutMilliseconds;
        public CodexClient():this(30000){}
        internal CodexClient(int timeout){timeoutMilliseconds=timeout;}
        DateTime lastTrim=DateTime.MinValue;
        public int ConnectionsStarted {get{return Volatile.Read(ref starts);}}
        public int ProcessId {get{lock(gate){try{return active!=null&&!active.HasExited?active.Id:0;}catch{return 0;}}}}
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);
        public static string FindCli()
        {
            string specified=Environment.GetEnvironmentVariable("QUOTA_CLOCK_CODEX");
            if(!String.IsNullOrEmpty(specified)&&File.Exists(specified)&&specified.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(specified);
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if(Directory.Exists(root)){
                string candidate=Directory.GetDirectories(root).Select(d=>Path.Combine(d,"codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(candidate!=null)return candidate;
            }
            foreach(string d in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){
                try{string p=Path.Combine(d.Trim('"'),"codex.exe");if(File.Exists(p))return p;}catch(ArgumentException){}
            }
            throw new InvalidOperationException("未找到 Codex。请先安装并登录 Codex 桌面版。");
        }
        public async Task<Snapshot> ReadAsync(bool keepAlive=true)
        {
            await serial.WaitAsync().ConfigureAwait(false);
            using(var cancel=new CancellationTokenSource()){
                try {
                    lock(gate){if(disposed)throw new ObjectDisposedException("CodexClient");}
                    CancellationToken token=cancel.Token;
                    var work=Task.Run(async delegate{return await ReadSession(token).ConfigureAwait(false);});
                    if(await Task.WhenAny(work,Task.Delay(timeoutMilliseconds,token)).ConfigureAwait(false)!=work){
                        cancel.Cancel();CloseConnection();ObserveFailure(work);
                        throw new TimeoutException("额度读取超时，请稍后刷新。");
                    }
                    var result=await work.ConfigureAwait(false);cancel.Cancel();
                    if(keepAlive)TrimIdlePages();else CloseConnection();
                    return result;
                }
                catch {CloseConnection();throw;}
                finally{cancel.Cancel();serial.Release();}
            }
        }
        static void ObserveFailure(Task work){work.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        async Task<Snapshot> ReadSession(CancellationToken cancel)
        {
            Process p;
            lock(gate){p=active;}
            if(p!=null){try{if(p.HasExited){CloseConnection();p=null;}}catch{CloseConnection();p=null;}}
            if(p==null){
                var info=new ProcessStartInfo(FindCli(),"app-server --stdio"){
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                    WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory
                };
                p=new Process{StartInfo=info};
                lock(gate){
                    if(disposed||cancel.IsCancellationRequested){p.Dispose();throw new ObjectDisposedException("CodexClient");}
                    p.Start();active=p;Interlocked.Increment(ref starts);
                }
                p.ErrorDataReceived+=delegate{};p.BeginErrorReadLine();
                Send(p,new{id=1,method="initialize",@params=new{clientInfo=new{name="quota_clock",title="Quota Clock",version="0.5.0"}}});
                await Reply(p,1).ConfigureAwait(false);
                Send(p,new{method="initialized",@params=new{}});
                nextId=2;lastTrim=DateTime.MinValue;
            }
            cancel.ThrowIfCancellationRequested();
            int id=nextId++;
            Send(p,new{id=id,method="account/rateLimits/read"});
            return QuotaParser.Parse(await Reply(p,id).ConfigureAwait(false),DateTimeOffset.Now);
        }
        static void Send(Process p,object value){p.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(value));p.StandardInput.Flush();}
        static async Task<string> Reply(Process p,int wanted)
        {
            var serializer=new JavaScriptSerializer();
            for(int lines=0;lines<5000;lines++){
                string line=await p.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if(line==null)throw new InvalidOperationException("Codex 连接已结束，下次刷新会自动重连。");
                if(line.Length>2000000)throw new InvalidOperationException("额度接口返回了异常响应。");
                Dictionary<string,object> msg;try{msg=serializer.DeserializeObject(line) as Dictionary<string,object>;}catch{continue;}
                object id;if(msg==null||!msg.TryGetValue("id",out id))continue;
                int n;if(!Int32.TryParse(Convert.ToString(id,CultureInfo.InvariantCulture),out n)||n!=wanted)continue;
                if(msg.ContainsKey("error"))throw new InvalidOperationException("Codex 未能返回额度。请确认已登录，稍后再试。");
                return line;
            }
            throw new InvalidOperationException("额度接口返回了过多无关消息。");
        }
        void TrimIdlePages()
        {
            // Bound the owned native cache as well as resident pages. Retiring a
            // large/old connection releases actual private commit; trimming only
            // reclaims working pages and is deliberately limited to completed reads.
            bool retire=false;
            lock(gate){try{if(active!=null&&!active.HasExited){
                active.Refresh();retire=active.PrivateMemorySize64>96L*1048576 || nextId>=122;
                if(!retire && active.WorkingSet64>32L*1048576 && DateTime.UtcNow-lastTrim>TimeSpan.FromSeconds(10)){
                    EmptyWorkingSet(active.Handle);lastTrim=DateTime.UtcNow;
                }
            }}catch{}}
            if(retire)CloseConnection();
        }
        void CloseConnection()
        {
            Process p;lock(gate){p=active;active=null;}
            if(p==null)return;
            try{p.StandardInput.Close();}catch{}
            try{if(!p.HasExited&&!p.WaitForExit(250)){p.Kill();p.WaitForExit(1000);}}catch{}
            p.Dispose();
        }
        public void ReleaseIdle()
        {
            Task.Run(delegate{if(!serial.Wait(0))return;try{CloseConnection();}finally{serial.Release();}});
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;}CloseConnection();}
    }
}
