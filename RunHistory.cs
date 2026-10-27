using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace QuotaClock
{
    public sealed class ProjectionSample
    {
        public double Time,Consumed;
        public int Mask,Epoch;
        public bool Estimated;
        public double HourY,MinuteY,FineY;
        public double Y(int hand){return hand==0?HourY:hand==1?MinuteY:FineY;}
        public bool Valid(int hand){return (Mask&(1<<hand))!=0;}
        public static ProjectionSample Create(double time,double consumed,int mask,bool estimated,int epoch)
        {
            return new ProjectionSample{Time=time,Consumed=consumed,Mask=mask,Estimated=estimated,Epoch=epoch,
                HourY=-Math.Cos(consumed*Math.PI/50)*67/102,
                MinuteY=-Math.Cos(consumed*Math.PI/5)*94/102,
                FineY=-Math.Cos(consumed*Math.PI*2)};
        }
        public static bool CanJoin(ProjectionSample a,ProjectionSample b,int hand)
        {
            double dt=b.Time-a.Time,change=b.Consumed-a.Consumed;
            return a.Valid(hand)&&b.Valid(hand)&&a.Epoch==b.Epoch&&dt>0&&dt<=45&&change>=-.001&&change<=(hand==0?100:hand==1?10:1)
                &&(hand!=2||a.Estimated==b.Estimated);
        }
    }
    public sealed class ProjectionBucket
    {
        public ProjectionSample First,Last;
        public readonly double[] Min={Double.PositiveInfinity,Double.PositiveInfinity,Double.PositiveInfinity};
        public readonly double[] Max={Double.NegativeInfinity,Double.NegativeInfinity,Double.NegativeInfinity};
        public long Count;
        public int JoinedMask;
        public bool Estimated;
        public ProjectionBucket(ProjectionSample point)
        {
            First=Last=point;Count=1;JoinedMask=point.Mask;Estimated=point.Estimated;
            for(int h=0;h<3;h++)if(point.Valid(h))Min[h]=Max[h]=point.Y(h);
        }
        public static ProjectionBucket Merge(ProjectionBucket a,ProjectionBucket b)
        {
            var result=new ProjectionBucket(a.First){Last=b.Last,Count=a.Count+b.Count,Estimated=a.Estimated||b.Estimated,JoinedMask=a.JoinedMask&b.JoinedMask};
            for(int h=0;h<3;h++){
                result.Min[h]=Math.Min(a.Min[h],b.Min[h]);result.Max[h]=Math.Max(a.Max[h],b.Max[h]);
                if(!ProjectionSample.CanJoin(a.Last,b.First,h))result.JoinedMask&=~(1<<h);
            }return result;
        }
    }
    public sealed class ProjectionView
    {
        public ProjectionBucket[] Buckets=new ProjectionBucket[0];
        public double Start,End;
        public long Samples;
        public DateTimeOffset Started;
        public bool Aggregated {get{return Buckets.Any(b=>b.Count>1);}}
    }
    public sealed class RunTimeline
    {
        public const int OverviewLimit=2048,RecentLimit=4096;
        readonly object gate=new object();
        readonly List<ProjectionBucket> overview=new List<ProjectionBucket>();
        readonly Queue<ProjectionSample> recent=new Queue<ProjectionSample>();
        long count;
        double duration;
        int revision;
        public long Count {get{lock(gate)return count;}}
        public int Revision {get{lock(gate)return revision;}}
        public double Duration {get{lock(gate)return duration;}}
        public int BucketCount {get{lock(gate)return overview.Count;}}
        public int RecentCount {get{lock(gate)return recent.Count;}}
        public void Add(ProjectionSample sample)
        {
            lock(gate){
                if(count>0&&sample.Time<duration)throw new InvalidDataException("运行记录的时间顺序无效。");
                overview.Add(new ProjectionBucket(sample));recent.Enqueue(sample);if(recent.Count>RecentLimit)recent.Dequeue();
                count++;duration=sample.Time;revision++;
                if(overview.Count>OverviewLimit){
                    var compact=new List<ProjectionBucket>();
                    for(int i=0;i<overview.Count;i+=2)compact.Add(i+1<overview.Count?ProjectionBucket.Merge(overview[i],overview[i+1]):overview[i]);
                    overview.Clear();overview.AddRange(compact);
                }
            }
        }
        public bool HasExactRange(double start){lock(gate)return recent.Count>0&&start>=recent.Peek().Time;}
        public ProjectionView View(double start,double end,DateTimeOffset started)
        {
            lock(gate){
                IEnumerable<ProjectionBucket> selected;
                if(recent.Count>0&&start>=recent.Peek().Time){
                    var raw=recent.ToArray();int first=Array.FindIndex(raw,p=>p.Time>=start);if(first<0)first=raw.Length-1;first=Math.Max(0,first-1);
                    selected=raw.Skip(first).TakeWhile(p=>p.Time<=end+15).Select(p=>new ProjectionBucket(p));
                }else selected=overview.Where(b=>b.Last.Time>=start&&b.First.Time<=end);
                return new ProjectionView{Buckets=selected.ToArray(),Start=start,End=Math.Max(start+1,end),Samples=count,Started=started};
            }
        }
    }
    public sealed class RunRecording:IDisposable
    {
        public const string Header="# QuotaClock projection history v1";
        const int QueueLimit=256,IndexLimit=2048;
        readonly object gate=new object();
        readonly Queue<ProjectionSample> pending=new Queue<ProjectionSample>();
        readonly List<Checkpoint> index=new List<Checkpoint>();
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly bool persistent;
        readonly string directory;
        Thread writer;
        bool enabled,stopping,disposed;
        int epoch;
        string key;
        long written,dropped;
        volatile string error;
        public readonly RunTimeline Timeline=new RunTimeline();
        public readonly DateTimeOffset Started=DateTimeOffset.UtcNow;
        public readonly double StartMonotonic=TrailHistory.Now;
        public readonly string FilePath;
        sealed class Checkpoint {public double Time;public long Offset;}
        public bool Enabled {get{lock(gate)return enabled;}}
        public long Written {get{return Interlocked.Read(ref written);}}
        public long Dropped {get{return Interlocked.Read(ref dropped);}}
        public string Error {get{return error;}}
        public RunRecording(string folder,bool record,bool persist=true)
        {
            directory=folder;persistent=persist;enabled=record;
            FilePath=persist?Path.Combine(folder,"run-"+Started.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".csv"):null;
        }
        public void SetEnabled(bool value)
        {
            bool paused;lock(gate){paused=enabled&&!value;enabled=value;}
            if(paused)Append(ProjectionSample.Create(Math.Max(Timeline.Duration,Math.Max(0,TrailHistory.Now-StartMonotonic)),0,0,false,epoch));
        }
        public void Record(TrailPoint point,string window)
        {
            lock(gate){if(!enabled||disposed||error!=null||point.Time<StartMonotonic)return;if(window!=key){key=window;epoch++;}}
            int mask=(point.CoarseValid?3:0)|(point.Valid?4:0);
            Append(ProjectionSample.Create(point.Time-StartMonotonic,point.Consumed,mask,point.Estimated,epoch));
        }
        void Append(ProjectionSample sample)
        {
            Timeline.Add(sample);
            if(!persistent)return;
            lock(gate){
                if(disposed)return;
                if(pending.Count>=QueueLimit){Interlocked.Increment(ref dropped);return;}
                pending.Enqueue(sample);
                if(writer==null){writer=new Thread(WriteLoop){IsBackground=true,Name="QuotaClock history",Priority=ThreadPriority.BelowNormal};writer.Start();}
                wake.Set();
            }
        }
        void WriteLoop()
        {
            ProjectionSample inFlight=null;
            try{
                Directory.CreateDirectory(directory);
                using(var file=new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.SequentialScan))
                using(var output=new StreamWriter(file,new UTF8Encoding(false))){
                    output.WriteLine(Header);output.WriteLine("# started_utc="+Started.ToString("o",CultureInfo.InvariantCulture));
                    output.WriteLine("elapsed_s,consumed_percent,hour_y,minute_y,fine_y,coarse_valid,fine_valid,estimated,epoch");output.Flush();
                    while(true){
                        ProjectionSample sample=null;
                        lock(gate){if(pending.Count>0)sample=pending.Dequeue();else if(stopping)break;}
                        if(sample==null){wake.WaitOne();continue;}
                        inFlight=sample;
                        if(Written%256==0){output.Flush();lock(gate){index.Add(new Checkpoint{Time=sample.Time,Offset=file.Position});if(index.Count>IndexLimit){var keep=index.Where((v,i)=>i%2==0).ToList();index.Clear();index.AddRange(keep);}}}
                        output.WriteLine(Serialize(sample));output.Flush();Interlocked.Increment(ref written);inFlight=null;
                    }
                }
            }catch(Exception e){error=e is UnauthorizedAccessException?"运行记录目录不可写":"运行记录写入失败";lock(gate){Interlocked.Add(ref dropped,pending.Count+(inFlight==null?0:1));pending.Clear();}}
        }
        static string Serialize(ProjectionSample p)
        {
            return String.Join(",",new[]{p.Time.ToString("F3",CultureInfo.InvariantCulture),p.Consumed.ToString("F8",CultureInfo.InvariantCulture),p.HourY.ToString("F8",CultureInfo.InvariantCulture),p.MinuteY.ToString("F8",CultureInfo.InvariantCulture),p.FineY.ToString("F8",CultureInfo.InvariantCulture),(p.Mask&3)!=0?"1":"0",p.Valid(2)?"1":"0",p.Estimated?"1":"0",p.Epoch.ToString(CultureInfo.InvariantCulture)});
        }
        public ProjectionView ReadRange(double start,double end,CancellationToken cancel,bool forceDisk=false)
        {
            if(!forceDisk&&Timeline.HasExactRange(start)||FilePath==null||!File.Exists(FilePath))return Timeline.View(start,end,Started);
            long offset=0;lock(gate){foreach(var item in index)if(item.Time<=start)offset=item.Offset;else break;}
            return ReadFile(FilePath,start,end,offset,Started,cancel);
        }
        static string Line(StreamReader reader,CancellationToken cancel)
        {
            var chars=new char[512];int length=0,c;
            while((c=reader.Read())>=0){if(c=='\n')break;if(length>=chars.Length)throw new InvalidDataException("CSV 行过长。");chars[length++]=(char)c;if((length&127)==0)cancel.ThrowIfCancellationRequested();}
            return c<0?null:new String(chars,0,length).TrimEnd('\r');
        }
        public static ProjectionView ReadFile(string path,double start,double end,long offset,DateTimeOffset started,CancellationToken cancel)
        {
            var timeline=new RunTimeline();ProjectionSample previous=null;bool began=false;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(file,Encoding.UTF8,true,4096)){
                if(Line(reader,cancel)!=Header)throw new InvalidDataException("不是额度时钟的轨迹记录。");
                if(offset>0){reader.DiscardBufferedData();file.Seek(offset,SeekOrigin.Begin);}
                string line;
                while((line=Line(reader,cancel))!=null){
                    cancel.ThrowIfCancellationRequested();if(line.StartsWith("# started_utc=")){DateTimeOffset stamp;if(DateTimeOffset.TryParse(line.Substring(14),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out stamp))started=stamp;continue;}
                    var sample=Parse(line);if(sample==null)continue;
                    if(sample.Time<start){previous=sample;continue;}
                    if(!began&&previous!=null)timeline.Add(previous);began=true;timeline.Add(sample);if(sample.Time>end)break;
                }
            }
            return timeline.View(start,Double.IsPositiveInfinity(end)?timeline.Duration:end,started);
        }
        static ProjectionSample Parse(string line)
        {
            var v=line.Split(',');if(v.Length!=9)return null;
            double t,u,h,m,s;int coarse,fine,estimated,epoch;
            if(!Number(v[0],out t)||!Number(v[1],out u)||!Number(v[2],out h)||!Number(v[3],out m)||!Number(v[4],out s)||
                !Int32.TryParse(v[5],out coarse)||!Int32.TryParse(v[6],out fine)||!Int32.TryParse(v[7],out estimated)||!Int32.TryParse(v[8],out epoch))return null;
            if(t<0||t>3153600000.0||u<0||u>100||Math.Abs(h)>1||Math.Abs(m)>1||Math.Abs(s)>1||coarse<0||coarse>1||fine<0||fine>1||estimated<0||estimated>1||epoch<0)return null;
            return new ProjectionSample{Time=t,Consumed=u,HourY=h,MinuteY=m,FineY=s,Mask=(coarse==1?3:0)|(fine==1?4:0),Estimated=estimated==1,Epoch=epoch};
        }
        static bool Number(string s,out double v){return Double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!Double.IsNaN(v)&&!Double.IsInfinity(v);}
        public void Dispose()
        {
            Thread thread;lock(gate){if(disposed)return;disposed=true;stopping=true;enabled=false;thread=writer;wake.Set();}
            if(thread==null||thread.Join(2000))wake.Dispose();
        }
    }
}
