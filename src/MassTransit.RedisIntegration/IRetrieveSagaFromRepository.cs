using System;
using MassTransit.Saga;
using ServiceStack.Model;

namespace MassTransit.RedisIntegration
{
    public interface IRetrieveSagaFromRepository<out TSaga> where TSaga: ISaga, IHasGuidId
    {
        TSaga GetSaga(Guid correlationId);
    }
}
