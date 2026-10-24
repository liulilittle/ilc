using System;
class N {
  static string FromCp(int cp){
    if(cp<=0xFFFF) return new string((char)cp, 1);
    cp-=0x10000;
    char[] a = new char[2];
    a[0] = (char)(0xD800 + (cp >> 10));
    a[1] = (char)(0xDC00 + (cp & 0x3FF));
    return new string(a);
  }
  static string H(string s){
    string r = "";
    for(int i=0;i<s.Length;i++){
      int cp = s[i];
      if(s[i] >= 0xD800 && s[i] <= 0xDBFF && i+1 < s.Length && s[i+1] >= 0xDC00 && s[i+1] <= 0xDFFF){
        cp = 0x10000 + ((cp - 0xD800) << 10) + (s[i+1] - 0xDC00);
        i++;
      }
      if(r.Length>0) r += " ";
      r += cp.ToString("X5");
    }
    return r;
  }
  static string B(bool v){ return v ? "T" : "F"; }
  static string Norm(string s, int f){
    try {
      if(f==1) return H(s.Normalize(System.Text.NormalizationForm.FormC));
      if(f==2) return H(s.Normalize(System.Text.NormalizationForm.FormD));
      if(f==5) return H(s.Normalize(System.Text.NormalizationForm.FormKC));
      return H(s.Normalize(System.Text.NormalizationForm.FormKD));
    } catch(Exception e){ return "EX:" + e.GetType().Name; }
  }
  static string IsNorm(string s, int f){
    try {
      if(f==1) return B(s.IsNormalized(System.Text.NormalizationForm.FormC));
      if(f==2) return B(s.IsNormalized(System.Text.NormalizationForm.FormD));
      if(f==5) return B(s.IsNormalized(System.Text.NormalizationForm.FormKC));
      return B(s.IsNormalized(System.Text.NormalizationForm.FormKD));
    } catch(Exception e){ return "EX:" + e.GetType().Name; }
  }
  static string Line(string s){
    return " C=" + Norm(s,1) + " D=" + Norm(s,2) + " KC=" + Norm(s,5) + " KD=" + Norm(s,6)
         + " ISC=" + IsNorm(s,1) + " ISD=" + IsNorm(s,2) + " ISKC=" + IsNorm(s,5) + " ISKD=" + IsNorm(s,6);
  }
  static void Main(){
    for(int cp=0; cp<0x110000; cp++){
      if(cp>=0xD800 && cp<=0xDFFF) continue;
      if(cp>0xFFFF && (cp & 63)!=0) continue;
      Console.WriteLine("U+" + cp.ToString("X4") + Line(FromCp(cp)));
    }
    string[] probes = {
      "a\u0300\u0327","\u031B\u0323\u0300","\u0915\u093C","\u0F71\u0F72\u0F73","\u0041\u030A",
      "\uAC00","\u1100\u1161","\u1100\u1161\u11A8","\u1100\u1161\u11A8\u0300","\u1E9B","\uFB01",
      "\u1F80\u0345","\u0344","\u212B","\u2126","\u212A","\u00DF","\u0130","\u0149","\u03C2",
      "\u1E69\u0327","\u0338\u0301","\u00C5\u0300","\u1EA7","\u1EA5","\u1E09",
      "\uD83D\uDE00\uFDD0","a\uFDD0b","\uFDD0\uD83D\uDE00",
      "\u031B\u0323\u0300\u0327","\u0344\u0300","\u0915\u093C\u094D","\u1100\u1161\u11A8\u0300\u0327",
      "\u0F71\u0F72\u0F80","\uFB01\uFB02","\u1F80\u0345\u0300","\uD83D\uDE00\uD83D\uDE01"
    };
    foreach(string p in probes) Console.WriteLine("S " + H(p) + Line(p));
  }
}
