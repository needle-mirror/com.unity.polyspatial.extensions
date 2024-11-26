using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.PolySpatial.Entities;
using Unity.PolySpatial.Internals;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Unity.PolySpatial.Extensions.RuntimeTests
{
    /// <summary>
    /// Tests to cover the PolySpatial Entity support.
    /// </summary>
    [TestFixture]
    public class EntitiesTests
    {
        struct SpawnJob : IJobParallelFor
        {
            internal Entity Prototype;
            internal EntityCommandBuffer.ParallelWriter Ecb;
            internal NativeArray<Entity> EntityArray;

            /// <summary>
            /// Function for IJobParallelFor interface to execute the spawn job.
            /// </summary>
            /// <param name="index">Index of the entity</param>
            public void Execute(int index)
            {
                // Clone the Prototype entity to create a new entity.
                var e = Ecb.Instantiate(index, Prototype);
                // Prototype has all correct components up front, can use SetComponent to
                // set values unique to the newly created entity, such as the transform.
                Ecb.SetComponent(index, e, new LocalToWorld {Value = ComputeTransform(index)});
                EntityArray[index] = e;
            }

            public float4x4 ComputeTransform(int index)
            {
                return float4x4.Translate(new float3(index, 0, 0));
            }
        }

        struct DestroyEntitiesJob : IJobParallelFor
        {
            internal EntityCommandBuffer.ParallelWriter Ecb;
            internal NativeArray<Entity> EntityArray;

            /// <summary>
            /// Function for IJobParallelFor interface to execute the destroy job.
            /// </summary>
            /// <param name="index">Index of the entity</param>
            public void Execute(int index)
            {
                Ecb.DestroyEntity(index, EntityArray[index]);
            }
        }

        /// <summary>
        /// Tests spawning and destroying entities in jobs from a prototype entity that has a material and mesh.
        /// Asserts that the spawned entities have valid instance IDs.
        /// </summary>
        /// <returns>The IEnumerator for the test runner</returns>
        [UnityTest]
        public IEnumerator SpawnAndDestroyEntities()
        {
            // Need to wait a frame as PolySpatialEntitiesSystem.OnUpdate() is called before the first call to PolySpatialCore.PolySpatialAfterLateUpdate
            yield return null;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var entityCount = 5;

            var previousPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop());

            var previousWorld = World.DefaultGameObjectInjectionWorld;
            var world = World.DefaultGameObjectInjectionWorld = new World("Test World");
            world.UpdateAllocatorEnableBlockFree = true;

            // Many ECS tests will only pass if the Jobs Debugger enabled;
            // force it enabled for all tests, and restore the original value at teardown.
            var jobsDebuggerWasEnabled = JobsUtility.JobDebuggerEnabled;
            JobsUtility.JobDebuggerEnabled = true;

            var entityManager = world.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            // Create a RenderMeshDescription using the convenience constructor
            // with named parameters.
            var desc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.Off,
                receiveShadows: false);

            // Create an array of mesh and material required for runtime rendering.
            var renderMeshArray = new RenderMeshArray(new Material[] { material }, new Mesh[] { mesh });

            // Create empty base entity
            var prototype = entityManager.CreateEntity();

            // Call AddComponents to populate base entity with the components required
            // by Entities Graphics
            RenderMeshUtility.AddComponents(
                prototype,
                entityManager,
                desc,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            entityManager.AddComponentData(prototype, new LocalToWorld());
            var spawnedEntities = new NativeArray<Entity>(entityCount, Allocator.Persistent);

            // Spawn most of the entities in a Burst job by cloning a pre-created prototype entity,
            // which can be either a Prefab or an entity created at run time like in this sample.
            // This is the fastest and most efficient way to create entities at run time.
            var spawnJob = new SpawnJob
            {
                Prototype = prototype,
                Ecb = ecb.AsParallelWriter(),
                EntityArray = spawnedEntities
            };

            var spawnHandle = spawnJob.Schedule(entityCount, 1);
            spawnHandle.Complete();

            yield return null;

            for (var i = 0; i < entityCount; i++)
            {
                Debug.Log(spawnedEntities[i].Version);
                Assert.IsTrue(PolySpatialEntitiesUtils.IdFor(spawnedEntities[i]) != PolySpatialInstanceID.None);
            }

            var destroyJob = new DestroyEntitiesJob
            {
                Ecb = ecb.AsParallelWriter(),
                EntityArray = spawnedEntities
            };

            var destroyHandle = destroyJob.Schedule(entityCount, 1, spawnHandle);
            destroyHandle.Complete();

            // For some reason this line causes Invalid Entity Version exceptions, but seems to function fine without it.
            //ecb.Playback(entityManager);
            ecb.Dispose();
            entityManager.DestroyEntity(prototype);

            entityManager.CompleteAllTrackedJobs();

            world.DestroyAllSystemsAndLogException(out bool errorsWhileDestroyingSystems);
            Assert.IsFalse(errorsWhileDestroyingSystems,
                "One or more exceptions were thrown while destroying systems during test teardown; consult the log for details.");

            world.Dispose();
            yield return null;

            JobsUtility.JobDebuggerEnabled = jobsDebuggerWasEnabled;
            World.DefaultGameObjectInjectionWorld = previousWorld;
            PlayerLoop.SetPlayerLoop(previousPlayerLoop);
        }
    }
}

