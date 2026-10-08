namespace DndEngine.Infrastructure;

public sealed class IdempotencyOperationRow
{
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public byte[] ResponseBody { get; set; } = [];
    public string? ContentType { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
