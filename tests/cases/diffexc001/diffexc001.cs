using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
namespace Cases {
    public sealed class SM : IAsyncStateMachine {
        public int Moved;
        public void MoveNext() { Moved = Moved + 1; }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
    public static class Prog {
        static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
        static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
        static void BuilderProbe() {
            try { AsyncTaskMethodBuilder b = AsyncTaskMethodBuilder.Create(); I("exc.builder.create", 1); } catch (Exception) { G("exc.builder.create"); }
            try {
                AsyncTaskMethodBuilder b = AsyncTaskMethodBuilder.Create();
                Task t = b.Task;
                I("exc.builder.task.nonnull", (t != null) ? 1 : 0);
            } catch (Exception) { G("exc.builder.task"); }
            try {
                AsyncTaskMethodBuilder b = AsyncTaskMethodBuilder.Create();
                SM sm = new SM();
                b.SetStateMachine(sm);
                I("exc.builder.setstatemachine", 1);
            } catch (Exception) { G("exc.builder.setstatemachine"); }
            try {
                AsyncTaskMethodBuilder<int> bg = AsyncTaskMethodBuilder<int>.Create();
                Task<int> tg = bg.Task;
                I("exc.builder.generic.task.nonnull", (tg != null) ? 1 : 0);
            } catch (Exception) { G("exc.builder.generic"); }
        }
        static void AwaiterProbe() {
            try {
                Task<int> t = Task.FromResult(42);
                TaskAwaiter<int> aw = t.GetAwaiter();
                I("exc.task.generic.getawaiter.obtained", 1);
            } catch (Exception) { G("exc.task.generic.getawaiter.obtained"); }
        }
        public static void Main() {
            Console.WriteLine("-- shard exc001 start --");
            try { BuilderProbe(); } catch (Exception) { G("exc.builder"); }
            try { AwaiterProbe(); } catch (Exception) { G("exc.task"); }
            Console.WriteLine("-- shard exc001 end --");
        }
    }
}
