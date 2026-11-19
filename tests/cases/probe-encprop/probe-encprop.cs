using System;
using System.Text;

class ProbeEncProp
{
    static void W(string k, object v) { Console.WriteLine(k + "=" + v); }

    static void Main()
    {
        W("utf8_an_C", Encoding.UTF8.IsAlwaysNormalized(NormalizationForm.FormC));
        W("utf8_an_D", Encoding.UTF8.IsAlwaysNormalized(NormalizationForm.FormD));
        W("utf8_an_KC", Encoding.UTF8.IsAlwaysNormalized(NormalizationForm.FormKC));
        W("utf8_an_KD", Encoding.UTF8.IsAlwaysNormalized(NormalizationForm.FormKD));
        W("ascii_an_C", Encoding.ASCII.IsAlwaysNormalized(NormalizationForm.FormC));
        W("ascii_an_D", Encoding.ASCII.IsAlwaysNormalized(NormalizationForm.FormD));
        W("ascii_an_KC", Encoding.ASCII.IsAlwaysNormalized(NormalizationForm.FormKC));
        W("ascii_an_KD", Encoding.ASCII.IsAlwaysNormalized(NormalizationForm.FormKD));

        byte[] src = Encoding.Unicode.GetBytes("abc");
        byte[] dst = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, src);
        W("conv3_len", dst.Length);
        W("conv3_hex", BitConverter.ToString(dst));
        byte[] dst2 = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, src, 2, 2);
        W("conv5_len", dst2.Length);
        W("conv5_hex", BitConverter.ToString(dst2));
        byte[] dst3 = Encoding.Convert(Encoding.ASCII, Encoding.UTF8, new byte[] { 65, 66 });
        W("conv_ascii_hex", BitConverter.ToString(dst3));
        byte[] dst4 = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, new byte[] { 65, 66, 67 });
        W("conv_u16_hex", BitConverter.ToString(dst4));
    }
}