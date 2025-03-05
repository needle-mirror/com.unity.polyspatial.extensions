using System.Collections.Generic;
using Unity.Assertions;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.PolySpatial.Internals;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
using UnityEditor.PolySpatial.Analytics;
#endif

namespace Unity.PolySpatial.Entities
{
    internal struct TrackedEntity : ICleanupComponentData { }

    internal struct TrackedMaterialMeshInfo : ICleanupComponentData { }

    internal struct TrackedRendering : ICleanupComponentData { }

    internal struct PolyRenderMeshArray
    {
        public int Version;
        public UnsafeList<PolySpatialAssetID> Materials;
        public UnsafeList<PolySpatialAssetID> Meshes;
        public uint4 hash128;

        public PolySpatialAssetID GetMaterialID(MaterialMeshInfo materialMeshInfo)
        {
            var materialIndex = materialMeshInfo.MaterialArrayIndex;

            if (!Materials.IsCreated || materialIndex >= Materials.Length)
                return PolySpatialAssetID.InvalidAssetID;
            return Materials[materialIndex];
        }

        public PolySpatialAssetID GetMeshID(MaterialMeshInfo materialMeshInfo)
        {
            var meshIndex = materialMeshInfo.MeshArrayIndex;

            if (!Meshes.IsCreated || meshIndex >= Meshes.Length)
                return PolySpatialAssetID.InvalidAssetID;
            return Meshes[meshIndex];
        }
    }
    internal static class PolySpatialEntitiesUtils
    {
        public static PolySpatialInstanceID IdFor(Entity entity) => PolySpatialInstanceID.For((long)entity.Index << 32 | (long)entity.Version);
    }

    internal partial struct PolySpatialEntitiesSystem : ISystem
    {
        private NativeParallelHashMap<int, PolyRenderMeshArray> m_polyRenderMeshArrays;

        private EntityQuery m_newVisibleEntitiesQuery;
        private EntityQuery m_newInvisibleEntitiesQuery;
        private EntityQuery m_removedEntitiesQuery;
        private EntityQuery m_updatedTransformsQuery;
        private EntityQuery m_newMaterialMeshQuery;
        private EntityQuery m_removedMaterialMeshQuery;
        private EntityQuery m_enableRenderingEntitiesQuery;
        private EntityQuery m_disableRenderingEntitiesQuery;

        private TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialGameObjectData>> m_entityTrackerMap;

        private NewEntityData m_newEntityData;

        private NativeList<PolySpatialInstanceID> m_removedEntityIds;

        private NativeList<PolySpatialInstanceID> m_idBuffer;
        private NativeList<Vector3> m_positionBuffer;
        private NativeList<Quaternion> m_rotationBuffer;
        private NativeList<Vector3> m_scaleBuffer;
        private NativeList<PolySpatialInstanceID> m_parentBuffer;
        private ChangeListStructWritable<PolySpatialGameObjectData> m_entityChanges;

        private TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialMeshMaterialTrackingData>> m_materialMeshTrackerMap;
        private ChangeListSerializedStructWritable<PolySpatialRenderData> m_materialMeshChanges;
        private NativeList<PolySpatialInstanceID> m_materialMeshRemoved;

        private static List<RenderMeshArray> m_renderMeshArraysBuffer;
        private static List<int> m_sharedIndicesBuffer;
        private static List<int> m_sharedVersionsBuffer;

        private SharedComponentTypeHandle<RenderMeshArray> m_renderMeshArrayType;
        private ComponentTypeHandle<MaterialMeshInfo> m_materialMeshInfoType;
        private EntityTypeHandle m_entityType;

