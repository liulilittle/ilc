//------------------------------------------------------------------------------
// stackqcap.cs -- the Stack(int)/Queue(int) capacity ctors wired by the
// impl-misc batch (net48 reference oracle):
//
//   * Stack(int capacity) / Queue(int capacity) -- the net48 capacity ctors
//     (il2cpp_stack_create_with_capacity / il2cpp_queue_create_with_capacity).
//     net48 allocates exactly 'capacity' slots and doubles on overflow (first
//     grow target 4). The runtime clamps capacity < 4 up to the 4-slot minimum
//     (the doubling growth cannot extend a zero-length array).
//   * A negative capacity raises ArgumentOutOfRangeException in both.
//
// Every line prints "label=value". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Text;

namespace Cases
{
    public static class StackQCap
    {
        public static void Main()
        {
            StackCtorCapacity();
            QueueCtorCapacity();
            NegativeCapacityThrows();
        }

        static void StackCtorCapacity()
        {
            // capacity 0 -> clamped to 4; five pushes cross one growth.
            Stack s0 = new Stack(0);
            for (int i = 0; i < 5; i++) s0.Push(i);
            Console.WriteLine("st0.count=" + s0.Count);
            Console.WriteLine("st0.pop=" + PopN(s0, 5));

            // capacity 3 -> grows to 6 on the 4th push.
            Stack s3 = new Stack(3);
            for (int i = 0; i < 6; i++) s3.Push(i * 10);
            Console.WriteLine("st3.count=" + s3.Count);
            Console.WriteLine("st3.pop=" + PopN(s3, 6));

            // capacity 10 -> ten pushes fit, the 11th grows.
            Stack s10 = new Stack(10);
            for (int i = 0; i < 11; i++) s10.Push(i);
            Console.WriteLine("st10.count=" + s10.Count);
            Console.WriteLine("st10.pop=" + PopN(s10, 11));

            // capacity 100 -> no growth.
            Stack s100 = new Stack(100);
            for (int i = 0; i < 50; i++) s100.Push(i);
            Console.WriteLine("st100.count=" + s100.Count);
            Console.WriteLine("st100.top=" + s100.Peek());
        }

        static void QueueCtorCapacity()
        {
            Queue q0 = new Queue(0);
            for (int i = 0; i < 5; i++) q0.Enqueue(i);
            Console.WriteLine("q0.count=" + q0.Count);
            Console.WriteLine("q0.deq=" + DeqN(q0, 5));

            Queue q3 = new Queue(3);
            for (int i = 0; i < 6; i++) q3.Enqueue(i * 10);
            Console.WriteLine("q3.count=" + q3.Count);
            Console.WriteLine("q3.deq=" + DeqN(q3, 6));

            Queue q10 = new Queue(10);
            for (int i = 0; i < 11; i++) q10.Enqueue(i);
            Console.WriteLine("q10.count=" + q10.Count);
            Console.WriteLine("q10.deq=" + DeqN(q10, 11));

            // capacity 100 with a dequeue/enqueue ring walk.
            Queue q100 = new Queue(100);
            for (int i = 0; i < 50; i++) q100.Enqueue(i);
            Console.WriteLine("q100.count=" + q100.Count);
            Console.WriteLine("q100.front=" + q100.Peek());
        }

        static void NegativeCapacityThrows()
        {
            try { new Stack(-1); Console.WriteLine("stneg=no"); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("stneg=ArgumentOutOfRange"); }

            try { new Queue(-1); Console.WriteLine("qneg=no"); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("qneg=ArgumentOutOfRange"); }
        }

        static string PopN(Stack s, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Append(s.Pop());
                if (i + 1 < n) sb.Append(",");
            }
            return sb.ToString();
        }

        static string DeqN(Queue q, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Append(q.Dequeue());
                if (i + 1 < n) sb.Append(",");
            }
            return sb.ToString();
        }
    }
}
