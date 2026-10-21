using System;
class P { static void Main(){
 var b=new bool[4]; b[0]=true; b[2]=true;
 Console.WriteLine("[b] "+b[0]+","+b[1]+","+b[2]+","+b[3]);
 var src=new DateTime[]{
  new DateTime(2020,1,2,3,4,5,DateTimeKind.Unspecified),
  new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc),
  new DateTime(2020,1,2,3,4,5,DateTimeKind.Local)};
 var dst=new DateTime[3]; for(int i=0;i<3;i++) dst[i]=src[i];
 for(int i=0;i<3;i++) Console.WriteLine("[ft] "+dst[i].ToFileTimeUtc());
 var ga=new Guid[2]; ga[0]=new Guid("00112233-4455-6677-8899-aabbccddeeff");
 Guid g0=ga[0]; Guid g1=ga[1];
 Console.WriteLine("[g] "+g0+" empty="+(g1==Guid.Empty));
}}
