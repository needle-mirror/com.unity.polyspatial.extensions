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
        public void Initialize(PolySpatialInstanceID id, Entity entity);
    }

    internal struct DefaultEntityTrackingData : IEntityTrackingData
    {
        public PolySpatialInstanceID instanceId;
        public PolySpatialTrackingFlags trackingFlags;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // The name of this entity for debugging.
        public FixedString32Bytes name;
#endif

        public PolySpatialInstanceID InstanceId => instanceId;

        public PolySpatialTrackingFlags TrackingFlags => trackingFlags;

        public void Initialize(PolySpatialInstanceID id, Entity entity)
        {
            instanceId = id;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            name.SetAndTruncate(entity.ToString());
#endif
            trackingFlags.Initialize();
        }

        /// <summary>
        /// Marks the data for destruction. Also validates internal consistency of flags in DEVELOPMENT_BUILDs
        /// </summary>
        public void MarkForDestruction() => trackingFlags = PolySpatialTrackingFlags.Destroyed | PolySpatialTrackingFlags.Disabled | PolySpatialTrackingFlags.Inactive;

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered due to GameObject active state
        /// </summary>
        public bool IsActive() => trackingFlags.IsActive();

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered due to component enabled state
        /// </summary>
        public bool IsEnabled() => trackingFlags.IsEnabled();

        /// <summary>
        /// Returns whether the content tracked by trackingFlags should currently be rendered
        /// </summary>
        public bool IsActiveAndEnabled() => trackingFlags.IsActiveAndEnabled();

        /// <summary>
        /// Updates the enabled/disabled state of flags to isEnabled. Also validates internal
        /// consistency of flags in DEVELOPMENT_BUILDs
        /// </summary>
        public void SetActiveState(bool isActive) => trackingFlags.SetActiveState(isActive);

        /// <summary>
        /// Updates the enabled/disabled state of flags to isEnabled. Also validates internal
        /// consistency of flags in DEVELOPMENT_BUILDs
        /// </summary>
        public void SetEnabledState(bool isEnabled) => trackingFlags.SetEnabledState(isEnabled);

        /// <summary>
        /// Extracts and returns just the lifecycle flag from aggregated flags bitfield. Also validates internal
        /// consistency of trackingFlags in DEVELOPMENT_BUILDs
        /// </summary>
        public PolySpatialTrackingFlags GetLifecycleStage() => trackingFlags.GetLifecycleStage();

        /// <summary>
        /// Changes the stage of flags to lifecycleStage. Does not perform full validation, but
        /// will error if supplied an inconsistent lifecycleStage.
        /// </summary>
        public void SetLifecycleStage(PolySpatialTrackingFlags value) => trackingFlags.SetLifecycleStage(value);

        /// <summary>
        /// Validates the internal consistency of trackingFlags by ensuring it has only one lifecycleStage and that deallocated
        /// data is never being accessed
        /// </summary>
        public bool ValidateTrackingFlags() => trackingFlags.Validate();
    }

    internal struct EntityTrackingData<T> : IEntityTrackingData where T : unmanaged
    {
        private DefaultEntityTrackingData defaultData;
        public T customData;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // The name of this entity for debugging.
        public void SetName(string name) => defaultData.name.SetAndTruncate(name);
#endif

        public bool IsDirty() => defaultData.trackingFlags.HasFlag(PolySpatialTrackingFlags.Dirty);

        public void MarkDirty() => defaultData.trackingFlags |= PolySpatialTrackingFlags.Dirty;

        public void MarkClean() => defaultData.trackingFlags &= ~PolySpatialTrackingFlags.Dirty;

        public void Initialize(PolySpatialInstanceID id, Entity entity) => defaultData.Initialize(id, entity);
        public void MarkForDestruction() => defaultData.MarkForDestruction();
        public bool IsActive() => defaultData.IsActive();
        public bool IsEnabled() => defaultData.IsEnabled();
        public bool IsActiveAndEnabled() => defaultData.IsActiveAndEnabled();
        public void SetActiveState(bool isActive) => defaultData.SetActiveState(isActive);
        public void SetEnabledState(bool isEnabled) => defaultData.SetEnabledState(isEnabled);
        public PolySpatialTrackingFlags GetLifecycleStage() => defaultData.GetLifecycleStage();
        public void SetLifecycleStage(PolySpatialTrackingFlags value) => defaultData.SetLifecycleStage(value);
        public bool ValidateTrackingFlags() => defaultData.ValidateTrackingFlags();
        public PolySpatialInstanceID InstanceId => defaultData.InstanceId;
        public PolySpatialTrackingFlags TrackingFlags => defaultData.TrackingFlags;
    }
}
