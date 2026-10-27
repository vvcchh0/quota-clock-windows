using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace QuotaClock
{
    public sealed class UsagePoint { public long Time; public double Weight, UnknownTokens, Tokens; }
    public sealed class QuotaAnchor { public long Time, Reset; public string Id; public decimal Used; public bool Raw; }
    public sealed class EstimateView
    {
        public bool Available, Exact;
        public decimal Remaining;
        public string State = "等待校准";
        public double Rms, R2, UnknownShare, HoldoutRms;
        public int Responses, Anchors;
    }

    public static class Calibration
    {
        static int Upper(long[] times,long value)
        { int lo=0,hi=times.Length;while(lo<hi){int mid=lo+(hi-lo)/2;if(times[mid]<=value)lo=mid+1;else hi=mid;}return lo; }
        public static EstimateView Evaluate(List<UsagePoint> input,List<QuotaAnchor> observations,QuotaWindow official,long now)
        {
            var result=new EstimateView { Responses=input.Count };
            if(official==null||!official.Reset.HasValue){result.State="等待官方额度";return result;}
            if(official.Reset.Value<=now/1000){result.State="额度窗口已到期";return result;}
            var anchors=observations.Where(a=>a.Id==official.Id&&Math.Abs(a.Reset-official.Reset.Value)<=60&&a.Time>=now-86400000&&a.Time<=now+1000)
                .OrderBy(a=>a.Time).ToList();
            var precise=anchors.LastOrDefault(a=>a.Raw&&a.Used!=Decimal.Truncate(a.Used)&&a.Time>=now-300000&&Math.Abs(a.Used-official.Used)<=1);
            if(precise!=null)return new EstimateView { Available=true, Exact=true, Remaining=100-precise.Used, State="本地原始小数", Responses=input.Count, Anchors=anchors.Count };
            // Older snapshots from concurrent requests can arrive late. Ignore small backwards steps.
            var monotonic=new List<QuotaAnchor>();decimal high=-1;
            foreach(var a in anchors)
            {
                if(a.Used<high-2){monotonic.Clear();high=-1;}
                if(a.Used>=high){monotonic.Add(a);high=a.Used;}
            }
            if(monotonic.Count>0){decimal floor=monotonic[monotonic.Count-1].Used-6;monotonic=monotonic.Where(a=>a.Used>=floor).ToList();}
            anchors=monotonic;result.Anchors=anchors.Count;
            if(anchors.Count<30||anchors.Select(a=>a.Used).Distinct().Count()<4||anchors.Max(a=>a.Used)-anchors.Min(a=>a.Used)<3)
            {result.State="校准中：需要至少 3 个百分点的变化";return result;}
            var points=input.OrderBy(p=>p.Time).ToList();long[] times=points.Select(p=>p.Time).ToArray();
            double[] sum=new double[points.Count+1],bad=new double[points.Count+1],tokens=new double[points.Count+1];
            for(int i=0;i<points.Count;i++){sum[i+1]=sum[i]+points[i].Weight;bad[i+1]=bad[i]+points[i].UnknownTokens;tokens[i+1]=tokens[i]+points[i].Tokens;}
            int first=Upper(times,anchors[0].Time),last=Upper(times,anchors[anchors.Count-1].Time);
            result.UnknownShare=(bad[last]-bad[first])/Math.Max(1,tokens[last]-tokens[first]);
            if(result.UnknownShare>.2){result.State="未识别模型占比较高，暂停估算";return result;}
            double[] xs=anchors.Select(a=>sum[Upper(times,a.Time)]).ToArray(),ys=anchors.Select(a=>(double)a.Used).ToArray();
            double slope,bias,rms,r2;
            if(!Fit(xs,ys,xs.Length,out slope,out bias,out rms,out r2)){result.State="校准数据不足";return result;}
            result.Rms=rms;result.R2=r2;
            // A temporal holdout protects against a changing model/mode mix or unobserved remote usage.
            int train=Math.Max(20,(int)(xs.Length*.8));double hs,hb,hr,h2,hold=0;
            if(train>=xs.Length||!Fit(xs,ys,train,out hs,out hb,out hr,out h2)){result.State="校准数据不足";return result;}
            for(int i=train;i<xs.Length;i++)hold+=Math.Pow(ys[i]-(hb+hs*xs[i]),2);
            hold=Math.Sqrt(hold/(xs.Length-train));result.HoldoutRms=hold;
            if(slope<=0||r2<.95||rms>.45||hold>.65){result.State="拟合不稳定，暂停估算";return result;}
            if(now-anchors[anchors.Count-1].Time>600000){result.State="官方校准点较旧，暂停估算";return result;}
            // The app-server rounds its percentage. Stay inside that +/- 0.5 percentage-point bucket.
            double estimate=bias+slope*sum[sum.Length-1];
            estimate=Math.Max((double)official.Used-.49,Math.Min((double)official.Used+.49,estimate));
            estimate=Math.Max(0,Math.Min(100,estimate));
            result.Available=true;result.Remaining=100-(decimal)estimate;result.State="本地加权校准估算";
            return result;
        }
        static bool Fit(double[] x,double[] y,int n,out double slope,out double bias,out double rms,out double r2)
        {
            slope=bias=rms=r2=0;if(n<3)return false;
            double mx=0,my=0;for(int i=0;i<n;i++){mx+=x[i];my+=y[i];}mx/=n;my/=n;
            double xx=0,xy=0,yy=0;for(int i=0;i<n;i++){xx+=(x[i]-mx)*(x[i]-mx);xy+=(x[i]-mx)*(y[i]-my);yy+=(y[i]-my)*(y[i]-my);}
            if(xx<=0||yy<=0)return false;slope=xy/xx;bias=my-slope*mx;
            double error=0;for(int i=0;i<n;i++)error+=Math.Pow(y[i]-(bias+slope*x[i]),2);
            rms=Math.Sqrt(error/n);r2=1-error/yy;return true;
        }
    }

    // Reads only token/model/quota metadata. Conversation lines are discarded after their small header.
    // An initial recent-file pass is followed by FileSystemWatcher-driven incremental reads.
    public sealed class LocalQuotaMeter:IDisposable
    {
        sealed class Cursor
        {
            public long Offset;public string Model,Speed,Root;public bool Skip;
            public readonly MemoryStream Line=new MemoryStream(2048);
        }
        readonly object gate=new object();
        readonly Dictionary<string,Cursor> cursors=new Dictionary<string,Cursor>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,UsagePoint> records=new Dictionary<string,UsagePoint>();
        readonly Dictionary<string,QuotaAnchor> anchors=new Dictionary<string,QuotaAnchor>();
        readonly HashSet<string> dirty=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
        readonly List<string> roots=new List<string>();
        static readonly Regex ModelField=new Regex(@"(?<!\\)""model""\s*:\s*""([^""\\]*)""",RegexOptions.Compiled);
        static readonly Regex SpeedField=new Regex(@"(?<!\\)""(?:service_tier|speed)""\s*:\s*""([^""\\]*)""",RegexOptions.Compiled);
        static readonly Regex RecordId=new Regex(@"(?<!\\)""response_id""\s*:\s*""([^""\\]+)""",RegexOptions.Compiled);
        static readonly Regex Stamp=new Regex(@"(?<!\\)""timestamp""\s*:\s*""([^""\\]+)""",RegexOptions.Compiled);
        static readonly Regex FlatUsage=new Regex(@"(?<!\\)""usage""\s*:\s*\{([^{}]*)\}",RegexOptions.Compiled);
        static readonly Regex UsageNumbers=new Regex(@"(?<!\\)""(input_tokens|cached_input_tokens|output_tokens)""\s*:\s*([0-9]+)",RegexOptions.Compiled);
        readonly Timer timer;
        readonly Action<EstimateView> callback;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly byte[] buffer=new byte[65536];
        static readonly Regex Kind=new Regex("\\\"type\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"",RegexOptions.Compiled);
        volatile bool disposed;
        public volatile bool Enabled=true;
        int running;bool first=true,changed=true;
        QuotaWindow official;
        long latestOfficial;
        DateTime lastFit=DateTime.MinValue,lastPrune=DateTime.MinValue;
        decimal? previousUsed;
        string previousEpoch;
        public LocalQuotaMeter(Action<EstimateView> onChange,bool enabled=true)
        {
            callback=onChange;Enabled=enabled;
            string home=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(String.IsNullOrWhiteSpace(home)||!Path.IsPathRooted(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            foreach(string name in new[]{"sessions","archived_sessions"})
            {
                string root=Path.Combine(home,name);if(!Directory.Exists(root))continue;roots.Add(root);
                try
                {
                    var watcher=new FileSystemWatcher(root,"*.jsonl"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size,InternalBufferSize=16384};
                    watcher.Changed+=delegate(object s,FileSystemEventArgs e){Queue(e.FullPath);};
                    watcher.Created+=delegate(object s,FileSystemEventArgs e){Queue(e.FullPath);};
                    watcher.Renamed+=delegate(object s,RenamedEventArgs e){Queue(e.FullPath);};
                    watcher.Error+=delegate{lock(gate){first=true;changed=true;}};
                    watcher.EnableRaisingEvents=true;watchers.Add(watcher);
                }
                catch{}
            }
            timer=new Timer(Poll,null,0,5000);
        }
        public void Observe(QuotaWindow w,DateTimeOffset fetched)
        {
            lock(gate)
            {
                official=w==null?null:new QuotaWindow{Id=w.Id,Name=w.Name,Used=w.Used,Reset=w.Reset,Minutes=w.Minutes,Fractional=w.Fractional};
                latestOfficial=fetched.ToUnixTimeMilliseconds();changed=true;
            }
        }
        void Queue(string path){lock(gate){if(dirty.Count<1024)dirty.Add(path);}}
        void Poll(object ignored)
        {
            if(disposed||!Enabled||Interlocked.Exchange(ref running,1)!=0)return;
            ThreadPriority priority=Thread.CurrentThread.Priority;
            try
            {
                Thread.CurrentThread.Priority=ThreadPriority.BelowNormal;
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool initial;
                lock(gate){initial=first;first=false;foreach(string p in dirty)paths.Add(p);dirty.Clear();}
                if(initial)
                {
                    foreach(string root in roots)
                    {
                        try{foreach(var f in new DirectoryInfo(root).EnumerateFiles("*.jsonl",SearchOption.AllDirectories).Where(f=>f.LastWriteTimeUtc>=DateTime.UtcNow.AddDays(-1)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(512))paths.Add(f.FullName);}catch{}
                    }
                }
                int read=0;
                foreach(string path in paths)
                {
                    if(disposed)return;
                    Read(path);read++;
                    if(initial&&read%20==0)Thread.Sleep(15);
                }
                if(DateTime.UtcNow-lastPrune>TimeSpan.FromMinutes(10)) {
                    long cutoff=DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
                    foreach(string key in records.Where(k=>k.Value.Time<cutoff).Select(k=>k.Key).ToList())records.Remove(key);
                    foreach(string key in anchors.Where(k=>k.Value.Time<cutoff).Select(k=>k.Key).ToList())anchors.Remove(key);
                    lastPrune=DateTime.UtcNow;
                }
                QuotaWindow window;long fetched;bool doFit;
                lock(gate){window=official;fetched=latestOfficial;doFit=changed||paths.Count>0;changed=false;}
                if(!doFit||disposed)return;
                if(!initial&&DateTime.UtcNow-lastFit<TimeSpan.FromSeconds(15)){lock(gate){changed=true;}return;}
                lastFit=DateTime.UtcNow;
                if(window!=null&&window.Reset.HasValue)
                {
                    string key="service:"+window.Id+":"+fetched;
                    anchors[key]=new QuotaAnchor{Id=window.Id,Reset=window.Reset.Value,Time=fetched,Used=window.Used};
                }
                var view=Calibration.Evaluate(records.Values.ToList(),anchors.Values.ToList(),window,DateTimeOffset.Now.ToUnixTimeMilliseconds());
                if(view.Available&&!view.Exact)
                {
                    string epoch=window.Id+":"+window.Reset;
                    decimal used=100-view.Remaining;
                    if(previousEpoch==epoch&&previousUsed.HasValue)used=Math.Min(window.Used+.49m,Math.Max(previousUsed.Value,used));
                    previousEpoch=epoch;previousUsed=used;view.Remaining=100-used;
                }
                else if(window!=null&&previousEpoch!=window.Id+":"+window.Reset){previousUsed=null;}
                WriteSummary(view);if(!disposed)callback(view);
            }
            catch
            {if(!disposed)callback(new EstimateView{State="本地记录暂不可读"});}
            finally{Thread.CurrentThread.Priority=priority;Volatile.Write(ref running,0);}
        }
        void Read(string path)
        {
            try
            {
                if(!File.Exists(path))return;
                Cursor cursor;if(!cursors.TryGetValue(path,out cursor)){cursor=new Cursor();cursors[path]=cursor;}
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,FileOptions.SequentialScan))
                {
                    if(file.Length<cursor.Offset){cursor.Offset=0;cursor.Model=null;cursor.Line.SetLength(0);}
                    file.Position=cursor.Offset;
                    int length;
                    while(!disposed&&(length=file.Read(buffer,0,buffer.Length))>0)
                    {
                        int start=0;
                        while(start<length)
                        {
                            int end=Array.IndexOf(buffer,(byte)10,start,length-start);
                            bool complete=end>=0;int count=(complete?end:length)-start;
                            if(!cursor.Skip)
                            {
                                if(cursor.Line.Length==0)
                                {
                                    string kind=HeaderKind(buffer,start,Math.Min(count,512));cursor.Root=kind;
                                    if(kind=="other")cursor.Skip=true;
                                    if(kind=="event_msg"&&(count>=512||complete)&&!AsciiContains(buffer,start,Math.Min(count,512),"token_count"))cursor.Skip=true;
                                }
                                if(!cursor.Skip)
                                {
                                    if(cursor.Root==null&&cursor.Line.Length>0)cursor.Root=HeaderKind(cursor.Line.GetBuffer(),0,(int)cursor.Line.Length);
                                    if(cursor.Root=="turn_context") {
                                        int keep=Math.Max(0,Math.Min(count,8192-(int)cursor.Line.Length));
                                        if(keep>0)cursor.Line.Write(buffer,start,keep);
                                    } else if(cursor.Line.Length+count>65536)cursor.Skip=true;
                                    else cursor.Line.Write(buffer,start,count);
                                }
                            }
                            if(complete)
                            {
                                if(!cursor.Skip&&cursor.Line.Length>0)Parse(Encoding.UTF8.GetString(cursor.Line.GetBuffer(),0,(int)cursor.Line.Length),cursor);
                                cursor.Line.SetLength(0);cursor.Skip=false;cursor.Root=null;
                            }
                            start=complete?end+1:length;
                        }
                    }
                    cursor.Offset=file.Position;
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        static bool AsciiAt(byte[] data,int at,int end,string value) {
            if(at+value.Length>end)return false;for(int i=0;i<value.Length;i++)if(data[at+i]!=(byte)value[i])return false;return true;
        }
        static bool AsciiContains(byte[] data,int start,int count,string value) {
            int end=start+count;for(int i=start;i+value.Length<=end;i++)if(AsciiAt(data,i,end,value))return true;return false;
        }
        internal static string HeaderKind(byte[] data,int start,int count) {
            int end=start+count;
            for(int i=start;i+6<end;i++) {
                if(!AsciiAt(data,i,end,"\"type\""))continue;
                int at=i+6;while(at<end&&data[at]==32)at++;
                if(at>=end||data[at++]!=58)return null;while(at<end&&data[at]==32)at++;
                if(at>=end||data[at++]!=34)return null;
                if(AsciiAt(data,at,end,"turn_context")&&at+12<end&&data[at+12]==34)return "turn_context";
                if(AsciiAt(data,at,end,"token_usage_record")&&at+18<end&&data[at+18]==34)return "token_usage_record";
                if(AsciiAt(data,at,end,"event_msg")&&at+9<end&&data[at+9]==34)return "event_msg";
                for(int j=at;j<end;j++)if(data[j]==34)return "other";
                return null;
            }return null;
        }
        static object Get(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
        static Dictionary<string,object> Dict(object o){return o as Dictionary<string,object>;}
        static double Number(object o){double n;return o!=null&&Double.TryParse(Convert.ToString(o,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!Double.IsInfinity(n)&&!Double.IsNaN(n)?Math.Max(0,n):0;}
        void Parse(string line,Cursor cursor)
        {
            if(cursor.Root=="turn_context"||(cursor.Root==null&&Kind.Match(line).Groups[1].Value=="turn_context")) {
                var model=ModelField.Match(line);var speed=SpeedField.Match(line);
                cursor.Model=model.Success?model.Groups[1].Value:null;cursor.Speed=speed.Success?speed.Groups[1].Value:null;return;
            }
            if(cursor.Root=="token_usage_record") {
                var idMatch=RecordId.Match(line);if(!idMatch.Success)return;
                string id=idMatch.Groups[1].Value;if(records.ContainsKey(id))return;
                var timestamp=Stamp.Match(line);DateTimeOffset at;
                if(!timestamp.Success||!DateTimeOffset.TryParse(timestamp.Groups[1].Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out at)||at<DateTimeOffset.UtcNow.AddDays(-1))return;
                var usageMatch=FlatUsage.Match(line);if(!usageMatch.Success)return;
                double input=0,cached=0,output=0;
                foreach(Match field in UsageNumbers.Matches(usageMatch.Groups[1].Value)) {
                    double value;if(!Double.TryParse(field.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out value))continue;
                    switch(field.Groups[1].Value){case "input_tokens":input=value;break;case "cached_input_tokens":cached=value;break;case "output_tokens":output=value;break;}
                }
                cached=Math.Min(cached,input);double[] rate=Rates(cursor.Model);
                double mode=cursor.Speed=="fast"||cursor.Speed=="priority"?2.5:cursor.Speed=="ultrafast"?8:1;
                records[id]=new UsagePoint{Time=at.ToUnixTimeMilliseconds(),Weight=rate==null?0:((input-cached)*rate[0]+cached*rate[1]+output*rate[2])*mode/1000000,UnknownTokens=rate==null?input+output:0,Tokens=input+output};
                return;
            }
            Dictionary<string,object> root;
            try{root=Dict(json.DeserializeObject(line));}catch{return;}
            if(root==null)return;
            string kind=Get(root,"type") as string;var payload=Dict(Get(root,"payload"));if(payload==null)return;
            if(kind=="turn_context"){cursor.Model=Get(payload,"model") as string;cursor.Speed=(Get(payload,"service_tier")??Get(payload,"speed")) as string;return;}
            DateTimeOffset when;if(!DateTimeOffset.TryParse(Get(root,"timestamp") as string,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out when))return;
            if(when<DateTimeOffset.UtcNow.AddDays(-1))return;long time=when.ToUnixTimeMilliseconds();
            if(kind=="token_usage_record")
            {
                string id=Get(payload,"response_id") as string;if(String.IsNullOrEmpty(id)||records.ContainsKey(id))return;
                var usage=Dict(Get(payload,"usage"));if(usage==null)return;
                double input=Number(Get(usage,"input_tokens")),cached=Math.Min(input,Number(Get(usage,"cached_input_tokens"))),output=Number(Get(usage,"output_tokens"));
                double[] rate=Rates(cursor.Model);double mode=cursor.Speed=="fast"||cursor.Speed=="priority"?2.5:cursor.Speed=="ultrafast"?8:1;
                records[id]=new UsagePoint{Time=time,Weight=rate==null?0:((input-cached)*rate[0]+cached*rate[1]+output*rate[2])*mode/1000000,UnknownTokens=rate==null?input+output:0,Tokens=input+output};
                return;
            }
            if(kind!="event_msg")return;
            var limits=Dict(Get(payload,"rate_limits"));if(limits==null)return;
            string bucket=Get(limits,"limit_id") as string;if(String.IsNullOrEmpty(bucket))bucket="codex";
            foreach(string part in new[]{"primary","secondary"})
            {
                var w=Dict(Get(limits,part));if(w==null||Get(w,"used_percent")==null||Get(w,"resets_at")==null)continue;
                double value=Number(Get(w,"used_percent"));if(value>100)continue;
                long reset=(long)Number(Get(w,"resets_at"));
                string key=bucket+":"+part+":"+time+":"+reset+":"+value.ToString(CultureInfo.InvariantCulture);
                anchors[key]=new QuotaAnchor{Id=bucket+":"+part,Time=time,Reset=reset,Used=(decimal)value,Raw=true};
            }
        }
        // Official Standard subscription credit weights, read 2026-10-27.
        // They are proxy features for empirical calibration, never an asserted conversion to included quota.
        static double[] Rates(string model)
        {
            switch(model)
            {
                case "gpt-6-astra":return new[]{250.0,25,1250};
                case "gpt-6.1-sol":return new[]{50.0,2.5,250};
                case "gpt-6.1-terra":return new[]{25.0,1.25,125};
                case "gpt-6-sol":return new[]{50.0,5,250};
                case "gpt-6-luna":return new[]{2.5,.25,12.5};
                case "gpt-5.6-sol":return new[]{100.0,10,500};
                case "gpt-5.6-terra":return new[]{50.0,5,300};
                case "gpt-5.6-luna":return new[]{5.0,.5,30};
                default:return null;
            }
        }
        void WriteSummary(EstimateView v)
        {
            try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"local-meter-status.json"),new JavaScriptSerializer().Serialize(new{
                available=v.Available,exact=v.Exact,state=v.State,responses=v.Responses,anchors=v.Anchors,rms=v.Rms,r2=v.R2,holdoutRms=v.HoldoutRms,unknownTokenShare=v.UnknownShare,
                estimatedRemaining=v.Available?(decimal?)QuotaMath.Display(v.Remaining):null,watchers=watchers.Count,files=cursors.Count,managedBytes=GC.GetTotalMemory(false),gen0=GC.CollectionCount(0),gen1=GC.CollectionCount(1),gen2=GC.CollectionCount(2)}));}catch{}
        }
        public void Dispose(){disposed=true;timer.Dispose();foreach(var watcher in watchers)watcher.Dispose();}
    }
}

