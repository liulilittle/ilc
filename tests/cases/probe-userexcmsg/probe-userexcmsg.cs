//------------------------------------------------------------------------------
// probe-userexcmsg.cs -- a USER-DEFINED exception must keep the message its
// constructor was given.
//
// The throw-site carrier is populated from the ctor arguments captured at the
// allocation, but the capture was gated on "is this a BCL exception name",
// which a user type can never satisfy. Every such throw therefore carried the
// placeholder text instead of the caller's message.
//
// Every line must match the net48 oracle byte for byte.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class MyError : Exception
    {
        public MyError(string message) : base(message) { }
    }

    // Two levels deep: the capture must follow the chain, not just the leaf.
    public sealed class MyInner : MyError
    {
        public MyInner(string message) : base(message) { }
    }

    // A user exception with no message argument: net48 answers the default
    // type-based text, so this guards the placeholder path.
    public sealed class MyBare : Exception
    {
        public MyBare() { }
    }

    public static class ProbeUserExcMsg
    {
        public static void Main()
        {
            try
            {
                throw new MyError("custom-detail");
            }
            catch (Exception ex)
            {
                Console.WriteLine("user.msg=" + ex.Message);
                Console.WriteLine("user.type=" + ex.GetType().FullName);
            }

            try
            {
                throw new MyInner("inner-detail");
            }
            catch (Exception ex)
            {
                Console.WriteLine("derived.msg=" + ex.Message);
                Console.WriteLine("derived.type=" + ex.GetType().FullName);
            }

            // A non-exception type must keep its own allocation path intact:
            // this is the guard against over-broadening the capture.
            object plain = new Plain("not-an-exception");
            Console.WriteLine("plain.msg=" + ((Plain)plain).Text);
        }
    }

    public sealed class Plain
    {
        public string Text { get; private set; }
        public Plain(string text) { Text = text; }
    }
}
