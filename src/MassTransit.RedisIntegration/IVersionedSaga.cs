using MassTransit.Saga;

namespace MassTransit.RedisIntegration
{
    public interface IVersionedSaga : ISaga
    {
        int Version { get; set; }
    }
}