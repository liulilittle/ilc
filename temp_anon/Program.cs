using System;
class P {
 static void Main(){
   var a = new { message = 42 };
   var b = new { message = "hello" };
   Console.WriteLine(a.ToString());
   Console.WriteLine(b.ToString());
   Console.WriteLine(a.Equals(b));
   Console.WriteLine(a.GetHashCode());
 }
}
