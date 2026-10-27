using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace QuotaClock
{
    public static class ClientChecks
    {
        static bool Alive(int pid){try{using(var p=Process.GetProcessById(pid))return !p.HasExited;}catch{return false;}}
        public static int Run()
        {
            string cli=Environment.GetEnvironmentVariable("QUOTA_CLOCK_CODEX"),mode=Environment.GetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE");
            int count=0;Action<bool,string> check=(v,n)=>{if(!v)throw new Exception(n);count++;};
            try{
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_CODEX",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"research-v0.4","FakeCli.exe"));
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","good");int owned;
                using(var c=new CodexClient()){
                    var reads=Task.WhenAll(c.ReadAsync(),c.ReadAsync(),c.ReadAsync()).GetAwaiter().GetResult();
                    check(reads.Length==3&&reads[2].Windows.Count==1,"concurrent reads serialized and notifications ignored");
                    check(c.ConnectionsStarted==1,"reuse one process across three reads");owned=c.ProcessId;
                    check(Alive(owned),"owned child remains connected");
                }
                check(!Alive(owned),"dispose closes owned child");
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","exit");
                using(var c=new CodexClient()){
                    c.ReadAsync().GetAwaiter().GetResult();Thread.Sleep(250);c.ReadAsync().GetAwaiter().GetResult();
                    check(c.ConnectionsStarted==2,"dead child reconnects on next read");
                }
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","error");
                using(var c=new CodexClient()){
                    bool failed=false;try{c.ReadAsync().GetAwaiter().GetResult();}catch(InvalidOperationException){failed=true;}
                    check(failed&&c.ProcessId==0,"RPC error closes connection");
                    Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","good");c.ReadAsync().GetAwaiter().GetResult();
                    check(c.ConnectionsStarted==2,"error can recover without restarting app");
                    c.ReadAsync(false).GetAwaiter().GetResult();check(c.ProcessId==0,"one-shot option releases connection");
                }
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","hang");
                using(var c=new CodexClient()){
                    var read=c.ReadAsync();for(int i=0;i<50&&c.ProcessId==0;i++)Thread.Sleep(20);
                    owned=c.ProcessId;c.Dispose();bool failed=false;
                    try{read.GetAwaiter().GetResult();}catch{failed=true;}
                    check(failed&&!Alive(owned),"dispose interrupts blocked read and reaps process");
                }
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","good");
                using(var c=new CodexClient()){
                    for(int i=0;i<120;i++)c.ReadAsync().GetAwaiter().GetResult();
                    check(c.ProcessId==0,"connection retires after 120 reads");
                    c.ReadAsync().GetAwaiter().GetResult();check(c.ConnectionsStarted==2,"retired connection recreates on demand");
                }
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","large");
                using(var c=new CodexClient()){
                    var result=c.ReadAsync().GetAwaiter().GetResult();
                    check(result.Windows.Count==1&&c.ProcessId==0,"private cache cap releases process without losing result");
                }
                Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","hang");
                using(var c=new CodexClient(1000)){
                    bool timedOut=false;try{c.ReadAsync().GetAwaiter().GetResult();}catch(TimeoutException){timedOut=true;}
                    check(timedOut&&c.ProcessId==0,"deadline cancels and reaps blocked RPC");
                    Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE","good");
                    c.ReadAsync().GetAwaiter().GetResult();check(c.ConnectionsStarted==2,"timeout recovers on next read");
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"client-test-results.txt"),"PASS: "+count+" client checks. Reuse, serialization, notifications, reconnect, errors, one-shot and interrupted dispose.");return 0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"client-test-results.txt"),"FAIL after "+count+": "+e.GetType().Name+" "+e.Message);return 1;}
            finally{Environment.SetEnvironmentVariable("QUOTA_CLOCK_CODEX",cli);Environment.SetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE",mode);}
        }
    }
}
