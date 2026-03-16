using System.Text.Json;
using FC.Engine.Domain.DataRecord;

namespace FC.Engine.Application.Services;

public static class SubmissionPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string Serialize(ReturnDataRecord record)
    {
        var payload = new
        {
            record.ReturnCode,
            record.TemplateVersionId,
            Category = record.Category.ToString(),
            Rows = record.Rows.Select(row => new
            {
                row.RowKey,
                Fields = row.AllFields
            })
        };

        return JsonSerializer.Serialize(payload, Options);
    }
}
