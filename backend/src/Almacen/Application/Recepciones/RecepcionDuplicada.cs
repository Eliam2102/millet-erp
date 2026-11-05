using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// G-3: antes de registrar, busca otra recepción vigente de la misma OC con la misma remisión o factura (CFDI).
/// Si existe, exige un motivo; sin motivo responde <c>RECEPCION_YA_RECIBIDA</c> (la pantalla pide confirmar).
/// Se llama con el lock por OC ya tomado, así dos capturas simultáneas no se cuelan. Las canceladas no cuentan.
/// </summary>
internal static class RecepcionDuplicada
{
    public const int RemisionMaxLength = 50;

    /// <summary>Devuelve la remisión normalizada (recortada, en mayúsculas) y el motivo solo si de verdad hubo duplicado.</summary>
    public static async Task<(string? Remision, string? Motivo)> ValidarAsync(
        AlmacenDbContext db, Guid ordenCompraId, string? remision, Guid? cfdiRecibidoId, string? cfdiUuidFiscal,
        string? motivo, CancellationToken ct)
    {
        remision = string.IsNullOrWhiteSpace(remision) ? null : remision.Trim().ToUpperInvariant();
        var uuid = string.IsNullOrWhiteSpace(cfdiUuidFiscal) ? null : cfdiUuidFiscal.Trim().ToUpperInvariant();
        if (remision is null && cfdiRecibidoId is null && uuid is null) return (null, null);

        // El movimiento de material en revisión (RecepcionOrigenId) es hermano de la recepción principal, no otra recepción.
        var previa = await db.Movimientos.AsNoTracking()
            .Where(m => m.OcId == ordenCompraId && m.Tipo == TipoMovimiento.EntradaCompra
                && m.Estado != EstadoMovimiento.Cancelado && m.RecepcionOrigenId == null
                && ((remision != null && m.Remision == remision)
                    || (cfdiRecibidoId != null && m.CfdiRecibidoId == cfdiRecibidoId)
                    || (uuid != null && m.CfdiUuidFiscal == uuid)))
            .Select(m => new { m.Folio })
            .FirstOrDefaultAsync(ct);
        if (previa is null) return (remision, null);

        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo))
            throw new BusinessRuleException("RECEPCION_YA_RECIBIDA",
                $"Esta remisión o factura ya fue recibida en la orden (recepción {previa.Folio}). Para registrarla otra vez captura el motivo.");
        return (remision, motivo);
    }
}
