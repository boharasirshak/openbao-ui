namespace ControlPlane.Infrastructure.OpenBao;

public sealed class OpenBaoOptions
{
    public const string SectionName = "OpenBao";
    public required Uri Address { get; init; }
    public string? ControlToken { get; init; }
    public string MetadataMount { get; init; } = "wrapper-metadata";
    public TimeSpan SessionSafetyMargin { get; init; } = TimeSpan.FromMinutes(1);
}
