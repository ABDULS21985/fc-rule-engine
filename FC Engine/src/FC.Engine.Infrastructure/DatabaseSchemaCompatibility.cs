using Microsoft.Data.SqlClient;

namespace FC.Engine.Infrastructure;

public static class DatabaseSchemaCompatibility
{
    public static bool IsMissingSchemaObject(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number == 208)
            {
                return true;
            }

            if (current.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
