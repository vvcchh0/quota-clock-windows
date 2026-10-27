using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
namespace QuotaClock
{
    public static class CalibrationTests
    {
        public static int Run()
        {
            int n=0;Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);n++;};
            try
            {
                long now=81000,reset=10000;
                var usage=new List<UsagePoint>();var anchors=new List<QuotaAnchor>();
                for(int i=1;i<=80;i++)
                {
                    usage.Add(new UsagePoint{Time=i*1000,Weight=1,Tokens=100});
                    anchors.Add(new QuotaAnchor{Id="codex:primary",Reset=reset,Time=i*1000,Used=Math.Round(i*.1m),Raw=true});
                }
                var official=new QuotaWindow{Id="codex:primary",Used=8,Reset=reset,Minutes=10080};
                var fit=Calibration.Evaluate(usage,anchors,official,now);
                check(fit.Available&&!fit.Exact,"stable quantized relationship permits explicitly estimated needle");
                check(fit.Rms<.45&&fit.R2>.95,"fit has quality bounds");
                check(fit.Remaining>=91.51m&&fit.Remaining<=92.49m,"estimate remains inside official rounding bin");
                usage.Add(new UsagePoint{Time=80500,Weight=2,Tokens=100});
                var progressed=Calibration.Evaluate(usage,anchors,official,now);
                check(progressed.Available&&progressed.Remaining<fit.Remaining,"new local tokens move consumption clockwise");
                check(!Calibration.Evaluate(usage,anchors.Take(10).ToList(),official,now).Available,"short calibration rejected");
                var unknown=usage.Select(p=>new UsagePoint{Time=p.Time,Weight=p.Weight,Tokens=100,UnknownTokens=50}).ToList();
                check(!Calibration.Evaluate(unknown,anchors,official,now).Available,"large unknown-model share rejected");
                var expired=new QuotaWindow{Id=official.Id,Used=8,Reset=60};
                check(!Calibration.Evaluate(usage,anchors,expired,now).Available,"expired window rejected");
                var other=new QuotaWindow{Id="other:primary",Used=8,Reset=reset};
                check(!Calibration.Evaluate(usage,anchors,other,now).Available,"buckets are not combined");
                check(!Calibration.Evaluate(usage,anchors,official,now+700000).Available,"stale calibration rejected");
                var raw=new List<QuotaAnchor>{new QuotaAnchor{Id=official.Id,Reset=reset,Time=now,Used=8.3m,Raw=true}};
                var exact=Calibration.Evaluate(usage,raw,official,now);
                check(exact.Available&&exact.Exact&&exact.Remaining==91.7m,"fresh raw fraction preferred without regression");
                raw[0].Time=now-400000;
                check(!Calibration.Evaluate(usage,raw,official,now).Available,"old raw fraction rejected");
                var noisy=anchors.Select((a,i)=>new QuotaAnchor{Id=a.Id,Reset=a.Reset,Time=a.Time,Used=Math.Max(0,a.Used+(i%2==0?2:-2)),Raw=true}).ToList();
                check(!Calibration.Evaluate(usage,noisy,official,now).Available,"unreliable data cannot fabricate precision");
                foreach(string kind in new[]{"turn_context","token_usage_record","event_msg"}) {
                    byte[] header=System.Text.Encoding.UTF8.GetBytes("{\"timestamp\":\"2026-10-27T00:00:00Z\",\"type\":\""+kind+"\",\"payload\":{}}");
                    check(LocalQuotaMeter.HeaderKind(header,0,header.Length)==kind,"metadata header classification "+kind);
                }
                byte[] ignored=System.Text.Encoding.UTF8.GetBytes("{\"type\":\"response_item\",\"payload\":{}}");
                check(LocalQuotaMeter.HeaderKind(ignored,0,ignored.Length)=="other","conversation content skipped before JSON parsing");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"calibration-test-results.txt"),"PASS: "+n+" calibration quality checks.");
                return 0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"calibration-test-results.txt"),"FAIL after "+n+": "+e.Message);return 1;}
        }
    }
    public static class LocalProbe
    {
        public static int Run()
        {
            try
            {
                Snapshot snapshot;using(var c=new CodexClient())snapshot=c.ReadAsync().GetAwaiter().GetResult();
                var prefs=Preferences.Load();var w=snapshot.Windows.FirstOrDefault(x=>x.Id==prefs.SelectedId)??snapshot.Windows.FirstOrDefault();
                using(var done=new ManualResetEvent(false))
                using(var meter=new LocalQuotaMeter(delegate(EstimateView view){if(view.Anchors>0||view.Responses>0)done.Set();}))
                {
                    meter.Observe(w,snapshot.Fetched);
                    if(!done.WaitOne(TimeSpan.FromSeconds(45)))throw new TimeoutException("Local metadata scan timed out.");
                }
                return 0;
            }
            catch(Exception e)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"local-probe-error.txt"),e.GetType().Name+": "+e.Message);return 1;
            }
        }
    }
}

