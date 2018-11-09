﻿using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Unity.Networking.Transport.LowLevel.Unsafe;

namespace Unity.Networking.Transport
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntFloat
    {
        [FieldOffset(0)] public float floatValue;

        [FieldOffset(0)] public uint intValue;

        [FieldOffset(0)] public double doubleValue;

        [FieldOffset(0)] public ulong longValue;
    }

    /// <summary>
    /// Data streams can be used to serialize data over the network. The
    /// <c>DataStreamWriter</c> and <c>DataStreamReader</c> classes work together
    /// to serialize data for sending and then to deserialize when receiving.
    /// </summary>
    /// <remarks>
    /// The reader can be used to deserialize the data from a writer, writing data
    /// to a writer and reading it back can be done like this:
    /// <code>
    /// using (var dataWriter = new DataStreamWriter(16, Allocator.Persistent))
    /// {
    ///     dataWriter.Write(42);
    ///     dataWriter.Write(1234);
    ///     // Length is the actual amount of data inside the writer,
    ///     // Capacity is the total amount.
    ///     var dataReader = new DataStreamReader(dataWriter, 0, dataWriter.Length);
    ///     var context = default(DataStreamReader.Context);
    ///     var myFirstInt = dataReader.ReadInt(ref context);
    ///     var mySecondInt = dataReader.ReadInt(ref context);
    /// }
    /// </code>
    ///
    /// The writer needs to be Disposed (here done by wrapping usage in using statement)
    /// because it uses native memory which needs to be freed.
    ///
    /// There are a number of functions for various data types. Each write call
    /// returns a <c>Deferred*</c> variant for that particular type and this can be used
    /// as a marker to overwrite the data later on, this is particularly useful when
    /// the size of the data is written at the start and you want to write it at
    /// the end when you know the value.
    ///
    /// <code>
    /// using (var data = new DataStreamWriter(16, Allocator.Persistent))
    /// {
    ///     // My header data
    ///     var headerSizeMark = data.Write((ushort)0);
    ///     var payloadSizeMark = data.Write((ushort)0);
    ///     data.Write(42);
    ///     data.Write(1234);
    ///     var headerSize = data.Length;
    ///     // Update header size to correct value
    ///     headerSizeMark.Update((ushort)headerSize);
    ///     // My payload data
    ///     byte[] someBytes = Encoding.ASCII.GetBytes("some string");
    ///     data.Write(someBytes, someBytes.Length);
    ///     // Update payload size to correct value
    ///     payloadSizeMark.Update((ushort)(data.Length - headerSize));
    /// }
    /// </code>
    ///
    /// It's possible to get a more direct access to the buffer inside the
    /// reader/writer, in an unsafe way. See <see cref="DataStreamUnsafeUtility"/>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct DataStreamWriter : IDisposable
    {
        public struct DeferredByte
        {
            public void Update(byte value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredShort
        {
            public void Update(short value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredUShort
        {
            public void Update(ushort value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredInt
        {
            public void Update(int value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredUInt
        {
            public void Update(uint value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredFloat
        {
            public void Update(float value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.Write(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredShortNetworkByteOrder
        {
            public void Update(short value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.WriteNetworkByteOrder(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredUShortNetworkByteOrder
        {
            public void Update(ushort value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.WriteNetworkByteOrder(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredIntNetworkByteOrder
        {
            public void Update(int value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.WriteNetworkByteOrder(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }
        public struct DeferredUIntNetworkByteOrder
        {
            public void Update(uint value)
            {
                int oldOffset = m_writer.m_Data->length;
                m_writer.m_Data->length = m_offset;
                m_writer.WriteNetworkByteOrder(value);
                m_writer.m_Data->length = oldOffset;
            }

            internal DataStreamWriter m_writer;
            internal int m_offset;
        }

        internal struct StreamData
        {
            public byte* buffer;
            public int length;
            public int capacity;
        }
        
        [NativeDisableUnsafePtrRestriction] internal StreamData* m_Data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule] internal DisposeSentinel m_DisposeSentinel;
#endif

        Allocator m_Allocator;

        public DataStreamWriter(int capacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_Data = (StreamData*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<StreamData>(), UnsafeUtility.AlignOf<StreamData>(), m_Allocator);
            m_Data->capacity = capacity;
            m_Data->length = 0;
            m_Data->buffer = (byte*) UnsafeUtility.Malloc(capacity, UnsafeUtility.AlignOf<byte>(), m_Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, m_Allocator);
#else
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1);
#endif
#endif
        }

        /// <summary>
        /// True if there is a valid data buffer present. This would be false
        /// if the writer was created with no arguments.
        /// </summary>
        public bool IsCreated
        {
            get { return m_Data != null; }
        }

        /// <summary>
        /// The total size of the data buffer, see <see cref="Length"/> for
        /// the size of space used in the buffer. Capacity can be
        /// changed after the writer has been created.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the given
        /// capacity is smaller than the current buffer usage.</exception>
        public int Capacity
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return m_Data->capacity;
            }
            set
            {
                if (m_Data->capacity == value)
                    return;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
                if (m_Data->length > value)
                    throw new InvalidOperationException("Cannot shrink a data stream to be shorter than the current data in it");
#endif
                byte* newbuf = (byte*) UnsafeUtility.Malloc(value, UnsafeUtility.AlignOf<byte>(), m_Allocator);
                UnsafeUtility.MemCpy(newbuf, m_Data->buffer, m_Data->length);
                UnsafeUtility.Free(m_Data->buffer, m_Allocator);
                m_Data->buffer = newbuf;
                m_Data->capacity = value;
            }
        }
        
        /// <summary>
        /// The size of the buffer used. See <see cref="Capacity"/> for the total size.
        /// </summary>
        public int Length
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return m_Data->length;
            }
        }

        /// <summary>
        /// The writer uses unmanaged memory for its data buffer. Dispose
        /// needs to be called to free this resource.
        /// </summary>
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#if UNITY_2018_3_OR_NEWER
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#else
            DisposeSentinel.Dispose(m_Safety, ref m_DisposeSentinel);
#endif
#endif
            UnsafeUtility.Free(m_Data->buffer, m_Allocator);
            UnsafeUtility.Free(m_Data, m_Allocator);
            m_Data = (StreamData*) 0;
        }

        /// <summary>
        /// Copy data from the writer to the given NativeArray, the data size
        /// (start to length) must not exceed the total size of the array or
        /// an exception will be thrown.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <param name="dest"></param>
        public void CopyTo(int start, int length, NativeArray<byte> dest)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            ValidateSizeParameters(start, length, dest.Length);
#endif

            void* dstPtr = dest.GetUnsafePtr();
            UnsafeUtility.MemCpy(dstPtr, m_Data->buffer + start, length);
        }

        /// <summary>
        /// Copy data from the writer to the given managed byte array, the
        /// data size (start to length) must not exceed the total size of the
        /// byte array or an exception will be thrown.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <param name="dest"></param>
        public void CopyTo(int start, int length, ref byte[] dest)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            ValidateSizeParameters(start, length, dest.Length);
#endif

            fixed (byte* ptr = dest)
            {
                UnsafeUtility.MemCpy(ptr, m_Data->buffer + start, length);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        void ValidateSizeParameters(int start, int length, int dstLength)
        {
            if (start < 0 || length + start > m_Data->length)
                throw new ArgumentOutOfRangeException("start+length",
                    "The sum of start and length can not be larger than the data buffer Length");

            if (length > dstLength)
                throw new ArgumentOutOfRangeException("length", "Length must be <= than the length of the destination");

            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
        }
#endif

        public void WriteBytes(byte* data, int bytes)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (m_Data->length + bytes > m_Data->capacity)
                throw new System.ArgumentOutOfRangeException();
#endif
            UnsafeUtility.MemCpy(m_Data->buffer + m_Data->length, data, bytes);
            m_Data->length += bytes;
        }

        public DeferredByte Write(byte value)
        {
            var ret = new DeferredByte {m_writer = this, m_offset = m_Data->length};
            WriteBytes((byte*) &value, sizeof(byte));
            return ret;
        }

        /// <summary>
        /// Copy byte array into the writers data buffer, up to the
        /// given length or the complete size if no length (or -1) is given.
        /// </summary>
        /// <param name="value">Source byte array</param>
        /// <param name="length">Length to copy, omit this to copy all the byte array</param>
        public void Write(byte[] value, int length = -1)
        {
            if (length < 0)
                length = value.Length;
            
            fixed (byte* p = value)
            {
                WriteBytes(p, length);
            }
        }

        public DeferredShort Write(short value)
        {
            var ret = new DeferredShort {m_writer = this, m_offset = m_Data->length};
            WriteBytes((byte*) &value, sizeof(short));
            return ret;
        }

        public DeferredUShort Write(ushort value)
        {
            var ret = new DeferredUShort {m_writer = this, m_offset = m_Data->length};
            WriteBytes((byte*) &value, sizeof(ushort));
            return ret;
        }

        public DeferredInt Write(int value)
        {
            var ret = new DeferredInt {m_writer = this, m_offset = m_Data->length};
            WriteBytes((byte*) &value, sizeof(int));
            return ret;
        }

        public DeferredUInt Write(uint value)
        {
            var ret = new DeferredUInt {m_writer = this, m_offset = m_Data->length};
            WriteBytes((byte*) &value, sizeof(uint));
            return ret;
        }

        public DeferredShortNetworkByteOrder WriteNetworkByteOrder(short value)
        {
            var ret = new DeferredShortNetworkByteOrder {m_writer = this, m_offset = m_Data->length};
            short netValue = System.Net.IPAddress.HostToNetworkOrder(value);
            WriteBytes((byte*) &netValue, sizeof(short));
            return ret;
        }

        public DeferredUShortNetworkByteOrder WriteNetworkByteOrder(ushort value)
        {
            var ret = new DeferredUShortNetworkByteOrder {m_writer = this, m_offset = m_Data->length};
            WriteNetworkByteOrder((short) value);
            return ret;
        }

        public DeferredIntNetworkByteOrder WriteNetworkByteOrder(int value)
        {
            var ret = new DeferredIntNetworkByteOrder {m_writer = this, m_offset = m_Data->length};
            int netValue = System.Net.IPAddress.HostToNetworkOrder(value);
            WriteBytes((byte*) &netValue, sizeof(int));
            return ret;
        }

        public DeferredUIntNetworkByteOrder WriteNetworkByteOrder(uint value)
        {
            var ret = new DeferredUIntNetworkByteOrder {m_writer = this, m_offset = m_Data->length};
            WriteNetworkByteOrder((int)value);
            return ret;
        }

        public DeferredFloat Write(float value)
        {
            var ret = new DeferredFloat {m_writer = this, m_offset = m_Data->length};
            UIntFloat uf = new UIntFloat();
            uf.floatValue = value;
            Write((int) uf.intValue);
            return ret;
        }

        /// <summary>
        /// Moves the write position to the start of the data buffer used.
        /// </summary>
        public void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            m_Data->length = 0;
        }
    }

    /// <summary>
    /// The <c>DataStreamReader</c> class is the counterpart of the
    /// <c>DataStreamWriter</c> class and can be be used to deserialize
    /// data which was prepared with it.
    /// </summary>
    /// <remarks>
    /// Simple usage example:
    /// <code>
    /// using (var dataWriter = new DataStreamWriter(16, Allocator.Persistent))
    /// {
    ///     dataWriter.Write(42);
    ///     dataWriter.Write(1234);
    ///     // Length is the actual amount of data inside the writer,
    ///     // Capacity is the total amount.
    ///     var dataReader = new DataStreamReader(dataWriter, 0, dataWriter.Length);
    ///     var context = default(DataStreamReader.Context);
    ///     var myFirstInt = dataReader.ReadInt(ref context);
    ///     var mySecondInt = dataReader.ReadInt(ref context);
    /// }
    /// </code>
    ///
    /// The <c>DataStreamReader.Context</c> passed to all the <c>Read*</c> functions
    /// carries the position of the read pointer inside the buffer inside, this can be
    /// used with the job system but normally you start by creating
    /// a default context like the example above shows.
    /// 
    /// See the <see cref="DataStreamWriter"/> class for more information
    /// and examples.
    /// </remarks>
    public unsafe struct DataStreamReader
    {
        /// <summary>
        /// The context is the current index to the data buffer used by the
        /// reader. This can be used when the reader is used with the job system but normally
        /// the same context is passed in for each read invocation so the correct
        /// read index is used:
        /// <code>
        /// var dataReader = new DataStreamReader(someDataWriter, 0, someDataWriter.Length);
        /// var ctx = default(DataStreamReader.Context);
        /// var someInt = dataReader.ReadInt(ref ctx);
        /// var someOtherInt = dataReader.ReadInt(ref ctx);
        /// </code>
        /// </summary>
        public struct Context
        {
            internal int m_ReadByteIndex;
        }

        internal byte* m_bufferPtr;
        int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        
        public DataStreamReader(DataStreamWriter writer, int offset, int length)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (offset + length > writer.Length)
                throw new System.ArgumentOutOfRangeException();
            m_Safety = writer.m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, false);
#endif
            m_bufferPtr = writer.GetUnsafeReadOnlyPtr() + offset;
            m_Length = length;
        }

        /// <summary>
        /// The total size of the buffer space this reader is working with.
        /// </summary>
        public int Length
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return m_Length;
            }
        }

        /// <summary>
        /// True if the reader has been pointed to a valid buffer space. This
        /// would be false if the reader was created with no arguments.
        /// </summary>
        public bool IsCreated
        {
            get { return m_bufferPtr != null; }
        }

        /// <summary>
        /// Read and copy data to the memory location pointed to, an exception will
        /// be thrown if it does not fit.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="data"></param>
        /// <param name="length"></param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the length
        /// will put the reader out of bounds based on the current read pointer
        /// position.</exception>
        public void ReadBytes(ref Context ctx, byte* data, int length)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            if (GetBytesRead(ref ctx) + length > m_Length)
            {
                throw new System.ArgumentOutOfRangeException();
            }
#endif
            UnsafeUtility.MemCpy(data, m_bufferPtr + ctx.m_ReadByteIndex, length);
            ctx.m_ReadByteIndex += length;
        }

        /// <summary>
        /// Read and copy data into the given managed byte array, an exception will
        /// be thrown if it does not fit.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="dest"></param>
        /// <param name="length"></param>
        public void ReadBytesIntoArray(ref Context ctx, byte[] dest, int length)
        {
            for (var i = 0; i < length; ++i)
                dest[i] = ReadByte(ref ctx);
        }

        /// <summary>
        /// Create a new byte array and read the given length of bytes into it.
        /// </summary>
        /// <param name="ctx">Current reader context</param>
        /// <param name="length">Amount of bytes to read.</param>
        /// <returns>Newly created byte array with the contents.</returns>
        public byte[] ReadBytesAsArray(ref Context ctx, int length)
        {
            var array = new byte[length];
            for (var i = 0; i < array.Length; ++i)
                array[i] = ReadByte(ref ctx);
            return array;
        }

        public int GetBytesRead(ref Context ctx)
        {
            return ctx.m_ReadByteIndex;
        }

        public byte ReadByte(ref Context ctx)
        {
            byte data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(byte));
            return data;
        }

        public short ReadShort(ref Context ctx)
        {
            short data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(short));
            return data;
        }

        public ushort ReadUShort(ref Context ctx)
        {
            ushort data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(ushort));
            return data;
        }

        public int ReadInt(ref Context ctx)
        {
            int data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(int));
            return data;
        }

        public uint ReadUInt(ref Context ctx)
        {
            uint data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(uint));
            return data;
        }

        public short ReadShortNetworkByteOrder(ref Context ctx)
        {
            short data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(short));
            return System.Net.IPAddress.NetworkToHostOrder(data);
        }

        public ushort ReadUShortNetworkByteOrder(ref Context ctx)
        {
            return (ushort) ReadShortNetworkByteOrder(ref ctx);
        }

        public int ReadIntNetworkByteOrder(ref Context ctx)
        {
            int data;
            ReadBytes(ref ctx, (byte*) &data, sizeof(int));
            return System.Net.IPAddress.NetworkToHostOrder(data);
        }

        public uint ReadUIntNetworkByteOrder(ref Context ctx)
        {
            return (uint) ReadIntNetworkByteOrder(ref ctx);
        }

        public float ReadFloat(ref Context ctx)
        {
            UIntFloat uf = new UIntFloat();
            uf.intValue = (uint) ReadInt(ref ctx);
            return uf.floatValue;
        }
    }
}

