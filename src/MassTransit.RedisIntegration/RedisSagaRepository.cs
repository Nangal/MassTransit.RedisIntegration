using System;
using System.Threading.Tasks;
using MassTransit.Logging;
using MassTransit.Pipeline;
using MassTransit.Saga;
using MassTransit.Util;
using ServiceStack.Model;
using ServiceStack.Redis;
using ServiceStack.Redis.Generic;

namespace MassTransit.RedisIntegration
{
    public class RedisSagaRepository<TSaga> : ISagaRepository<TSaga> where TSaga : class, IVersionedSaga, IHasGuidId
    {
        private static readonly ILog _log = Logger.Get<RedisSagaRepository<TSaga>>();
        private readonly IRedisClientsManager _clientsManager;

        public RedisSagaRepository(IRedisClientsManager clientsManager)
        {
            _clientsManager = clientsManager;
        }

        public async Task Send<T>(ConsumeContext<T> context, ISagaPolicy<TSaga, T> policy,
            IPipe<SagaConsumeContext<TSaga, T>> next) where T : class
        {
            if (!context.CorrelationId.HasValue)
                throw new SagaException("The CorrelationId was not specified", typeof(TSaga), typeof(T));

            var sagaId = context.CorrelationId.Value;
            TSaga instance;
            using (var redis = _clientsManager.GetClient())
            {
                var sagas = redis.As<TSaga>();

                if (policy.PreInsertInstance(context, out instance))
                    await PreInsertSagaInstance<T>(sagas, instance).ConfigureAwait(false);

                if (instance == null)
                    instance = sagas.GetById(sagaId);
            }

            if (instance == null)
            {
                var missingSagaPipe = new MissingPipe<T>(_clientsManager, next);
                await policy.Missing(context, missingSagaPipe).ConfigureAwait(false);
            }
            else
            {
                await SendToInstance(context, policy, next, instance).ConfigureAwait(false);
            }
        }

        public Task SendQuery<T>(SagaQueryConsumeContext<TSaga, T> context, ISagaPolicy<TSaga, T> policy,
            IPipe<SagaConsumeContext<TSaga, T>> next) where T : class
        {
            throw new NotImplementedByDesignException("Redis saga repository does not support queries");
        }

        public void Probe(ProbeContext context)
        {
            var scope = context.CreateScope("sagaRepository");
            scope.Set(new
            {
                Persistence = "redis"
            });
        }

        async Task SendToInstance<T>(ConsumeContext<T> context, ISagaPolicy<TSaga, T> policy,
            IPipe<SagaConsumeContext<TSaga, T>> next, TSaga instance)
            where T : class
        {
            try
            {
                if (_log.IsDebugEnabled)
                    _log.DebugFormat("SAGA:{0}:{1} Used {2}", TypeMetadataCache<TSaga>.ShortName, instance.CorrelationId, TypeMetadataCache<T>.ShortName);

                var sagaConsumeContext = new RedisSagaConsumeContext<TSaga, T>(_clientsManager, context, instance);

                await policy.Existing(sagaConsumeContext, next).ConfigureAwait(false);

                if (!sagaConsumeContext.IsCompleted)
                    UpdateRedisSaga(instance);
            }
            catch (SagaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SagaException(ex.Message, typeof(TSaga), typeof(T), instance.CorrelationId, ex);
            }
        }

        private static Task<bool> PreInsertSagaInstance<T>(IRedisTypedClient<TSaga> sagas, TSaga instance)
        {
            try
            {
                sagas.Store(instance);

                if (_log.IsDebugEnabled)
                    _log.DebugFormat("SAGA:{0}:{1} Insert {2}", TypeMetadataCache<TSaga>.ShortName, instance.CorrelationId,
                        TypeMetadataCache<T>.ShortName);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                if (_log.IsDebugEnabled)
                    _log.DebugFormat("SAGA:{0}:{1} Dupe {2} - {3}", TypeMetadataCache<TSaga>.ShortName,
                        instance.CorrelationId,
                        TypeMetadataCache<T>.ShortName, ex.Message);

                return Task.FromResult(false);
            }
        }

        private void UpdateRedisSaga(TSaga instance)
        {
            using (var redis = _clientsManager.GetClient())
            {
                var sagas = redis.As<TSaga>();

                instance.Version++;
                var old = sagas.GetById(instance.Id);
                if (old.Version > instance.Version)
                    throw new RedisSagaConcurrencyException($"Version conflict for saga with id {instance.Id}");

                sagas.Store(instance);
            }
        }

        /// <summary>
        ///     Once the message pipe has processed the saga instance, add it to the saga repository
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        private class MissingPipe<TMessage> :
            IPipe<SagaConsumeContext<TSaga, TMessage>>
            where TMessage : class
        {
            private readonly IPipe<SagaConsumeContext<TSaga, TMessage>> _next;
            private readonly IRedisClientsManager _redis;

            public MissingPipe(IRedisClientsManager redis, IPipe<SagaConsumeContext<TSaga, TMessage>> next)
            {
                _redis = redis;
                _next = next;
            }

            void IProbeSite.Probe(ProbeContext context)
            {
                _next.Probe(context);
            }

            public async Task Send(SagaConsumeContext<TSaga, TMessage> context)
            {
                if (_log.IsDebugEnabled)
                {
                    _log.DebugFormat("SAGA:{0}:{1} Added {2}", TypeMetadataCache<TSaga>.ShortName,
                        context.Saga.CorrelationId,
                        TypeMetadataCache<TMessage>.ShortName);
                }

                SagaConsumeContext<TSaga, TMessage> proxy = new RedisSagaConsumeContext<TSaga, TMessage>(_redis,
                    context, context.Saga);

                await _next.Send(proxy).ConfigureAwait(false);

                if (!proxy.IsCompleted)
                    using (var client = _redis.GetClient())
                        client.As<TSaga>().Store(context.Saga);
            }
        }
    }

}