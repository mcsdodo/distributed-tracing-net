
public record Message
{
    public DateTime CreatedOn { get; set; }
    public string? Content { get; set; }
}

public record TracedMessage : Message
{
    public string PropagationContext { get; set; }
}