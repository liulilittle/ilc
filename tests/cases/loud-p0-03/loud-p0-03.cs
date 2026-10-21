using System;
class P { static void Main(){
 var a=new decimal[]{1.5m,2.5m}; Console.WriteLine("[p0-03] dec="+a[0]+","+a[1]+",len="+a.Length);
 var g=new Guid[]{new Guid("00112233-4455-6677-8899-aabbccddeeff")}; Console.WriteLine("[p0-03] guid="+g[0]);
} }
