using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IAfcftaTrackingService
{
    Task<IReadOnlyList<AfcftaProtocolDto>> ListProtocolsAsync(
        CancellationToken ct = default);

    Task<AfcftaProtocolDto?> GetProtocolAsync(
        string protocolCode, CancellationToken ct = default);

    Task UpdateProtocolStatusAsync(
        string protocolCode, Enums.AfcftaProtocolStatus newStatus,
        int userId, CancellationToken ct = default);
}
