# Sample project showcasing OTel distributed tracing with .NET

Purpose of this is to see data journey across the application using multiple messaging / caching technologies.

1. [ApiCaller.Worker](ApiCaller.Worker) runs in an infinite loop and calls [KafkaWriter.Api](KafkaWriter.Api)
2. [KafkaWriter.Api](KafkaWriter.Api) has a single API endpoint that fans-out some data - Kafka, Redis cache, Redis streams
3. [KafkaReader.Worker](KafkaReader.Worker) reads data from Kafka
4. [RedisCacheReader.Worker](RedisCacheReader.Worker) reads data from Redis cache by key
5. [RedisStreamReader.Worker](RedisStreamReader.Worker) reads data from Redis Streams


### Caveats:
The app does not plug in to StackExchange.Redis - we miss XREADGROUP and GET redis calls. You cannot set parentId once an Activity is started as [discussed here](https://github.com/dotnet/runtime/issues/63883). The [RedisCacheService.Get<T> method](Common.Redis/RedisCacheService.cs#L31) tries this approach.