namespace Unity.Networking.Transport.LowLevel.Unsafe
{
    /// <summary>
    /// DataStream (Reader/Writer) unsafe utilities used to do pointer operations on streams.
    /// 
    /// These are added to the <c>DataStreamWriter</c>/<c>DataStreamReader</c> classes as extensions, so
    /// you need to add <c>using Unity.Collections.LowLevel.Unsafe</c> at the top
    /// of file where you need to access these functions.
    ///
    /// Since these are unsafe C# operations care must be taken when using them, it can
    /// easily crash the editor/player.
    ///
    /// Every time data is written directly to the data stream buffer you must call
    /// <c>WriteBytesWithUnsafePointer</c> afterwards with the length of the data written so
    /// that the stream class can internally keep track of how much of the internal
    /// buffer has been written to.
    ///
    /// The functions have read/write access check variants which utilize the job
    /// system atomic safety handle. The ENABLE_UNITY_COLLECTIONS_CHECKS define needs
    /// to be used for this to work. For more information see
    /// <a href="https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.html">Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle</a>.
    ///
    /// Example of typical usage:
    /// <code>
    /// // Manually write some numbers into a data stream from a source buffer.
    /// var data = new DataStreamWriter(4, Allocator.Temp);
    /// unsafe
    /// {
    ///     var ptr = data.GetUnsafePtr();
    ///     var sourceData = new NativeArray&lt;byte&gt;(4, Allocator.Temp);
    ///     sourceData[0] = 42;
    ///     sourceData[1] = 42;
    ///     sourceData[2] = 42;
    ///     sourceData[3] = 42;
    ///     UnsafeUtility.MemCpy(ptr, sourceData.GetUnsafePtr(), sourceData.Length);
    ///     data.WriteBytesWithUnsafePointer(sourceData.Length);
    /// }
    /// </code>
    /// </summary>
    public static class DataStreamUnsafeUtility
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static AtomicSafetyHandle GetAtomicSafetyHandle(DataStreamWriter strm)
        {
            return strm.m_Safety;
        }

