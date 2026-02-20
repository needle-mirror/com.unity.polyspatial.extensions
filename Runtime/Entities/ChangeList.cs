using System;
using System.Collections;
using System.Collections.Generic;
using FlatSharp;
using FlatSharp.Runtime.Extensions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PolySpatial.Internals;

namespace Unity.PolySpatial.Entities
{
    internal struct ChangeListUnmanagedEnumerator<TEngineData> : IEnumerator<EngineAndObjectData<TEngineData>>
        where TEngineData : unmanaged
    {
        static readonly int k_Stride = PolySpatialUtils.AlignSize(UnsafeUtility.SizeOf<EngineAndObjectData<TEngineData>>());

        NativeArray<byte> m_Array;
        int m_ByteOffset;
        readonly int m_Length;

        public ChangeListUnmanagedEnumerator(NativeArray<byte> array, int length)
        {
            m_Array = array;
            m_Length = length;

            // IEnumerator internal "cursor" starts at a position before the collection's first element
            m_ByteOffset = -k_Stride;
        }

        public readonly void Dispose() { }

        public bool MoveNext()
        {
            m_ByteOffset += k_Stride;
            return m_ByteOffset < m_Length;
        }

        public void Reset() => m_ByteOffset = -k_Stride;

        public readonly unsafe EngineAndObjectData<TEngineData>* UnsafeCurrentPointer =>
            (EngineAndObjectData<TEngineData>*)((byte*)m_Array.GetUnsafePtr() + m_ByteOffset);

        public readonly unsafe EngineAndObjectData<TEngineData> Current => *UnsafeCurrentPointer;

        readonly object IEnumerator.Current => Current;
    }

    internal struct ChangeListManagedEnumerator<TEngineData> : IEnumerator<EngineAndObjectData<TEngineData>>
        where TEngineData : class, IFlatBufferSerializable<TEngineData>, new()
    {
        NativeArray<byte> m_Array;
        int m_Index;
        int m_Length;

        public ChangeListManagedEnumerator(NativeArray<byte> array, int length)
        {
            m_Array = array;
            m_Index = -1;
            m_Length = length;
        }

        public void Dispose() { }

        public unsafe bool MoveNext()
        {
            if (m_Index == -1)
            {
                if (m_Length <= 0)
                    return false;
                m_Index = 0;
                return true;

            }

            var curSize = *(int*)((byte*)m_Array.GetUnsafePtr() + m_Index);
            curSize = PolySpatialUtils.AlignSize(curSize);
            if (m_Index + curSize >= m_Length)
                return false;
            m_Index += curSize;
            return true;
        }

        public void Reset() => m_Index = -1;

        public unsafe EngineAndObjectData<TEngineData> Current
        {
            get
            {
                EngineAndObjectData<TEngineData> result = new()
                {
                    objectData = *UnsafeCurrentEntityDataPointer,
                    engineData = new TEngineData()
                };
                var size = UnsafeCurrentDataSize;
                result.engineData = size > 0 ? result.engineData.Serializer.Parse(size, UnsafeCurrentDataPointer) : null;
                return result;
            }
        }

        object IEnumerator.Current => Current;

        readonly unsafe byte* CurrentBasePointer => (byte*)m_Array.GetUnsafePtr() + m_Index;

        public readonly unsafe PolySpatialChangeListEntityData* UnsafeCurrentEntityDataPointer =>
            (PolySpatialChangeListEntityData*)(CurrentBasePointer + PolySpatialUtils.PaddedIntSize);

        public readonly unsafe int UnsafeCurrentDataSize =>
            (*(int*)CurrentBasePointer) - PolySpatialUtils.PaddedIntSize - sizeof(PolySpatialChangeListEntityData);

        public readonly unsafe byte* UnsafeCurrentDataPointer => (byte*)(UnsafeCurrentEntityDataPointer + 1);

    }

