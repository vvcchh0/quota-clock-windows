using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace QuotaClock
{
    public static class RunHistoryChecks
    {
        public static int Run()
        {
            int count=0;Action<bool,string> check=(v,n)=>{if(!v)throw new Exception(n);count++;};
            string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","history-"+Guid.NewGuid().ToString("N"));
            try{
                var sample=ProjectionSample.Create(0,0,7,false,1);
                check(Math.Abs(sample.HourY+67.0/102)<1e-9&&Math.Abs(sample.MinuteY+94.0/102)<1e-9&&sample.FineY==-1,"three projections preserve hand lengths");
                check(Math.Abs(ProjectionSample.Create(0,25,7,false,1).HourY)<1e-8,"hour quarter-cycle");
                check(Math.Abs(ProjectionSample.Create(0,5,7,false,1).MinuteY-94.0/102)<1e-8,"minute half-cycle");
                check(ProjectionSample.Create(0,.5,7,false,1).FineY==1,"fine half-cycle");
                var a=ProjectionSample.Create(0,1,3,false,1);var b=ProjectionSample.Create(15,2,3,false,1);
                check(ProjectionSample.CanJoin(a,b,0)&&ProjectionSample.CanJoin(a,b,1)&&!ProjectionSample.CanJoin(a,b,2),"coarse history survives unavailable fine hand");
                check(!ProjectionSample.CanJoin(a,ProjectionSample.Create(15,2,3,false,2),0),"epoch switch does not join");
                check(!ProjectionSample.CanJoin(a,ProjectionSample.Create(60,2,3,false,1),0),"suspend creates a gap");
                var history=new TrailHistory();int observed=0;history.Sampled+=delegate(TrailPoint point,string key){if(point.CoarseValid&&!point.Valid)observed++;};
                history.Capture(0,75,false,false,"one",true);check(observed==1&&history.CurrentCoarseValid,"rolling history emits official-only sample");
                var timeline=new RunTimeline();const int many=100000;
                for(int i=0;i<many;i++)timeline.Add(ProjectionSample.Create(i*15,(i%2000)*.05,7,true,i/2000));
                var all=timeline.View(0,timeline.Duration,DateTimeOffset.UtcNow);
                check(timeline.Count==many&&all.Samples==many,"all samples counted across a long run");
                check(timeline.BucketCount<=RunTimeline.OverviewLimit&&timeline.RecentCount<=RunTimeline.RecentLimit,"memory remains bounded after 100000 samples");
                check(all.Buckets.Sum(x=>x.Count)==many,"compaction retains full-run coverage");
                check(all.Buckets.First().First.Time==0&&all.Buckets.Last().Last.Time==(many-1)*15,"start and end survive compaction");
                check(all.Buckets.Any(x=>x.Min[2]<-.99&&x.Max[2]>.99),"fine extrema survive long-run compaction");
                var near=timeline.View(timeline.Duration-900,timeline.Duration,DateTimeOffset.UtcNow);
                check(near.Buckets.All(x=>x.Count==1)&&near.Buckets.Length<100,"recent zoom keeps original samples");
                bool reversed=false;try{timeline.Add(ProjectionSample.Create(0,0,7,false,1));}catch(InvalidDataException){reversed=true;}
                check(reversed,"out-of-order time rejected");
                RunRecording recording;
                using(recording=new RunRecording(output,true)){
                    for(int i=0;i<300;i++){recording.Record(new TrailPoint{Time=recording.StartMonotonic+i*15,Consumed=i*.05,CoarseValid=true,Valid=i>=50,Estimated=i>=50},i<100?"first":"second");while(i-recording.Written>64&&recording.Error==null)Thread.Sleep(1);}
                    check(recording.Timeline.Count==300,"quota epoch changes preserve earlier run records");
                    recording.SetEnabled(false);long paused=recording.Timeline.Count;
                    recording.Record(new TrailPoint{Time=recording.StartMonotonic+4500,Consumed=20,CoarseValid=true,Valid=true},"second");
                    check(recording.Timeline.Count==paused,"recording toggle stops sampling");
                    recording.SetEnabled(true);recording.Record(new TrailPoint{Time=recording.StartMonotonic+4515,Consumed=20,CoarseValid=true,Valid=true},"second");
                    check(recording.Timeline.Count==302,"resuming preserves prior records and pause marker");
                }
                check(recording.Error==null&&recording.Written==302&&recording.Dropped==0,"close flushes all queued rows");
                check(File.Exists(recording.FilePath)&&new FileInfo(recording.FilePath).Length>10000,"durable CSV created");
                var loaded=RunRecording.ReadFile(recording.FilePath,0,Double.PositiveInfinity,0,DateTimeOffset.Now,CancellationToken.None);
                check(loaded.Samples==302&&loaded.Started.ToUnixTimeSeconds()==recording.Started.ToUnixTimeSeconds(),"saved run reloads samples and start time");
                check(loaded.Buckets.Any(x=>x.First.Mask==3)&&loaded.Buckets.Any(x=>x.First.Mask==0),"fine-unavailable and paused states survive CSV");
                check(loaded.Buckets.Any(x=>x.First.Epoch==1)&&loaded.Buckets.Any(x=>x.First.Epoch==2),"window boundaries retained without private window identifiers");
                var selected=recording.ReadRange(4000,4200,CancellationToken.None,true);
                check(selected.Buckets.Length<25&&selected.Buckets.Any(x=>x.First.Time>=4000&&x.First.Time<=4200),"indexed range read restores original interval");
                string partial=Path.Combine(output,"partial.csv");File.WriteAllText(partial,File.ReadAllText(recording.FilePath)+"9999,1,0,0,0,1,1,0,2");
                check(RunRecording.ReadFile(partial,0,Double.PositiveInfinity,0,DateTimeOffset.Now,CancellationToken.None).Samples==302,"incomplete final CSV line ignored");
                string wrong=Path.Combine(output,"wrong.csv");File.WriteAllText(wrong,"unrelated,data\n");bool rejected=false;
                try{RunRecording.ReadFile(wrong,0,100,0,DateTimeOffset.Now,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
                check(rejected,"unrelated CSV rejected");
                string huge=Path.Combine(output,"long.csv");File.WriteAllText(huge,RunRecording.Header+"\n"+new String('x',600)+"\n");rejected=false;
                try{RunRecording.ReadFile(huge,0,100,0,DateTimeOffset.Now,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
                check(rejected,"oversized line rejected with bounded allocation");
                string overflow=Path.Combine(output,"overflow.csv");File.WriteAllText(overflow,RunRecording.Header+"\n1e300,1,0,0,0,1,1,0,1\n");
                check(RunRecording.ReadFile(overflow,0,Double.PositiveInfinity,0,DateTimeOffset.Now,CancellationToken.None).Samples==0,"unrenderable timestamp rejected");
                using(var cancel=new CancellationTokenSource()){
                    cancel.Cancel();bool stopped=false;try{RunRecording.ReadFile(recording.FilePath,0,100,0,DateTimeOffset.Now,cancel.Token);}catch(OperationCanceledException){stopped=true;}
                    check(stopped,"superseded reads can be canceled");
                }
                string memoryOnly=Path.Combine(output,"memory-only");
                using(var preview=new RunRecording(memoryOnly,true,false)){
                    preview.Record(new TrailPoint{Time=preview.StartMonotonic,Consumed=10,CoarseValid=true,Valid=true},"demo");
                    check(preview.Timeline.Count==1&&preview.FilePath==null&&!Directory.Exists(memoryOnly),"preview never writes account history");
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history-test-results.txt"),"PASS: "+count+" run-history checks. Three projections, full coverage, extrema, bounded memory, disk journal, pause, reload, seek, malformed files and cancellation.");return 0;
            }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history-test-results.txt"),"FAIL after "+count+": "+e.GetType().Name+" "+e.Message);return 1;}
        }
    }
}
