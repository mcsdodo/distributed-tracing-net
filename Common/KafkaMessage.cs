
public record KafkaMessage
{
    public DateTime CreatedOn { get; set; }
    public string? Message { get; set; }
}