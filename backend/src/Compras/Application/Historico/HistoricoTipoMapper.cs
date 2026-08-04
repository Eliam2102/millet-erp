using System.Text.Json;
using Millet.Compras.Domain;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.Application.Historico;

/// <summary>
/// Mapea entradas de <see cref="AuditLogEntry"/> al
/// <see cref="HistoricoTipo"/> que el frontend muestra como tag/icono
/// (B.2). Inspecciona <c>(Operacion, Entidad, Cambios diff)</c> con
/// reglas explícitas; lo que no encaja queda como
/// <see cref="HistoricoTipo.Cambio"/> (genérico, no es error).
///
/// <para>
/// La fuente de verdad para los <c>estado</c> son los enteros del enum
/// <see cref="EstadoRequisicion"/>; al cambiar el enum, actualizar
/// también el switch interno (riesgo: el mapping se desincroniza
/// silenciosamente, los tests cubren las transiciones core).
/// </para>
/// </summary>
public static class HistoricoTipoMapper
{
    public static HistoricoTipo InferirTipo(AuditLogEntry entry)
    {
        return entry.Entidad switch
        {
            nameof(Requisicion) => InferirTipoRequisicion(entry),
            nameof(LineaRequisicion) => InferirTipoLinea(entry),
            nameof(Autorizacion) => InferirTipoAutorizacion(entry),
            _ => HistoricoTipo.Cambio,
        };
    }

    private static HistoricoTipo InferirTipoRequisicion(AuditLogEntry entry)
    {
        return entry.Operacion switch
        {
            "crear" => HistoricoTipo.Creada,
            "actualizar" => InferirTipoUpdateRequisicion(entry.Cambios),
            _ => HistoricoTipo.Cambio,
        };
    }

    private static HistoricoTipo InferirTipoUpdateRequisicion(string cambiosJson)
    {
        // El diff serializado por AuditSaveChangesInterceptor tiene shape
        // { "diff": { "Estado": { "antes": 0, "despues": 1 }, ... } }.
        var nuevoEstado = ExtraerEstadoDespues(cambiosJson);
        return nuevoEstado switch
        {
            (short)EstadoRequisicion.EnAutorizacion => HistoricoTipo.Transmitida,
            (short)EstadoRequisicion.Rechazada => HistoricoTipo.Rechazada,
            (short)EstadoRequisicion.EnSurtido => HistoricoTipo.CubrimientoRegistrado,
            (short)EstadoRequisicion.Cancelada => HistoricoTipo.Cancelada,
            (short)EstadoRequisicion.Cerrada => HistoricoTipo.Cerrada,
            (short)EstadoRequisicion.Eliminada => HistoricoTipo.Eliminada,
            _ => HistoricoTipo.Cambio,
        };
    }

    private static HistoricoTipo InferirTipoLinea(AuditLogEntry entry)
    {
        return entry.Operacion switch
        {
            "crear" => HistoricoTipo.LineaAgregada,
            "borrar" => HistoricoTipo.LineaEliminada,
            "actualizar" => InferirTipoUpdateLinea(entry.Cambios),
            _ => HistoricoTipo.Cambio,
        };
    }

    private static HistoricoTipo InferirTipoUpdateLinea(string cambiosJson)
    {
        // Si el diff incluye CantidadRecibida, fue una recepción; si solo
        // CantidadDeAlmacen/DeCompra, fue cubrimiento; otros campos →
        // edición estructural.
        var campos = ExtraerCamposModificados(cambiosJson);
        if (campos.Contains("CantidadRecibida")) return HistoricoTipo.RecepcionRegistrada;
        if (campos.Contains("CantidadDeAlmacen") || campos.Contains("CantidadDeCompra"))
            return HistoricoTipo.CubrimientoRegistrado;
        return HistoricoTipo.LineaActualizada;
    }

    private static HistoricoTipo InferirTipoAutorizacion(AuditLogEntry entry)
    {
        if (entry.Operacion != "crear") return HistoricoTipo.Cambio;

        // El snapshot de creación tiene shape { "snapshot": { "Nivel": 1 | 2, ... } }.
        var nivel = ExtraerSnapshotInt(entry.Cambios, "Nivel");
        return nivel switch
        {
            (short)NivelAutorizacion.Nivel1 => HistoricoTipo.AutorizadaN1,
            (short)NivelAutorizacion.Nivel2 => HistoricoTipo.AutorizadaN2,
            _ => HistoricoTipo.Cambio,
        };
    }

    // --- Helpers para extraer campos del JSON ---

    private static short? ExtraerEstadoDespues(string cambiosJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(cambiosJson);
            if (!doc.RootElement.TryGetProperty("diff", out var diff)) return null;
            if (!diff.TryGetProperty("Estado", out var estadoChange)) return null;
            if (!estadoChange.TryGetProperty("despues", out var despues)) return null;
            // EF serializa enums como int en el snapshot; conviene leer como int.
            return despues.ValueKind == JsonValueKind.Number ? (short)despues.GetInt32() : null;
        }
        catch (JsonException) { return null; }
    }

    private static HashSet<string> ExtraerCamposModificados(string cambiosJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(cambiosJson);
            if (!doc.RootElement.TryGetProperty("diff", out var diff)) return new HashSet<string>();
            return diff.EnumerateObject().Select(p => p.Name).ToHashSet();
        }
        catch (JsonException) { return new HashSet<string>(); }
    }

    private static short? ExtraerSnapshotInt(string cambiosJson, string campo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cambiosJson);
            if (!doc.RootElement.TryGetProperty("snapshot", out var snapshot)) return null;
            if (!snapshot.TryGetProperty(campo, out var valor)) return null;
            return valor.ValueKind == JsonValueKind.Number ? (short)valor.GetInt32() : null;
        }
        catch (JsonException) { return null; }
    }
}
