using System;
using FlatSharp;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PolySpatial.Entities;
using Unity.PolySpatial.Internals;

namespace Unity.PolySpatial.Extensions.RuntimeTests
{
    /// <summary>
    /// Tests focusing on interop between entities ChangeList structs and standard ChangeList classes
    /// </summary>
    [TestFixture]
    class EntitiesChangeListTests
    {
        struct DummyStructData
        {
            public long Value;
        }

        struct ByteStructData
        {
            public byte Value;
        }

        class DummyFlatBufferData : IFlatBufferSerializable<DummyFlatBufferData>
        {
            public long Value;

            public ISerializer<DummyFlatBufferData> Serializer => new DummySerializer();

            private class DummySerializer : ISerializer<DummyFlatBufferData>
            {
                public FlatBufferDeserializationOption DeserializationOption => throw new NotImplementedException();
                public int GetMaxSize(DummyFlatBufferData item) => sizeof(long);
                public int Write<TSpanWriter>(TSpanWriter writer, Span<byte> destination, DummyFlatBufferData item) where TSpanWriter : ISpanWriter
                {
                    writer.WriteLong(destination, item.Value, 0);
                    return sizeof(long);
                }
                public DummyFlatBufferData Parse<TInputBuffer>(TInputBuffer buffer, FlatBufferDeserializationOption? option = null) where TInputBuffer : IInputBuffer
                {
                    var val = buffer.ReadLong(0);
                    return new DummyFlatBufferData { Value = val };
                }
                public ISerializer<DummyFlatBufferData> WithSettings(Action<SerializerSettings> settingsCallback) => this;
            }
        }

        // ChangeListStructWritable Tests

        [Test]
        public void Test_ChangeListStructWritable_Enumerator()
        {
            using var writer = new ChangeListStructWritable<DummyStructData>(Allocator.Temp);

            var tracking1 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(10) };
            var tracking2 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(20) };

            writer.Add(tracking1, new DummyStructData { Value = 111 });
            writer.Add(tracking2, new DummyStructData { Value = 222 });

            Assert.IsFalse(writer.IsEmpty);
            Assert.AreEqual(2, writer.Count);

            using var enumerator = writer.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(20, enumerator.Current.instanceId.id);
            Assert.AreEqual(222, enumerator.Current.engineData.Value);

            Assert.IsFalse(enumerator.MoveNext());

            enumerator.Reset();