        public static void SetAtomicSafetyHandle(ref DataStreamWriter strm, AtomicSafetyHandle safety)
        {
            strm.m_Safety = safety;
        }

#endif
        /// <summary>
        /// Get the byte* pointer to the start of the buffer backing the <c>DataStreamWriter</c>.
        /// A safety check is done to see if you have write access to the buffer.
        /// </summary>
        /// <param name="strm"></param>
        /// <returns>Pointer to the data stream buffer.</returns>
        public unsafe static byte* GetUnsafePtr(this DataStreamWriter strm)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(strm.m_Safety);
#endif
            return strm.m_Data->buffer;
        }

        /// <summary>
        /// Get the byte* pointer to the start of the buffer backing the <c>DataStreamWriter</c>.
        /// A safety check is done to make sure you only have read access to the buffer.
        /// </summary>
        /// <param name="strm"></param>
        /// <returns>Pointer to the data stream buffer.</returns>
        public unsafe static byte* GetUnsafeReadOnlyPtr(this DataStreamWriter strm)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(strm.m_Safety);
#endif
            return strm.m_Data->buffer;
        }

        /// <summary>
        /// Get the byte* pointer to the buffer backing the <c>DataStreamWriter</c>.
        /// Does not check the safety handle for read/write access.
        /// </summary>
        /// <param name="strm"></param>
        /// <returns>Pointer to the data stream buffer.</returns>
        unsafe public static byte* GetUnsafeBufferPointerWithoutChecks(this DataStreamWriter strm)
        {
            return strm.m_Data->buffer;
        }

        /// <summary>
        /// Get the byte* pointer to the start of the buffer backing the <c>DataStreamReader</c>.
        /// A safety check is done to make sure you only have read access to the buffer.
        /// </summary>
        /// <param name="strm"></param>
        /// <returns>Pointer to the data stream buffer.</returns>
        public unsafe static byte* GetUnsafeReadOnlyPtr(this DataStreamReader strm)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(strm.m_Safety);
#endif
            return strm.m_bufferPtr;
        }
        
        /// <summary>
        /// Signal how many bytes have been written to the buffer used by the data
        /// stream using one of the unsafe pointer getters.
        /// </summary>
        /// <param name="strm"></param>
        /// <param name="length">Amount of data written to the buffer.</param>
        /// <exception cref="ArgumentOutOfRangeException">If the length specified brings the total length to a value higher than the capacity of the buffer.</exception>
        public unsafe static void WriteBytesWithUnsafePointer(this DataStreamWriter strm, int length)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(strm.m_Safety);
            if (strm.m_Data->length + length > strm.m_Data->capacity)
                throw new ArgumentOutOfRangeException();
#endif
            strm.m_Data->length += length;
        }

    }
}