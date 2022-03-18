using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

[GenerateAuthoringComponent]
[GhostComponent(OwnerSendType = SendToOwnerType.SendToNonOwner)]
public struct PredictionSwitchingInput : ICommandData
{
    [GhostField] public uint Tick{get; set;}
    [GhostField] public int horizontal;
    [GhostField] public int vertical;
}

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class PredictionSwitchingSampleInputSystem : SystemBase
{
    private ClientSimulationSystemGroup m_ClientSimulationSystemGroup;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
        RequireSingletonForUpdate<NetworkIdComponent>();
        m_ClientSimulationSystemGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
    }

    protected override void OnUpdate()
    {
        var myConnection = GetSingleton<NetworkIdComponent>().Value;
        var tick = m_ClientSimulationSystemGroup.ServerTick;
        var connection = GetSingletonEntity<CommandTargetComponent>();
        Entities
            .WithoutBurst()
            .ForEach((Entity entity, DynamicBuffer<PredictionSwitchingInput> inputBuffer, in GhostOwnerComponent owner) => {
            if (owner.NetworkId == myConnection)
            {
                var input = default(PredictionSwitchingInput);
                input.Tick = tick;
                if (UnityEngine.Input.GetKey("left") || TouchInput.GetKey(TouchInput.KeyCode.Left))
                    input.horizontal -= 1;
                if (UnityEngine.Input.GetKey("right") || TouchInput.GetKey(TouchInput.KeyCode.Right))
                    input.horizontal += 1;
                if (UnityEngine.Input.GetKey("down") || TouchInput.GetKey(TouchInput.KeyCode.Down))
                    input.vertical -= 1;
                if (UnityEngine.Input.GetKey("up") || TouchInput.GetKey(TouchInput.KeyCode.Up))
                    input.vertical += 1;
                inputBuffer.AddCommandData(input);
                if (EntityManager.GetComponentData<CommandTargetComponent>(connection).targetEntity == Entity.Null)
                {
                    EntityManager.SetComponentData(connection, new CommandTargetComponent{targetEntity = entity});
                }
            }
        }).Run();
    }
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BuildPhysicsWorld))]
public partial class PredictionSwitchingApplyInputSystem : SystemBase
{
    private GhostPredictionSystemGroup m_GhostPredictionSystemGroup;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
        m_GhostPredictionSystemGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
    }

    protected override void OnUpdate()
    {
        var tick = m_GhostPredictionSystemGroup.PredictingTick;
        var deltaTime = Time.DeltaTime;
        float speed = 5;
        Entities
            .ForEach((DynamicBuffer<PredictionSwitchingInput> inputBuffer, ref PhysicsVelocity vel, in PredictedGhostComponent prediction) => {
            if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
                return;
            inputBuffer.GetDataAtTick(tick, out var input);
            float3 dir = default;
            if (input.horizontal > 0)
                dir.x += 1;
            if (input.horizontal < 0)
                dir.x -= 1;
            if (input.vertical > 0)
                dir.z += 1;
            if (input.vertical < 0)
                dir.z -= 1;
            if (math.lengthsq(dir) > 0.5)
            {
                dir = math.normalize(dir);
                dir *= speed;
            }
            vel.Linear.x = dir.x;
            vel.Linear.z = dir.z;
        }).Schedule();
    }
}

[UpdateAfter(typeof(TransformSystemGroup))]
[UpdateInWorld(TargetWorld.Client)]
public partial class PredictionSwitchingCameraFollowSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
        RequireSingletonForUpdate<NetworkIdComponent>();
    }

    protected override void OnUpdate()
    {
        var myConnection = GetSingleton<NetworkIdComponent>().Value;
        Entities
            .WithoutBurst()
            .ForEach((DynamicBuffer<PredictionSwitchingInput> inputBuffer, ref Translation trans, in GhostOwnerComponent owner) => {
            if (owner.NetworkId != myConnection)
                return;
            UnityEngine.Camera.main.transform.position = trans.Value + new float3(0, 1, -10);
        }).Run();
    }
}
