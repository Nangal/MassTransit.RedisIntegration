using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MassTransit.Saga;
using MassTransit.TestFramework;
using NUnit.Framework;
using RedisInside;
using ServiceStack.Redis;
using Shouldly;

namespace MassTransit.RedisIntegration.Tests
{
    [TestFixture, Category("Integration")]
    public class LocatingAnExistingSaga : InMemoryTestFixture
    {


        [OneTimeTearDown]
        public void TearDownRedis() => _redis.Dispose(); 

        [Test]
        public async Task A_correlated_message_should_find_the_correct_saga()
        {
            Guid sagaId = NewId.NextGuid();
            var message = new InitiateSimpleSaga(sagaId);

            await InputQueueSendEndpoint.Send(message).ConfigureAwait(false);

            var found = _sagaRepository.Value.ShouldContainSaga(message.CorrelationId, TestTimeout);

            found.ShouldBeTrue();

            var nextMessage = new CompleteSimpleSaga { CorrelationId = sagaId };

            await InputQueueSendEndpoint.Send(nextMessage).ConfigureAwait(false);

            found = await _sagaRepository.Value.ShouldContainSaga(sagaId, x => x.Completed, TestTimeout).ConfigureAwait(false);
            found.ShouldBeTrue();
            var retrieveRepository = _sagaRepository.Value as IRetrieveSagaFromRepository<SimpleSaga>;
            var retrieved = retrieveRepository.GetSaga(sagaId);
            retrieved.ShouldNotBeNull();
            retrieved.Completed.ShouldBeTrue();
        }

        [Test]
        public void An_initiating_message_should_start_the_saga()
        {
            Guid sagaId = NewId.NextGuid();
            var message = new InitiateSimpleSaga(sagaId);

            InputQueueSendEndpoint.Send(message);

            var found = _sagaRepository.Value.ShouldContainSaga(message.CorrelationId, TestTimeout);

            found.ShouldBeTrue();
        }

        readonly Lazy<ISagaRepository<SimpleSaga>> _sagaRepository;
        private Redis _redis;

        public LocatingAnExistingSaga()
        {
            _redis = new Redis();
            var clientManager = new BasicRedisClientManager(_redis.Endpoint.ToString());
            _sagaRepository = new Lazy<ISagaRepository<SimpleSaga>>(() => new RedisSagaRepository<SimpleSaga>(clientManager));
        }

        protected override void ConfigureInputQueueEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
        {
            configurator.Saga(_sagaRepository.Value);
        }
    }
}