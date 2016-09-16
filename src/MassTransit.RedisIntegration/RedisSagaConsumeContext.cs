using System;
using System.Threading.Tasks;
using MassTransit.Context;
using MassTransit.Logging;
using MassTransit.Util;
using ServiceStack.Model;
using ServiceStack.Redis;

namespace MassTransit.RedisIntegration
{
    public class RedisSagaConsumeContext<TSaga, TMessage> :
        ConsumeContextProxyScope<TMessage>,
        SagaConsumeContext<TSaga, TMessage>
        where TMessage : class
        where TSaga : class, IVersionedSaga, IHasGuidId
    {
        private static readonly ILog Log = Logger.Get<RedisSagaRepository<TSaga>>();
        private readonly IRedisClientsManager _redis;

        public RedisSagaConsumeContext(IRedisClientsManager redis, ConsumeContext<TMessage> context, TSaga instance)
            : base(context)
        {
            Saga = instance;
            _redis = redis;
        }

        Guid? MessageContext.CorrelationId => Saga.CorrelationId;

        SagaConsumeContext<TSaga, T> SagaConsumeContext<TSaga>.PopContext<T>()
        {
            var context = this as SagaConsumeContext<TSaga, T>;
            if (context == null)
                throw new ContextException($"The ConsumeContext<{TypeMetadataCache<TMessage>.ShortName}> could not be cast to {TypeMetadataCache<T>.ShortName}");

            return context;
        }

        Task SagaConsumeContext<TSaga>.SetCompleted()
        {
            using (var client = _redis.GetClient())
               client.As<TSaga>().Delete(Saga);

            IsCompleted = true;
            if (Log.IsDebugEnabled)
                Log.DebugFormat("SAGA:{0}:{1} Removed {2}", TypeMetadataCache<TSaga>.ShortName, TypeMetadataCache<TMessage>.ShortName,
                    Saga.CorrelationId);

            return TaskUtil.Completed;
        }

        public TSaga Saga { get; }
        public bool IsCompleted { get; private set; }
    }
}