using System;
class P { static void Main(){
 var d=new DateTime[2]; d[0]=new DateTime(2020,1,2); Console.WriteLine("[p0-02] dt="+d[0].ToString("yyyy-MM-dd"));
 var t=new TimeSpan[1]; t[0]=TimeSpan.FromSeconds(90); Console.WriteLine("[p0-02] ts="+t[0].TotalSeconds);
 var g=new Guid[1]; g[0]=new Guid("00112233-4455-6677-8899-aabbccddeeff"); Console.WriteLine("[p0-02] guid="+g[0].ToString());
} }
