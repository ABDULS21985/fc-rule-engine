using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IMacroShockTransmitter
{
    EntityShockResult ApplyShock(
        PrudentialMetricSnapshot preStress,
        ResolvedShockParameters  parameters);
}
