using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface IBoardPackGenerator
{
    Task<byte[]> Generate(
        List<BoardPackSection> sections,
        BrandingConfig branding,
        string title,
        CancellationToken ct = default);
}
