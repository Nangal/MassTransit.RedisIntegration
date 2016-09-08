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
    public class RedisSagaRepository<TSaga> : ISagaRepository<TSaga> where TSaga : class, ISaga, IHasGuidId
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
            using (var redis = _clientsManager.GetClient())
            {
                var sagas = redis.As<TSaga>();
                TSaga instance;

                if (policy.PreInsertInstance(context, out instance))
                    await PreInsertSagaInstance<T>(sagas, instance).ConfigureAwait(false);

                if (instance == null)
                    instance = sagas.GetById(sagaId);

                if (instance == null)
                {
                    var missingSagaPipe = new MissingPipe<T>(sagas, next);
                    await policy.Missing(context, missingSagaPipe).ConfigureAwait(false);
                }
                else
                {
                    await SendToInstance(context, policy, next, instance, sagas).ConfigureAwait(false);
                }
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
            IPipe<SagaConsumeContext<TSaga, T>> next, TSaga instance, IRedisTypedClient<TSaga> sagas)
            where T : class
        {
            try
            {
                if (_log.IsDebugEnabled)
                    _log.DebugFormat("SAGA:{0}:{1} Used {2}", TypeMetadataCache<TSaga>.ShortName, instance.CorrelationId, TypeMetadataCache<T>.ShortName);

                var sagaConsumeContext = new RedisSagaConsumeContext<TSaga, T>(sagas, context, instance);

                await policy.Existing(sagaConsumeContext, next).ConfigureAwait(false);

                if (!sagaConsumeContext.IsCompleted)
                {
                    sagas.Store(instance);
                    if (_log.IsDebugEnabled)
                        _log.DebugFormat("SAGA (Send): New saga state: {@Saga}", instance);
                }
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

        /// <summary>
        ///     Once the message pipe has processed the saga instance, add it to the saga repository
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        private class MissingPipe<TMessage> :
            IPipe<SagaConsumeContext<TSaga, TMessage>>
            where TMessage : class
        {
            private readonly IPipe<SagaConsumeContext<TSaga, TMessage>> _next;
            private readonly IRedisTypedClient<TSaga> _sagas;

            public MissingPipe(IRedisTypedClient<TSaga> sagas, IPipe<SagaConsumeContext<TSaga, TMessage>> next)
            {
                _sagas = sagas;
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

                SagaConsumeContext<TSaga, TMessage> proxy = new RedisSagaConsumeContext<TSaga, TMessage>(_sagas,
                    context, context.Saga);

                await _next.Send(proxy).ConfigureAwait(false);

                if (!proxy.IsCompleted)
                    _sagas.Store(context.Saga);
            }
        }
    }

}