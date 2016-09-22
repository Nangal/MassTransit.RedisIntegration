using System;
using MassTransit.Saga;
using ServiceStack.Model;

namespace MassTransit.RedisIntegration.Tests
{
    public class TestSaga : ISaga, IHasGuidId
    {
        public Guid CorrelationId { get; set; }
        public Guid Id => CorrelationId;
    }
}