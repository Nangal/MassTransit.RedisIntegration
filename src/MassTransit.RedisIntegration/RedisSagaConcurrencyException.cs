using System;

namespace MassTransit.RedisIntegration
{
    public class RedisSagaConcurrencyException : MassTransitException
    {
        public RedisSagaConcurrencyException() { }

        public RedisSagaConcurrencyException(string message) : base(message) { }

        public RedisSagaConcurrencyException(string message, Exception inner) : base(message, inner) { }
    }
}
