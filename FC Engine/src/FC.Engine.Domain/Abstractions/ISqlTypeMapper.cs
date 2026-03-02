using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface ISqlTypeMapper
{
    string MapToSqlType(FieldDataType dataType, string? sqlTypeOverride = null);
}