        #region Events
        public void OnCreate(ref SystemState systemState)
        {
#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
            if(Application.isPlaying)
                PolySpatialAnalytics.Send(FeatureName.EntitiesComponentSystem, "Enabled");
#endif

            m_polyRenderMeshArrays = new NativeParallelHashMap<int, PolyRenderMeshArray>(256, Allocator.Persistent);

            m_newVisibleEntitiesQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<LocalToWorld>() },
                None = new[] { ComponentType.ReadOnly<TrackedEntity>(), ComponentType.ReadOnly<DisableRendering>() }
            });

            m_newInvisibleEntitiesQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<LocalToWorld>(), ComponentType.ReadOnly<DisableRendering>() },
                None = new[] { ComponentType.ReadOnly<TrackedEntity>() }
            });

            m_removedEntitiesQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<TrackedEntity>() },
                None = new[] { ComponentType.ReadOnly<LocalToWorld>() }
            });

            m_updatedTransformsQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<LocalToWorld>(), ComponentType.ReadOnly<TrackedEntity>() }
            });
            m_updatedTransformsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<LocalToWorld>());
            m_updatedTransformsQuery.AddOrderVersionFilter();

            m_newMaterialMeshQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<MaterialMeshInfo>() },
                None = new[] { ComponentType.ReadOnly<TrackedMaterialMeshInfo>() }
            });

            m_removedMaterialMeshQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                // LocalToWorld is needed here as we'd like to only query for entities whose MaterialMeshInfo has been removed, not entities that have been removed
                All = new[] { ComponentType.ReadOnly<TrackedMaterialMeshInfo>(), ComponentType.ReadOnly<LocalToWorld>() },
                None = new[] { ComponentType.ReadOnly<MaterialMeshInfo>() }
            });

            m_enableRenderingEntitiesQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<TrackedEntity>(), ComponentType.ReadOnly<MaterialMeshInfo>() },
                None = new[] { ComponentType.ReadOnly<DisableRendering>(), ComponentType.ReadOnly<TrackedRendering>(),  }
            });

            m_disableRenderingEntitiesQuery = systemState.EntityManager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadOnly<TrackedEntity>(), ComponentType.ReadOnly<MaterialMeshInfo>(), ComponentType.ReadOnly<DisableRendering>(), ComponentType.ReadOnly<TrackedRendering>() },
            });

            m_entityTrackerMap = new TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialGameObjectData>>();
            m_entityTrackerMap.Initialize(1024);

            m_newEntityData = new NewEntityData(Allocator.Persistent);

            m_removedEntityIds = new NativeList<PolySpatialInstanceID>(Allocator.Persistent);

            m_idBuffer = new NativeList<PolySpatialInstanceID>(Allocator.Persistent);
            m_positionBuffer = new NativeList<Vector3>(Allocator.Persistent);
            m_rotationBuffer = new NativeList<Quaternion>(Allocator.Persistent);
            m_scaleBuffer = new NativeList<Vector3>(Allocator.Persistent);
            m_parentBuffer = new NativeList<PolySpatialInstanceID>(Allocator.Persistent);
            m_entityChanges = new ChangeListStructWritable<PolySpatialGameObjectData>(Allocator.Persistent);

            m_materialMeshTrackerMap = new TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialMeshMaterialTrackingData>>();
            m_materialMeshTrackerMap.Initialize(1024);
            m_materialMeshChanges = new ChangeListSerializedStructWritable<PolySpatialRenderData>(Allocator.Persistent);
            m_materialMeshRemoved = new NativeList<PolySpatialInstanceID>(Allocator.Persistent);

            m_renderMeshArraysBuffer = new List<RenderMeshArray>();
            m_sharedIndicesBuffer = new List<int>();
            m_sharedVersionsBuffer = new List<int>();

            m_renderMeshArrayType = systemState.GetSharedComponentTypeHandle<RenderMeshArray>();
            m_materialMeshInfoType = systemState.GetComponentTypeHandle<MaterialMeshInfo>();
            m_entityType = systemState.GetEntityTypeHandle();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (PolySpatialCore.LocalAssetManager == null)
                return;

            RegisterMaterialsAndMeshes(systemState.EntityManager);

            var count = m_updatedTransformsQuery.CalculateEntityCount();
            m_idBuffer.ResizeUninitialized(count);
            m_positionBuffer.ResizeUninitialized(count);
            m_rotationBuffer.ResizeUninitialized(count);
            m_scaleBuffer.ResizeUninitialized(count);
            m_parentBuffer.ResizeUninitialized(count);

            m_renderMeshArrayType.Update(ref systemState);
            m_materialMeshInfoType.Update(ref systemState);
            m_entityType.Update(ref systemState);

            var entityCommandBuffer = new EntityCommandBuffer(Allocator.TempJob);

            systemState.Dependency = new HandleNewEntitiesJob()
            {
                ECB = entityCommandBuffer,
                EntityTrackerMap = m_entityTrackerMap,
                NewEntityData = m_newEntityData,
                visible = true
            }.Schedule(m_newVisibleEntitiesQuery, systemState.Dependency);

            systemState.Dependency = new HandleNewEntitiesJob()
            {
                ECB = entityCommandBuffer,
                EntityTrackerMap = m_entityTrackerMap,
                NewEntityData = m_newEntityData,
                visible = false
            }.Schedule(m_newInvisibleEntitiesQuery, systemState.Dependency);

            systemState.Dependency = new HandleRemovedEntitiesJob()
            {
                ECB = entityCommandBuffer,
                EntityTrackerMap = m_entityTrackerMap,
                EntitiesChanges = m_removedEntityIds
            }.Schedule(m_removedEntitiesQuery, systemState.Dependency);

            systemState.Dependency = new HandleTransformUpdatesJob()
            {
                IdBuffer = m_idBuffer.AsArray(),
                PositionBuffer = m_positionBuffer.AsArray(),
                RotationBuffer = m_rotationBuffer.AsArray(),
                ScaleBuffer = m_scaleBuffer.AsArray(),
                ParentBuffer = m_parentBuffer.AsArray()
            }.ScheduleParallel(m_updatedTransformsQuery, systemState.Dependency);

            systemState.Dependency = new HandleNewMaterialMeshInfosJob()
            {
                ECB = entityCommandBuffer,
                RenderMeshArray = m_renderMeshArrayType,
                PolyRenderMeshArrays = m_polyRenderMeshArrays,
                TrackerMap = m_materialMeshTrackerMap,
                Changes = m_materialMeshChanges,
                MaterialMeshInfoType = m_materialMeshInfoType,
                EntityType = m_entityType
            }.Schedule(m_newMaterialMeshQuery, systemState.Dependency);

            systemState.Dependency = new HandleRemovedMaterialMeshInfosJob()
            {
                ECB = entityCommandBuffer,
                EntityType = m_entityType,
                TrackerMap = m_materialMeshTrackerMap,
                Changes = m_materialMeshRemoved
            }.Schedule(m_removedMaterialMeshQuery, systemState.Dependency);

            systemState.Dependency = new SetEnableRenderingEntitiesJob()
            {
                ECB = entityCommandBuffer,
                TrackerMap = m_entityTrackerMap,
                Changes = m_entityChanges,
                visible = false
            }.Schedule(m_disableRenderingEntitiesQuery, systemState.Dependency);

            systemState.Dependency = new SetEnableRenderingEntitiesJob()
            {
                ECB = entityCommandBuffer,
                TrackerMap = m_entityTrackerMap,
                Changes = m_entityChanges,
                visible = true
            }.Schedule(m_enableRenderingEntitiesQuery, systemState.Dependency);

            systemState.Dependency.Complete();

            entityCommandBuffer.Playback(systemState.EntityManager);
            entityCommandBuffer.Dispose();

            var sim = PolySpatialCore.UnitySimulation;
            if (sim != null)
            {
                if (!m_newEntityData.ids.IsEmpty)
                {
                    sim.AddEntitiesWithTransforms(
                        m_newEntityData.ids.AsArray(),
                        m_newEntityData.parentIds.AsArray(),
                        m_newEntityData.positions.AsArray(),
                        m_newEntityData.rotations.AsArray(),
                        m_newEntityData.scales.AsArray(),
                        m_newEntityData.data.AsArray());
                }

                if (!m_entityChanges.IsEmpty)
                {
                    sim.OnGameObjectsModified(m_entityChanges);
                }

                if (!m_idBuffer.IsEmpty)
                {
                    sim.OnTransformsChanged(m_idBuffer.AsArray(), m_positionBuffer.AsArray(), m_rotationBuffer.AsArray(), m_scaleBuffer.AsArray());
                    sim.OnHierarchyChanged(m_idBuffer.AsArray(), m_parentBuffer.AsArray());
                }

                if (!m_materialMeshChanges.IsEmpty)
                {
                    sim.OnMeshRenderersCreatedOrUpdated(m_materialMeshChanges);
                }

                if (!m_materialMeshRemoved.IsEmpty)
                {
                    sim.OnMeshRenderersDestroyed(m_materialMeshRemoved.AsArray());
                }

                if (!m_removedEntityIds.IsEmpty)
                {
                    sim.OnGameObjectsDestroyed(m_removedEntityIds.AsArray());
                }
            }

            m_newEntityData.Clear();
            m_entityChanges.Clear();
            m_removedEntityIds.Clear();
            m_idBuffer.Clear();
            m_positionBuffer.Clear();
            m_rotationBuffer.Clear();
            m_scaleBuffer.Clear();
            m_parentBuffer.Clear();
            m_materialMeshChanges.Clear();
            m_materialMeshRemoved.Clear();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            var polyRenderArrays = m_polyRenderMeshArrays.GetValueArray(Allocator.Temp);
            for (var i = 0; i < polyRenderArrays.Length; ++i)
                UnregisterMaterialsAndMeshesAndDispose(polyRenderArrays[i]);
            m_polyRenderMeshArrays.Dispose();

            m_newVisibleEntitiesQuery.Dispose();
            m_newInvisibleEntitiesQuery.Dispose();
            m_removedEntitiesQuery.Dispose();
            m_updatedTransformsQuery.Dispose();
            m_newMaterialMeshQuery.Dispose();
            m_removedMaterialMeshQuery.Dispose();
            m_entityTrackerMap.Dispose();

            m_newEntityData.Dispose();
            m_removedEntityIds.Dispose();

            m_idBuffer.Dispose();
            m_positionBuffer.Dispose();
            m_rotationBuffer.Dispose();
            m_scaleBuffer.Dispose();
            m_parentBuffer.Dispose();
            m_entityChanges.Dispose();

            m_materialMeshTrackerMap.Dispose();
            m_materialMeshChanges.Dispose();
            m_materialMeshRemoved.Dispose();
        }

        #endregion

        #region MaterialMeshManagement

        // This function is responsible for registering materials and meshes with the PolySpatialCore.LocalAssetManager.
        // It is called every frame and will register any new materials or meshes that have been added to the scene.
        // It will also unregister any materials or meshes that have been removed from the scene.
        // It works by retrieving the RenderMeshArray shared components from the EntityManager and then iterating over them.
        // For each RenderMeshArray, it will check if the corresponding PolyRenderMeshArray already exists in the m_polyRenderMeshArrays map.
        // If it does, it will update the version and hash128 fields of the PolyRenderMeshArray and update the materials and meshes.
        // If it doesn't, it will create a new PolyRenderMeshArray and add it to the map.
        // After iterating over all the RenderMeshArrays, it will check if any PolyRenderMeshArrays need to be removed and dispose of them.
        // Finally, it will update the m_polyRenderMeshArrays map with the new PolyRenderMeshArrays.
        private void RegisterMaterialsAndMeshes(EntityManager em)
        {
            // Get the filtered render mesh arrays
            var (renderArrays, sharedIndices, sharedVersions) = GetFilteredRenderMeshArrays(em);

            var polyArraysToDispose = new NativeList<PolyRenderMeshArray>(renderArrays.Count, Allocator.Temp);

            var sortedKeys = m_polyRenderMeshArrays.GetKeyArray(Allocator.Temp);
            sortedKeys.Sort();

            for (int i = 0, j = 0; (i < sortedKeys.Length) && (j < renderArrays.Count); ++i)
            {
                var oldKey = sortedKeys[i];
                while (j < renderArrays.Count && sharedIndices[j] < oldKey)
                    ++j;
                var found = j != renderArrays.Count && sharedIndices[j] == oldKey;
                if (found)
                    continue;
                var polyRenderMeshArray = m_polyRenderMeshArrays[oldKey];
                polyArraysToDispose.Add(polyRenderMeshArray);
                m_polyRenderMeshArrays.Remove(oldKey);
            }

            sortedKeys.Dispose();

            for (var index = 0; index < renderArrays.Count; ++index)
            {
                var renderArray = renderArrays[index];
                if (renderArray.MaterialReferences == null || renderArray.MeshReferences == null)
                    continue;

                var sharedIndex = sharedIndices[index];
                var sharedVersion = sharedVersions[index];
                var hash128 = renderArray.GetHash128();

                var update = false;
                if (m_polyRenderMeshArrays.TryGetValue(sharedIndex, out var polyRenderMeshArray))
                {
                    if (polyRenderMeshArray.Version != sharedVersion || math.any(polyRenderMeshArray.hash128 != hash128))
                    {
                        polyArraysToDispose.Add(polyRenderMeshArray);
                        update = true;
                    }
                }
                else
                {
                    polyRenderMeshArray = new PolyRenderMeshArray();
                    update = true;
                }

                if (!update)
                    continue;

                var materialCount = renderArray.MaterialReferences.Length;
                var meshCount = renderArray.MeshReferences.Length;

                polyRenderMeshArray.Version = sharedVersion;
                polyRenderMeshArray.hash128 = hash128;
                polyRenderMeshArray.Materials = new UnsafeList<PolySpatialAssetID>(materialCount, Allocator.Persistent);
                polyRenderMeshArray.Meshes = new UnsafeList<PolySpatialAssetID>(meshCount, Allocator.Persistent);

                for (var i = 0; i < materialCount; ++i)
                {
                    var material = renderArray.MaterialReferences[i];
                    Debug.Assert(material != null, "Material is null");
                    var id = PolySpatialCore.LocalAssetManager.Register((Material)material);
                    polyRenderMeshArray.Materials.Add(id);
                }

                for (var i = 0; i < meshCount; ++i)
                {
                    var mesh = renderArray.MeshReferences[i];
                    var id = PolySpatialCore.LocalAssetManager.Register((Mesh)mesh);
                    polyRenderMeshArray.Meshes.Add(id);
                }

                m_polyRenderMeshArrays[sharedIndex] = polyRenderMeshArray;
            }

            for (var i = 0; i < polyArraysToDispose.Length; ++i)
                UnregisterMaterialsAndMeshesAndDispose(polyArraysToDispose[i]);

            polyArraysToDispose.Dispose();
        }

        private (List<RenderMeshArray> renderArrays, List<int> sharedIndices, List<int> sharedVersions) GetFilteredRenderMeshArrays(EntityManager em)
        {
            m_renderMeshArraysBuffer.Clear();
            m_sharedIndicesBuffer.Clear();
            m_sharedVersionsBuffer.Clear();

            var renderArrays = m_renderMeshArraysBuffer;
            var sharedIndices = m_sharedIndicesBuffer;
            var sharedVersions = m_sharedVersionsBuffer;

            em.GetAllUniqueSharedComponentsManaged(renderArrays, sharedIndices, sharedVersions);
            return (renderArrays, sharedIndices, sharedVersions);
        }

        private static void UnregisterMaterialsAndMeshesAndDispose(PolyRenderMeshArray polyRenderMeshArray)
        {
            foreach (var id in polyRenderMeshArray.Materials)
                PolySpatialCore.LocalAssetManager?.Unregister(id);
            foreach (var id in polyRenderMeshArray.Meshes)
                PolySpatialCore.LocalAssetManager?.Unregister(id);
            polyRenderMeshArray.Materials.Dispose();
            polyRenderMeshArray.Meshes.Dispose();
        }

        #endregion

        #region Jobs

        private partial struct HandleNewEntitiesJob : IJobEntity
        {
            public EntityCommandBuffer ECB;

            public TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialGameObjectData>> EntityTrackerMap;
            public NewEntityData NewEntityData;
            public bool visible;
            private void Execute(Entity e, LocalToWorld ltw)
            {
                ECB.AddComponent<TrackedEntity>(e);
                if (visible)
                    ECB.AddComponent<TrackedRendering>(e);

                // Ignored entities should never be part of this query
                Assert.IsFalse(EntityTrackerMap.IsIgnored(e));

                var trackingData = new EntityTrackingData<PolySpatialGameObjectData>();
                trackingData.Initialize(PolySpatialEntitiesUtils.IdFor(e), e);
                trackingData.customData = new PolySpatialGameObjectData
                {
                    active = true,
                    layer = 0,
                };


                NewEntityData.ids.Add(trackingData.InstanceId);
                NewEntityData.parentIds.Add(PolySpatialInstanceID.None);
                NewEntityData.positions.Add(ltw.Position);
                NewEntityData.rotations.Add(ltw.Rotation);
                NewEntityData.scales.Add(ltw.Value.Scale());
                NewEntityData.data.Add(trackingData.customData);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                trackingData.SetName(e.ToString());
#endif

                Assert.IsTrue(trackingData.GetLifecycleStage()==PolySpatialTrackingFlags.Created);
                trackingData.SetLifecycleStage(PolySpatialTrackingFlags.Running);

                EntityTrackerMap.Add(e, trackingData);
            }
        }

        private partial struct HandleRemovedEntitiesJob : IJobEntity
        {
            public EntityCommandBuffer ECB;
            public TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialGameObjectData>> EntityTrackerMap;
            public NativeList<PolySpatialInstanceID> EntitiesChanges;

            private void Execute(Entity e)
            {
                ECB.RemoveComponent<TrackedEntity>(e);
                // Ignored entities should never be part of this query
                Assert.IsFalse(EntityTrackerMap.IsIgnored(e));
                var trackingData = EntityTrackerMap[e];
                trackingData.MarkForDestruction();
                EntitiesChanges.Add(trackingData.InstanceId);
                EntityTrackerMap.Remove(e);
            }
        }

        private partial struct HandleTransformUpdatesJob : IJobEntity
        {
            public NativeArray<PolySpatialInstanceID> IdBuffer;
            public NativeArray<Vector3> PositionBuffer;
            public NativeArray<Quaternion> RotationBuffer;
            public NativeArray<Vector3> ScaleBuffer;
            public NativeArray<PolySpatialInstanceID> ParentBuffer;
            private void Execute([EntityIndexInQuery] int entityIndex, in Entity e, [ReadOnly] in LocalToWorld ltw)
            {
                var id = PolySpatialEntitiesUtils.IdFor(e);
                IdBuffer[entityIndex] = id;
                var mat = ltw.Value;
                PositionBuffer[entityIndex] = mat.Translation();
                RotationBuffer[entityIndex] = mat.Rotation();
                ScaleBuffer[entityIndex] = mat.Scale();
                ParentBuffer[entityIndex] = PolySpatialInstanceID.None;
            }
        }

        private struct HandleNewMaterialMeshInfosJob : IJobChunk
        {
            public EntityCommandBuffer ECB;
            [ReadOnly] public SharedComponentTypeHandle<RenderMeshArray> RenderMeshArray;
            public NativeParallelHashMap<int, PolyRenderMeshArray> PolyRenderMeshArrays;
            public TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialMeshMaterialTrackingData>> TrackerMap;
            public ChangeListSerializedStructWritable<PolySpatialRenderData> Changes;

            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo> MaterialMeshInfoType;
            [ReadOnly] public EntityTypeHandle EntityType;
            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                PolyRenderMeshArray polyRenderMeshArray = default;
                if (!PolyRenderMeshArrays.IsEmpty)
                {
                    var rmaIndex = chunk.GetSharedComponentIndex(RenderMeshArray);
                    if (rmaIndex >= 0)
                        PolyRenderMeshArrays.TryGetValue(rmaIndex, out polyRenderMeshArray);
                }

                var entities = chunk.GetNativeArray(EntityType);
                var mmis = chunk.GetNativeArray(ref MaterialMeshInfoType);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var e = entities[i];
                    ECB.AddComponent<TrackedMaterialMeshInfo>(e);
                    Assert.IsFalse(TrackerMap.IsIgnored(e));
                    if(!TrackerMap.TryGetValueOrDefault(e, out var trackingData))
                        trackingData.Initialize(PolySpatialEntitiesUtils.IdFor(e), e);
                    var mmi = mmis[i];
                    trackingData.customData.meshId = polyRenderMeshArray.GetMeshID(mmi);

                    var materialId = polyRenderMeshArray.GetMaterialID(mmi);
                    if (trackingData.customData.materials.materialIds.Length == 0)
                        trackingData.customData.materials.materialIds.Add(materialId);
                    else
                        trackingData.customData.materials.materialIds[0] = materialId;
                    TrackerMap.Add(e, trackingData);

                    Changes.Add(trackingData, new PolySpatialRenderData()
                    {
                        meshId = trackingData.customData.meshId,
                        renderingLayerMask = 1,
                        materialIds = PolySpatialUtils.GetNativeArrayForBuffer<PolySpatialAssetID>(
                            UnsafeUtility.AddressOf(ref trackingData.customData.materials.materialIds.ElementAt(0)),
                            trackingData.customData.materials.materialIds.Length)
                    });
                }
            }
        }

        private struct HandleRemovedMaterialMeshInfosJob : IJobChunk
        {
            public EntityCommandBuffer ECB;
            [ReadOnly] public EntityTypeHandle EntityType;

            public TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialMeshMaterialTrackingData>> TrackerMap;
            public NativeList<PolySpatialInstanceID> Changes;
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var e = entities[i];
                    ECB.RemoveComponent<TrackedMaterialMeshInfo>(e);
                    Assert.IsFalse(TrackerMap.IsIgnored(e));
                    var trackingData = TrackerMap[e];
                    trackingData.MarkForDestruction();
                    Changes.Add(trackingData.InstanceId);
                    TrackerMap.Remove(e);
                }
            }
        }

        private partial struct SetEnableRenderingEntitiesJob : IJobEntity
        {
            public EntityCommandBuffer ECB;
            public TrackerInstanceIdMap<Entity, EntityTrackingData<PolySpatialGameObjectData>> TrackerMap;
            public ChangeListStructWritable<PolySpatialGameObjectData> Changes;
            public bool visible;

            public void Execute(Entity e)
            {
                if (visible)
                    ECB.AddComponent<TrackedRendering>(e);
                else
                    ECB.RemoveComponent<TrackedRendering>(e);
                Assert.IsTrue(TrackerMap.ContainsKey(e));
                Assert.IsFalse(TrackerMap.IsIgnored(e));
                var trackingData = TrackerMap[e];
                trackingData.customData.active = visible;
                TrackerMap[e] = trackingData;
                Changes.Add(trackingData, trackingData.customData);
            }
        }

        #endregion

        private struct NewEntityData
        {
            public NewEntityData(Allocator allocator)
            {
                ids = new NativeList<PolySpatialInstanceID>(allocator);
                parentIds = new NativeList<PolySpatialInstanceID>(allocator);
                positions = new NativeList<Vector3>(allocator);
                rotations = new NativeList<Quaternion>(allocator);
                scales = new NativeList<Vector3>(allocator);
                data = new NativeList<PolySpatialGameObjectData>(allocator);
            }

            public void Dispose()
            {
                ids.Dispose();
                parentIds.Dispose();
                positions.Dispose();
                rotations.Dispose();
                scales.Dispose();
                data.Dispose();
            }

            public void Clear()
            {
                ids.Clear();
                parentIds.Clear();
                positions.Clear();
                rotations.Clear();
                scales.Clear();
                data.Clear();
            }

            public NativeList<PolySpatialInstanceID> ids;
            public NativeList<PolySpatialInstanceID> parentIds;
            public NativeList<Vector3> positions;
            public NativeList<Quaternion> rotations;
            public NativeList<Vector3> scales;
            public NativeList<PolySpatialGameObjectData> data;
        }
    }
}
