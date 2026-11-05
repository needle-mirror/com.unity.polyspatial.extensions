// Before 6000.6, ENABLE_PROFILER equals UNITY_EDITOR || DEVELOPMENT_BUILD.
// On 6000.6+, DEVELOPMENT_BUILD is deprecated in favor of UNITY_INCLUDE_INSTRUMENTATION.
#if !UNITY_6000_6_OR_NEWER && ENABLE_PROFILER
#define UNITY_INCLUDE_INSTRUMENTATION
#endif

using Unity.Collections;
using Unity.Entities;
using Unity.PolySpatial.Internals;

namespace Unity.PolySpatial.Entities
{
    internal struct PolySpatialMeshMaterialTrackingData
    {
        public PolySpatialMaterialTrackingData materials;
        public PolySpatialAssetID meshId;
    }

    internal interface IEntityTrackingData : ITrackingData
    {
        public void Initialize(PolySpatialEntityID id, Entity entity);
    }

    internal struct DefaultEntityTrackingData : IEntityTrackingData
    {
        public PolySpatialEntityID EntityId { get; set; }
        public PolySpatialTrackingFlags trackingFlags;

#if UNITY_INCLUDE_INSTRUMENTATION
        // The name of this entity for debugging.
        public FixedString32Bytes name;
#endif

        public readonly PolySpatialTrackingFlags TrackingFlags => trackingFlags;

        public void Initialize(PolySpatialEntityID id, Entity entity)
        {
            EntityId = id;

#if UNITY_INCLUDE_INSTRUMENTATION
            name.SetAndTruncate(entity.ToString());
#endif
            trackingFlags.Initialize();
        }

        /// <summary>
        /// Marks the data for destruction. Also validates internal consistency of flags in builds with diagnostic checks enabled
        /// </summary>
        public void MarkForDestruction() => trackingFlags = PolySpatialTrackingFlags.Destroyed | PolySpatialTrackingFlags.Disabled | PolySpatialTrackingFlags.Inactive;

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered due to GameObject active state
        /// </summary>
        public readonly bool IsActive() => trackingFlags.IsActive();

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered due to component enabled state
        /// </summary>
        public readonly bool IsEnabled() => trackingFlags.IsEnabled();

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered
        /// </summary>
        public readonly bool IsActiveAndEnabled() => trackingFlags.IsActiveAndEnabled();

        /// <summary>
        /// Updates the enabled/disabled state of flags to isEnabled. Also validates internal
        /// consistency of flags in builds with diagnostic checks enabled
        /// </summary>
        public void SetActiveState(bool isActive) => trackingFlags.SetActiveState(isActive);

        /// <summary>
        /// Updates the enabled/disabled state of flags to isEnabled. Also validates internal
        /// consistency of flags in builds with diagnostic checks enabled
        /// </summary>
        public void SetEnabledState(bool isEnabled) => trackingFlags.SetEnabledState(isEnabled);

        /// <summary>
        /// Extracts and returns just the lifecycle flag from aggregated flags bitfield. Also validates internal
        /// consistency of trackingFlags in builds with diagnostic checks enabled
        /// </summary>
        public readonly PolySpatialTrackingFlags GetLifecycleStage() => trackingFlags.GetLifecycleStage();

        /// <summary>
        /// Changes the stage of flags to lifecycleStage. Does not perform full validation, but
        /// will error if supplied an inconsistent lifecycleStage.
        /// </summary>
        public void SetLifecycleStage(PolySpatialTrackingFlags value) => trackingFlags.SetLifecycleStage(value);

        /// <summary>
        /// Validates the internal consistency of trackingFlags by ensuring it has only one lifecycleStage and that deallocated
        /// data is never being accessed
        /// </summary>
        public readonly bool ValidateTrackingFlags() => trackingFlags.Validate();
    }

    internal struct EntityTrackingData<T> : IEntityTrackingData where T : unmanaged
    {
        private DefaultEntityTrackingData defaultData;
        public T customData;

#if UNITY_INCLUDE_INSTRUMENTATION
        // The name of this entity for debugging.
        public void SetName(string name)
        {
            defaultData.name.SetAndTruncate(name);
        }
#endif

        public readonly bool IsDirty() => defaultData.trackingFlags.HasFlag(PolySpatialTrackingFlags.Dirty);

        public void MarkDirty() => defaultData.trackingFlags |= PolySpatialTrackingFlags.Dirty;

        public void MarkClean() => defaultData.trackingFlags &= ~PolySpatialTrackingFlags.Dirty;

        public void Initialize(PolySpatialEntityID id, Entity entity) => defaultData.Initialize(id, entity);
        public void MarkForDestruction() => defaultData.MarkForDestruction();
        public readonly bool IsActive() => defaultData.IsActive();
        public readonly bool IsEnabled() => defaultData.IsEnabled();
        public readonly bool IsActiveAndEnabled() => defaultData.IsActiveAndEnabled();
        public void SetActiveState(bool isActive) => defaultData.SetActiveState(isActive);
        public void SetEnabledState(bool isEnabled) => defaultData.SetEnabledState(isEnabled);
        public readonly PolySpatialTrackingFlags GetLifecycleStage() => defaultData.GetLifecycleStage();
        public void SetLifecycleStage(PolySpatialTrackingFlags value) => defaultData.SetLifecycleStage(value);
        public readonly bool ValidateTrackingFlags() => defaultData.ValidateTrackingFlags();
        public readonly PolySpatialEntityID EntityId => defaultData.EntityId;
        public readonly PolySpatialTrackingFlags TrackingFlags => defaultData.TrackingFlags;
    }
}
