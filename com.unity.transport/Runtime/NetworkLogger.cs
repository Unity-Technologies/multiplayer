using System;
using Unity.Collections;

namespace Unity.Networking.Transport
{
    public interface INetworkLogMessage
    {
        void Print(ref NetworkLogString container);
    }

    public unsafe struct NetworkLogString
    {
        public NetworkLogString(string str)
        {
            m_Length = 0;
            fixed (ushort* dst = m_Message)
            {
                fixed (char* src = str)
                {
                    for (int i = 0; src[i] != '\0'; ++i)
                        dst[m_Length++] = src[i];
                }

                dst[m_Length] = '\0';
            }
        }
        private fixed ushort m_Message[512];
        private int m_Length;
        public void Append(ref NetworkLogString str)
        {
            if (m_Length + str.m_Length >= 512)
                throw new InvalidOperationException("String cannot fit in buffer");
            fixed (ushort* dst = m_Message)
            {
                fixed (ushort* src = str.m_Message)
                {
                    for (int i = 0; i < str.m_Length; ++i)
                        dst[m_Length++] = src[i];
                    dst[m_Length] = '\0';
                }
            }
        }
        public void AppendSpace()
        {
            if (m_Length + 1 >= 512)
                throw new InvalidOperationException("String cannot fit in buffer");
            fixed (ushort* dst = m_Message)
            {
                dst[m_Length++] = ' ';
                dst[m_Length] = '\0';
            }
        }
        public void AppendComma()
        {
            if (m_Length + 1 >= 512)
                throw new InvalidOperationException("String cannot fit in buffer");
            fixed (ushort* dst = m_Message)
            {
                dst[m_Length++] = ',';
                dst[m_Length] = '\0';
            }
        }
        public void AppendInt(int val)
        {
            int digits = 1;
            int maxval = 10;
            bool isneg = val < 0;
            if (isneg)
                val = -val;
            while (val > maxval)
            {
                ++digits;
                maxval *= 10;
            }
            if (m_Length + digits + (isneg?1:0) >= 512)
                throw new InvalidOperationException("Int cannot fit in buffer");
            fixed (ushort* dst = m_Message)
            {
                if (isneg)
                    dst[m_Length++] = '-';
                while (maxval > 1)
                {
                    maxval /= 10;
                    dst[m_Length++] = (ushort)('0' + (val/maxval));
                    val = val % maxval;
                }
                dst[m_Length] = '\0';
            }
        }

        public string AsString()
        {
            string temp;
            fixed (ushort* dst = m_Message)
                temp = new string((char*)dst);
            return temp;
        }
    }
    public struct NetworkLogger : IDisposable
    {
        internal struct LogMessage
        {
            public NetworkLogString msg;
            public LogLevel level;
        }
        public enum LogLevel
        {
            None = 0,
            Error,
            Warning,
            Info,
            Debug
        }

        public NetworkLogger(LogLevel level)
        {
            m_Level = level;
            m_PendingLog = new NativeQueue<LogMessage>(Allocator.Persistent);
            m_LogFile = new NativeList<LogMessage>(Allocator.Persistent);
        }

        public void Dispose()
        {
            m_PendingLog.Dispose();
            m_LogFile.Dispose();
        }

        public void FlushPending()
        {
            LogMessage msg;
            while (m_PendingLog.TryDequeue(out msg))
                m_LogFile.Add(msg);
        }

        public void DumpToConsole()
        {
            for (int i = 0; i < m_LogFile.Length; ++i)
            {
                var msg = m_LogFile[i];
                switch (msg.level)
                {
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(msg.msg.AsString());
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(msg.msg.AsString());
                    break;
                default:
                    UnityEngine.Debug.Log(msg.msg.AsString());
                    break;
                }
            }
            m_LogFile.Clear();
        }
        public void Clear()
        {
            m_LogFile.Clear();
        }

        public void Log<T>(LogLevel level, T message) where T : struct, INetworkLogMessage
        {
            if ((int) level > (int) m_Level)
                return;
            var msg = default(LogMessage);
            msg.level = level;
            message.Print(ref msg.msg);
            m_PendingLog.Enqueue(msg);
        }
        public void Log(LogLevel level, NetworkLogString str)
        {
            if ((int) level > (int) m_Level)
                return;
            var msg = new LogMessage {level = level, msg = str};
            m_PendingLog.Enqueue(msg);
        }

        private LogLevel m_Level;
        private NativeList<LogMessage> m_LogFile;
        private NativeQueue<LogMessage> m_PendingLog;

        public Concurrent ToConcurrent()
        {
            var concurrent = default(Concurrent);
            concurrent.m_PendingLog = m_PendingLog.ToConcurrent();
            concurrent.m_Level = m_Level;
            return concurrent;
        }

        public struct Concurrent
        {
            public void Log<T>(LogLevel level, T message) where T : struct, INetworkLogMessage
            {
                if ((int) level > (int) m_Level)
                    return;
                var msg = default(LogMessage);
                msg.level = level;
                message.Print(ref msg.msg);
                m_PendingLog.Enqueue(msg);
            }
            public void Log(LogLevel level, NetworkLogString str)
            {
                if ((int) level > (int) m_Level)
                    return;
                var msg = new LogMessage {level = level, msg = str};
                m_PendingLog.Enqueue(msg);
            }

            internal NativeQueue<LogMessage>.Concurrent m_PendingLog;
            internal LogLevel m_Level;
        }
    }
}