    /// <summary>
    /// <see cref="ChangeList{TEngineData}.Writable"/> implemented as a struct so it can be used in Burst jobs
    /// </summary>
    internal struct ChangeListStructWritable<TEngineData> : IChangeListWritable<TEngineData>
        where TEngineData : unmanaged
    {
        static readonly int k_ElementSize = UnsafeUtility.SizeOf<EngineAndObjectData<TEngineData>>();
        static readonly int k_Stride = PolySpatialUtils.AlignSize(k_ElementSize);

        NativeList<byte> m_Data;

        public void Clear() => m_Data.Clear();

        public NativeArray<byte> RawData => m_Data.AsArray();

        public readonly bool IsEmpty => m_Data.Length == 0;

        public readonly int Count => m_Data.Length / k_ElementSize;

        public IEnumerator<EngineAndObjectData<TEngineData>> GetEnumerator() =>
            new ChangeListUnmanagedEnumerator<TEngineData>(m_Data.AsArray(), m_Data.Length);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose() => m_Data.Dispose();

        public ChangeListStructWritable(AllocatorManager.AllocatorHandle allocatorHandle) =>
            m_Data = new NativeList<byte>(allocatorHandle);

        public unsafe void Add<TTrackingData>(TTrackingData trackingData, TEngineData element)
            where TTrackingData : ITrackingData
        {
            var bufferStart = m_Data.Length;
            m_Data.Length += k_Stride;
            var ptr = m_Data.GetUnsafePtr() + bufferStart;
            *(EngineAndObjectData<TEngineData>*)ptr = new EngineAndObjectData<TEngineData>
            {
                objectData = new()
                {
                    instanceId = trackingData.InstanceId,
                    trackingFlags = (int)trackingData.TrackingFlags
                },
                engineData = element
            };
        }
    }

    /// <summary>
    /// <see cref="ChangeListSerialized{TEngineData}.Writable"/> implemented as a struct so it can be used in Burst jobs
    /// </summary>
    internal struct ChangeListSerializedStructWritable<TEngineData> : IChangeListWritable<TEngineData>
        where TEngineData : class, IFlatBufferSerializable<TEngineData>, new()
    {
        public NativeArray<byte> RawData => m_Data.AsArray();
        NativeList<byte> m_Data;

        public readonly bool IsEmpty => m_Data.Length == 0;

        public ChangeListSerializedStructWritable(AllocatorManager.AllocatorHandle allocatorHandle) =>
            m_Data = new(allocatorHandle);

        public IEnumerator<EngineAndObjectData<TEngineData>> GetEnumerator() =>
            new ChangeListManagedEnumerator<TEngineData>(m_Data.AsArray(), m_Data.Length);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public unsafe void Add<TTrackingData>(TTrackingData trackingData, TEngineData element)
            where TTrackingData : ITrackingData
        {
            var serializer = element?.Serializer;
            var maxSize = serializer?.GetMaxSize(element) ?? 0;

            var size = PolySpatialUtils.PaddedIntSize + sizeof(PolySpatialChangeListEntityData) + maxSize;
            var oldLength = m_Data.Length;
            m_Data.Length += size;
            var ptr = (int*)(m_Data.GetUnsafePtr() + m_Data.Length - size);
            var obj = (PolySpatialChangeListEntityData*)((byte*)ptr + PolySpatialUtils.PaddedIntSize);
            *obj = new PolySpatialChangeListEntityData
            {
                instanceId = trackingData.InstanceId,
                trackingFlags = (int)trackingData.TrackingFlags
            };
            var actualSize = serializer?.Write(new Span<byte>((byte*)(obj + 1), maxSize), element) ?? 0;
            actualSize += PolySpatialUtils.PaddedIntSize + sizeof(PolySpatialChangeListEntityData);
            *ptr = actualSize;
            m_Data.Length = oldLength + PolySpatialUtils.AlignSize(actualSize);
        }

        public void Clear() => m_Data.Clear();

        public void Dispose() => m_Data.Dispose();
    }
}
