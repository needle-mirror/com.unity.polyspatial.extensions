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
        private NativeArray<byte> m_array;
        private int m_index;
        private int m_length;

        public ChangeListUnmanagedEnumerator(NativeArray<byte> array, int length)
        {
            m_array = array;
            m_index = -1;
            m_length = length;
        }

        public void Dispose() { }

        public unsafe bool MoveNext() => ++m_index * sizeof(EngineAndObjectData<TEngineData>) < m_length;

        public void Reset() => m_index = -1;

        public unsafe EngineAndObjectData<TEngineData>* UnsafeCurrentPointer => (EngineAndObjectData<TEngineData>*)m_array.GetUnsafePtr() + m_index;

        public unsafe EngineAndObjectData<TEngineData> Current => *UnsafeCurrentPointer;

        object IEnumerator.Current => Current;
    }

    internal struct ChangeListManagedEnumerator<TEngineData> : IEnumerator<EngineAndObjectData<TEngineData>>
        where TEngineData : class, IFlatBufferSerializable<TEngineData>, new()
    {
        private NativeArray<byte> m_Array;
        private int m_Index;
        private int m_Length;

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

        unsafe byte* CurrentBasePointer => (byte*)m_Array.GetUnsafePtr() + m_Index;

        public unsafe PolySpatialChangeListEntityData* UnsafeCurrentEntityDataPointer =>
            (PolySpatialChangeListEntityData*)(CurrentBasePointer + PolySpatialUtils.PaddedIntSize);

        public unsafe int UnsafeCurrentDataSize =>
            (*(int*)CurrentBasePointer) - PolySpatialUtils.PaddedIntSize - sizeof(PolySpatialChangeListEntityData);

        public unsafe byte* UnsafeCurrentDataPointer => (byte*)(UnsafeCurrentEntityDataPointer + 1);

    }

    internal struct ChangeListStruct<TEngineData> : IChangeList<TEngineData>
        where TEngineData : unmanaged
    {
        public NativeArray<byte> RawData => m_data;
        private NativeArray<byte> m_data;

        public bool IsEmpty => m_data.Length == 0;

        public ChangeListStruct(NativeArray<byte> data) => m_data = data;

        public IEnumerator<EngineAndObjectData<TEngineData>> GetEnumerator() => new ChangeListUnmanagedEnumerator<TEngineData>(m_data, m_data.Length);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Follows the code from com.unity.polyspatial/Runtime/Trackers/ChangeList.cs but implemented as a struct so it can be used in Burst jobs
    // All pointer/offset math should be the same as in ChangeList.cs
    internal struct ChangeListStructWritable<TEngineData> : IChangeListWritable<TEngineData>
        where TEngineData : unmanaged
    {
        public NativeList<byte> m_data;
        public void Clear() => m_data.Clear();

        public NativeArray<byte> RawData => m_data.AsArray();

        public bool IsEmpty => m_data.Length == 0;

        public IEnumerator<EngineAndObjectData<TEngineData>> GetEnumerator() => new ChangeListUnmanagedEnumerator<TEngineData>(m_data.AsArray(), m_data.Length);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose() => m_data.Dispose();

        public ChangeListStructWritable(AllocatorManager.AllocatorHandle allocatorHandle) => m_data = new NativeList<byte>(allocatorHandle);

        public unsafe void Add<TTrackingData>(TTrackingData trackingData, TEngineData element)
            where TTrackingData : ITrackingData
        {
            m_data.Length += sizeof(EngineAndObjectData<TEngineData>);
            var ptr = m_data.GetUnsafePtr() + m_data.Length - sizeof(EngineAndObjectData<TEngineData>);
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

    // Follows the code from com.unity.polyspatial/Runtime/Trackers/ChangeList.cs but implemented as a struct so it can be used in Burst jobs
    // All pointer/offset math should be the same as in ChangeList.cs
    internal struct ChangeListSerializedStructWritable<TEngineData> : IChangeListWritable<TEngineData>
        where TEngineData : class, IFlatBufferSerializable<TEngineData>, new()
    {
        public NativeArray<byte> RawData => m_data.AsArray();
        private NativeList<byte> m_data;

        public bool IsEmpty => m_data.Length == 0;

        public ChangeListSerializedStructWritable(AllocatorManager.AllocatorHandle allocatorHandle) => m_data = new(allocatorHandle);

        public IEnumerator<EngineAndObjectData<TEngineData>> GetEnumerator() => new ChangeListManagedEnumerator<TEngineData>(m_data.AsArray(), m_data.Length);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public unsafe void Add<TTrackingData>(TTrackingData trackingData, TEngineData element)
            where TTrackingData : ITrackingData
        {
            var serializer = element?.Serializer;
            var maxSize = serializer?.GetMaxSize(element) ?? 0;

            var size = PolySpatialUtils.PaddedIntSize + sizeof(PolySpatialChangeListEntityData) + maxSize;
            var oldLength = m_data.Length;
            m_data.Length += size;
            var ptr = (int*)(m_data.GetUnsafePtr() + m_data.Length - size);
            var obj = (PolySpatialChangeListEntityData*)((byte*)ptr + PolySpatialUtils.PaddedIntSize);
            *obj = new PolySpatialChangeListEntityData
            {
                instanceId = trackingData.InstanceId,
                trackingFlags = (int)trackingData.TrackingFlags
            };
            var actualSize = serializer?.Write(new Span<byte>((byte*)(obj + 1), maxSize), element) ?? 0;
            actualSize += PolySpatialUtils.PaddedIntSize + sizeof(PolySpatialChangeListEntityData);
            *ptr = actualSize;
            m_data.Length = oldLength + PolySpatialUtils.AlignSize(actualSize);
        }

        public void Clear() => m_data.Clear();

        public void Dispose() => m_data.Dispose();
    }
}
