using System;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
class FakeCli{
 static byte[] cache;
 static int Main(){
  var json=new JavaScriptSerializer();string mode=Environment.GetEnvironmentVariable("QUOTA_CLOCK_FAKE_MODE")??"good";string line;
  while((line=Console.ReadLine())!=null){
   var map=json.Deserialize<Dictionary<string,object>>(line);object id;if(!map.TryGetValue("id",out id))continue;
   string method=(string)map["method"];
   if(method=="initialize"){Console.WriteLine(json.Serialize(new{id=id,result=new{}}));continue;}
   if(mode=="hang"){Thread.Sleep(Timeout.Infinite);return 3;}
   if(mode=="large"){cache=new byte[112*1024*1024];for(int i=0;i<cache.Length;i+=4096)cache[i]=1;}
   if(mode=="error"){Console.WriteLine(json.Serialize(new{id=id,error=new{code=-1,message="Synthetic test error"}}));continue;}
   Console.WriteLine("{\"method\":\"account/rateLimits/updated\",\"params\":{}}");
   Console.WriteLine("{\"id\":987654,\"result\":{}}");
   Console.WriteLine(json.Serialize(new{id=id,result=new{rateLimits=new{primary=new{usedPercent=26,windowDurationMins=300,resetsAt=DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds()}}}}));
   if(mode=="exit")return 0;
  }return 0;
 }
}
