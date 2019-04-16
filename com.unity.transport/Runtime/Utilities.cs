using System;
using Unity.Collections;

namespace Unity.Networking.Transport.Utilities
{
    /// <summary>
    /// A NativeMultiQueue is a set of several FIFO queues split into buckets.
    /// Each bucket has its own first and last item and each bucket can have
    /// items pushed and popped individually.
    /// </summary>
    public struct NativeMultiQueue<T> : IDisposable where T : struct
    {
        private NativeList<T> m_Queue;
        private NativeList<int> m_QueueHeadTail;
        private NativeArray<int> m_MaxItems;

        /// <summary>
        /// New NativeMultiQueue has a single bucket and the specified number
        /// of items for that bucket. Accessing buckets out of range will grow
        /// the number of buckets and pushing more items than the initial capacity
        /// will increase the number of items for each bucket.
        /// </summary>
        public NativeMultiQueue(int initialMessageCapacity)
        {
            m_MaxItems = new NativeArray<int>(1, Allocator.Persistent);
            m_MaxItems[0] = initialMessageCapacity;
            m_Queue = new NativeList<T>(initialMessageCapacity, Allocator.Persistent);
            m_QueueHeadTail = new NativeList<int>(2, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_MaxItems.Dispose();
            m_Queue.Dispose();
            m_QueueHeadTail.Dispose();
        }

        /// <summary>
        /// Enqueue a new item to a specific bucket. If the bucket does not yet exist
        /// the number of buckets will be increased and if the queue is full the number
        /// of items for each bucket will be increased.
        /// </summary>
        public void Enqueue(int bucket, T value)
        {
            // Grow number of buckets to fit specified index
            if (bucket >= m_QueueHeadTail.Length / 2)
            {
                int oldSize = m_QueueHeadTail.Length;
                m_QueueHeadTail.ResizeUninitialized((bucket+1)*2);
                for (;oldSize < m_QueueHeadTail.Length; ++oldSize)
                    m_QueueHeadTail[oldSize] = 0;
                m_Queue.ResizeUninitialized((m_QueueHeadTail.Length / 2) * m_MaxItems[0]);
            }
            int idx = m_QueueHeadTail[bucket * 2 + 1];
            if (idx >= m_MaxItems[0])
            {
                // Grow number of items per bucket
                int oldMax = m_MaxItems[0];
                while (idx >= m_MaxItems[0])
                    m_MaxItems[0] = m_MaxItems[0]*2;
                int maxBuckets = m_QueueHeadTail.Length / 2;
                m_Queue.ResizeUninitialized(maxBuckets * m_MaxItems[0]);
                for (int b = maxBuckets-1; b >= 0; --b)
                {
                    for (int i = m_QueueHeadTail[b*2+1]-1; i >= m_QueueHeadTail[b * 2]; --i)
                    {
                        m_Queue[b * m_MaxItems[0] + i] = m_Queue[b * oldMax + i];
                    }
                }
            }
            m_Queue[m_MaxItems[0] * bucket + idx] = value;
            m_QueueHeadTail[bucket * 2 + 1] = idx + 1;
        }

        /// <summary>
        /// Dequeue an item from a specific bucket. If the bucket does not exist or if the
        /// bucket is empty the call will fail and return false.
        /// </summary>
        public bool Dequeue(int bucket, out T value)
        {
            if (bucket < 0 || bucket >= m_QueueHeadTail.Length / 2)
            {
                value = default(T);
                return false;
            }
            int idx = m_QueueHeadTail[bucket * 2];
            if (idx >= m_QueueHeadTail[bucket * 2 + 1])
            {
                m_QueueHeadTail[bucket * 2] = m_QueueHeadTail[bucket * 2 + 1] = 0;
                value = default(T);
                return false;
            }
            else if (idx + 1 == m_QueueHeadTail[bucket * 2 + 1])
            {
                m_QueueHeadTail[bucket * 2] = m_QueueHeadTail[bucket * 2 + 1] = 0;
            }
            else
            {
                m_QueueHeadTail[bucket * 2] = idx + 1;
            }

            value = m_Queue[m_MaxItems[0] * bucket + idx];
            return true;
        }

        /// <summary>
        /// Peek the next item in a specific bucket. If the bucket does not exist or if the
        /// bucket is empty the call will fail and return false.
        /// </summary>
        public bool Peek(int bucket, out T value)
        {
            if (bucket < 0 || bucket >= m_QueueHeadTail.Length / 2)
            {
                value = default(T);
                return false;
            }
            int idx = m_QueueHeadTail[bucket * 2];
            if (idx >= m_QueueHeadTail[bucket * 2 + 1])
            {
                value = default(T);
                return false;
            }

            value = m_Queue[m_MaxItems[0] * bucket + idx];
            return true;
        }

        /// <summary>
        /// Remove all items from a specific bucket. If the bucket does not exist
        /// the call will not do anything.
        /// </summary>
        public void Clear(int bucket)
        {
            if (bucket < 0 || bucket >= m_QueueHeadTail.Length / 2)
                return;
            m_QueueHeadTail[bucket * 2] = 0;
            m_QueueHeadTail[bucket * 2 + 1] = 0;
        }
    }

    /// <summary>
    /// A simple timer used to measure the number of milliseconds since it was created.
    /// </summary>
    public class Timer
    {
        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        public Timer()
        {
            stopwatch.Start();
        }

        public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;
    }

    public struct SequenceHelpers
    {
        // Calculate difference between the sequence IDs taking into account wrapping, so when you go from 65535 to 0 the distance is 1
        public static int AbsDistance(ushort lhs, ushort rhs)
        {
            int distance;
            if (lhs < rhs)
                distance = lhs + ushort.MaxValue + 1 - rhs;
            else
                distance = lhs - rhs;
            return distance;
        }
        
        public static bool IsNewer(uint current, uint old)
        {
            // Invert the check so same does not count as newer
            return !(old - current < (1u << 31));
        }

        public static bool GreaterThan16(ushort lhs, ushort rhs)
        {
            const uint max_sequence_divide_2 = 0x7FFF;
            return lhs > rhs && lhs - rhs <= (ushort) max_sequence_divide_2 ||
                   lhs < rhs && rhs - lhs > (ushort) max_sequence_divide_2;
        }

        public static bool LessThan16(ushort lhs, ushort rhs)
        {
            return GreaterThan16(rhs, lhs);
        }

        public static bool StalePacket(ushort sequence, ushort oldSequence, ushort windowSize)
        {
            return LessThan16(sequence, (ushort) (oldSequence - windowSize));
        }

        public static string BitMaskToString(uint mask)
        {
            //  31     24      16      8       0
            // |-------+-------+-------+--------|
            //  00000000000000000000000000000000

            var bits = sizeof(uint) * 8;
            var sb = new System.Text.StringBuilder(new string('*', bits));

            for (int i = bits - 1; i >= 0; i--)
            {
                sb[i] = (mask & 1) != 0 ? '1' : '0';
                mask >>= 1;
            }

            return sb.ToString();
        }
    }
}