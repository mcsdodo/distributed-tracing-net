public class Connections
{
    public RedisSettings Redis { get; set; }
    public KafkaSetitngs Kafka { get; set; }
    public Api Api { get; set; }
}

public class Api
{
    public Uri BaseUrl { get; set; }
}

public class KafkaSetitngs
{
    public string Brokers { get; set; }
    public string TopicName { get; set; }
}

public class RedisSettings
{
    public string ConnectionString { get; set; }
}