            // Should point to the 0th element again
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);
        }

        [Test]
        public void Test_ChangeListStructWritable_ProducesValidChangeListBuffer()
        {
            using var writer = new ChangeListStructWritable<DummyStructData>(Allocator.Temp);

            var tracking1 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(10) };
            var tracking2 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(20) };

            writer.Add(tracking1, new DummyStructData { Value = 111 });
            writer.Add(tracking2, new DummyStructData { Value = 222 });

            Assert.IsFalse(writer.IsEmpty);
            Assert.AreEqual(2, writer.Count);

            var reader = new ChangeList<DummyStructData>(writer.RawData);

            Assert.AreEqual(2, reader.Count);

            using var enumerator = reader.GetInternalEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(20, enumerator.Current.instanceId.id);
            Assert.AreEqual(222, enumerator.Current.engineData.Value);

            Assert.IsFalse(enumerator.MoveNext());
        }

        // Verify Enumerator handles empty state gracefully
        [Test]
        public void Test_ChangeListStructWritable_Empty()
        {
            using var writer = new ChangeListStructWritable<DummyStructData>(Allocator.Temp);

            Assert.IsTrue(writer.IsEmpty);
            Assert.AreEqual(0, writer.Count);

            using var enumerator = writer.GetEnumerator();
            Assert.IsFalse(enumerator.MoveNext());
        }

        [Test]
        public void Test_ChangeListStructWritable_Clear()
        {
            using var writer = new ChangeListStructWritable<DummyStructData>(Allocator.Temp);

            Assert.IsTrue(writer.IsEmpty);

            writer.Add(new DefaultTrackingData(), new DummyStructData());

            Assert.IsFalse(writer.IsEmpty);
            Assert.AreEqual(1, writer.Count);

            writer.Clear();

            Assert.IsTrue(writer.IsEmpty);
            Assert.AreEqual(0, writer.RawData.Length);
            Assert.AreEqual(0, writer.Count);

            writer.Add(new DefaultTrackingData(), new DummyStructData { Value = 999 });

            var reader = new ChangeList<DummyStructData>(writer.RawData);

            Assert.AreEqual(1, reader.Count);
            using var enumerator = reader.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(999, enumerator.Current.engineData.Value);
        }

        // This tests that the enumerator jumps by the aligned stride (32), not the packed size (25).
        [Test]
        public void Test_ChangeListStructWritable_AlignmentStride()
        {
            var expectedElementSize = PolySpatialUtils.AlignSize(
                UnsafeUtility.SizeOf<PolySpatialChangeListEntityData>() + UnsafeUtility.SizeOf<byte>());

            using var writer = new ChangeListStructWritable<ByteStructData>(Allocator.Temp);

            writer.Add(new DefaultTrackingData(), new ByteStructData { Value = 1 });
            writer.Add(new DefaultTrackingData(), new ByteStructData { Value = 2 });

            Assert.AreEqual(expectedElementSize * 2, writer.RawData.Length);

            using var enumerator = writer.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(1, enumerator.Current.engineData.Value);

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(2, enumerator.Current.engineData.Value);
        }

        // ChangeListSerializedStructWritable Tests

        [Test]
        public void Test_ChangeListSerializedStructWritable_Enumerator()
        {
            using var writer = new ChangeListSerializedStructWritable<DummyFlatBufferData>(Allocator.Temp);

            var tracking1 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(10) };
            var tracking2 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(20) };

            writer.Add(tracking1, new DummyFlatBufferData { Value = 111 });
            writer.Add(tracking2, new DummyFlatBufferData { Value = 222 });

            Assert.IsFalse(writer.IsEmpty);

            using var enumerator = writer.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(20, enumerator.Current.instanceId.id);
            Assert.AreEqual(222, enumerator.Current.engineData.Value);

            Assert.IsFalse(enumerator.MoveNext());

            enumerator.Reset();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);
        }

        [Test]
        public void Test_ChangeListSerializedStructWritable_ProducesValidChangeListSerializedBuffer()
        {
            using var writer = new ChangeListSerializedStructWritable<DummyFlatBufferData>(Allocator.Temp);

            var tracking1 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(10) };
            var tracking2 = new DefaultTrackingData { instanceId = new PolySpatialInstanceID(20) };

            writer.Add(tracking1, new DummyFlatBufferData { Value = 111 });
            writer.Add(tracking2, new DummyFlatBufferData { Value = 222 });

            Assert.IsFalse(writer.IsEmpty);

            var reader = new ChangeListSerialized<DummyFlatBufferData>(writer.RawData);

            using var enumerator = reader.GetInternalEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(10, enumerator.Current.instanceId.id);
            Assert.AreEqual(111, enumerator.Current.engineData.Value);

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(20, enumerator.Current.instanceId.id);
            Assert.AreEqual(222, enumerator.Current.engineData.Value);

            Assert.IsFalse(enumerator.MoveNext());
        }

        [Test]
        public void Test_ChangeListSerializedStructWritable_Clear()
        {
            using var writer = new ChangeListSerializedStructWritable<DummyFlatBufferData>(Allocator.Temp);

            Assert.IsTrue(writer.IsEmpty);

            writer.Add(new DefaultTrackingData(), new DummyFlatBufferData());
            Assert.IsFalse(writer.IsEmpty);

            writer.Clear();

            Assert.IsTrue(writer.IsEmpty);
            Assert.AreEqual(0, writer.RawData.Length);

            writer.Add(new DefaultTrackingData(), new DummyFlatBufferData());
            Assert.IsFalse(writer.IsEmpty);
        }
    }
}
