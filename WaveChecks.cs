using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace QuotaClock
{
    public static class WaveChecks
    {
        public static int Run()
        {
            int count=0;
            Action<bool,string> check=(v,n)=>{if(!v)throw new Exception(n);count++;};
            try {
                check(Math.Abs(TrailHistory.Projection(0)+1)<1e-8,"starts at top");
                check(Math.Abs(TrailHistory.Projection(.25))<1e-8,"quarter at centre");
                check(Math.Abs(TrailHistory.Projection(.5)-1)<1e-8,"half at bottom");
                check(Math.Abs(TrailHistory.Projection(.75))<1e-8,"three quarters at centre");
                check(Math.Abs(TrailHistory.Projection(1)+1)<1e-8,"full fine cycle");
                check(TrailHistory.Offset(300,100)==100,"five minutes equals one radius");
                check(TrailHistory.Offset(-5,100)==0,"negative age clamped");
                var h=new TrailHistory();h.Capture(0,75,true,true,"a");h.Capture(5,74.99m,true,true,"a");
                check(h.Points.Count==1,"sampling is bounded to fifteen seconds");
                h.Capture(15,74.95m,true,true,"a");check(h.Points.Count==2,"fifteen-second sample retained");
                check(TrailHistory.CanJoin(h.Points[0],h.Points[1]),"continuous estimated samples join");
                h.Capture(16,74.95m,false,true,"a");check(!TrailHistory.CanJoin(h.Points[1],h.Points[2]),"missing data breaks curve immediately");
                h.Capture(17,74.95m,true,false,"a");check(!TrailHistory.CanJoin(h.Points[2],h.Points[3]),"precision source change breaks curve");
                check(!TrailHistory.CanJoin(new TrailPoint{Time=0,Consumed=2,Valid=true},new TrailPoint{Time=60,Consumed=2.1,Valid=true}),"suspend or missing interval not bridged");
                check(!TrailHistory.CanJoin(new TrailPoint{Time=0,Consumed=2,Valid=true},new TrailPoint{Time=15,Consumed=1.9,Valid=true}),"backward calibration not presented as consumption");
                check(!TrailHistory.CanJoin(new TrailPoint{Time=0,Consumed=2,Valid=true},new TrailPoint{Time=15,Consumed=4,Valid=true}),"ambiguous large jump not reconstructed");
                h.Capture(18,50,true,false,"b");check(h.Points.Count==1,"new reset or quota window clears history");
                h.Capture(1,50,true,false,"b");check(h.Points.Count==1,"clock reversal clears history");
                for(int i=1;i<600;i++)h.Capture(i*15,50,true,false,"b");
                check(h.Points.Count<=TrailHistory.Capacity&&h.Points.Count<=241,"one-hour retention and bounded capacity");
                h.Clear();check(h.Points.Count==0,"clear releases retained samples");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wave-test-results.txt"),"PASS: "+count+" waveform checks. Projection, scale, sampling, gaps, reset, suspend and retention.");return 0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wave-test-results.txt"),"FAIL after "+count+": "+e.Message);return 1;}
        }
        public static int Probe()
        {
            var rows=new List<object>();
            try {
                using(var client=new CodexClient()){
                    for(int i=0;i<4;i++){
                        var w=Stopwatch.StartNew();var snap=client.ReadAsync().GetAwaiter().GetResult();
                        int pid=client.ProcessId;double ws=0,priv=0,cpu=0;
                        if(pid!=0)using(var p=Process.GetProcessById(pid)){ws=p.WorkingSet64/1048576.0;priv=p.PrivateMemorySize64/1048576.0;cpu=p.TotalProcessorTime.TotalSeconds;}
                        rows.Add(new{read=i+1,ok=snap.Windows.Count>0,latencySeconds=w.Elapsed.TotalSeconds,pid=pid,starts=client.ConnectionsStarted,workingMiB=ws,privateMiB=priv,cpuSeconds=cpu});
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reuse-probe.json"),new JavaScriptSerializer().Serialize(rows));
                        if(i<3)Thread.Sleep(15000);
                    }
                }return 0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reuse-probe-error.txt"),e.GetType().Name);return 1;}
        }
    }
}
