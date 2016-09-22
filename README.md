# MassTransit.RedisIntegration
Redis Saga persistence for MassTransit

### Disclaimer
This library uses `ServiceStack.Redis`, which requires a licence for v4. It has been only tested with ServiceStack 4 but I would expect it to work with v3 as well.

### Usage

The repository constructor requires `ServiceStack.Redis.IRedisClientsManager` as a parameter. You can either specify it directly in your endpoint configuration, or inject if you are using some DI container.

Refer to [ServiceStack.Redis documentation](https://github.com/ServiceStack/ServiceStack.Redis#redis-client-managers) to know more about the clients manager configuration.

### Using with Autofac sample
```c#
    builder.Register<IRedisClientsManager>
        (c => new RedisManagerPool(redisConnectionString));
    builder.RegisterGeneric(typeof(RedisSagaRepository<>))
        .As(typeof(ISagaRepository<>));
```
