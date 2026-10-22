using System.Text.Json;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.Application.Oc.Analisis;

internal static class CancelacionAnalisis
{
    public static DateTimeOffset? Fecha(IEnumerable<AuditLogEntry> auditoria)
    {
        foreach (var entrada in auditoria.OrderBy(a => a.Timestamp))
        {
            using var json = JsonDocument.Parse(entrada.Cambios);
            if (json.RootElement.TryGetProperty("diff", out var diff)
                && diff.TryGetProperty("Estado", out var estado)
                && estado.TryGetProperty("despues", out var valor)
                && valor.ValueKind == JsonValueKind.Number
                && valor.GetInt32() == (int)Domain.Oc.EstadoOrdenCompra.Cancelada)
                return entrada.Timestamp;
        }
        return null;
    }
}
