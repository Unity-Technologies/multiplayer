
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;

[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
public class NetworkStreamCloseSystem : JobComponentSystem
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    [RequireComponentTag(typeof(NetworkStreamDisconnected))]
    struct CloseJob : IJobForEachWithEntity<NetworkStreamConnection>
    {
        public EntityCommandBuffer commandBuffer;
        public void Execute(Entity entity, int index, [ReadOnly] ref NetworkStreamConnection con)
        {
            commandBuffer.DestroyEntity(entity);
        }
    }

    protected override void OnCreateManager()
    {
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var job = new CloseJob{commandBuffer = m_Barrier.CreateCommandBuffer()};
        var handle = job.ScheduleSingle(this, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }
}

