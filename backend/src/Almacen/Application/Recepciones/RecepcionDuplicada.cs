using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Application.Incidencias;
using Millet.SharedKernel.Application.Incidencias;

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
        string? motivo, CancellationToken ct, string? ocFolio = null, Guid? sucursalId = null,
        ICxpDocumentosReadPort? cxpDocumentos = null)
    {
        remision = string.IsNullOrWhiteSpace(remision) ? null : remision.Trim().ToUpperInvariant();
        var uuid = string.IsNullOrWhiteSpace(cfdiUuidFiscal) ? null : cfdiUuidFiscal.Trim().ToUpperInvariant();
        if (remision is null && cfdiRecibidoId is null && uuid is null) return (null, null);
        // La misma factura llega por dos canales: CFDI del repositorio de CxP (id) o folio fiscal impreso (UUID).
        // Con el puerto se compara por UUID en ambos sentidos.
        if (uuid is null && cfdiRecibidoId is Guid idNuevo && cxpDocumentos is not null
            && (await cxpDocumentos.ObtenerUuidsFiscalesCfdiAsync([idNuevo], ct)).TryGetValue(idNuevo, out var uuidNuevo))
            uuid = uuidNuevo.Trim().ToUpperInvariant();

        // El movimiento de material en revisión (RecepcionOrigenId) es hermano de la recepción principal, no otra recepción.
        var vigentes = db.Movimientos.AsNoTracking()
            .Where(m => m.OcId == ordenCompraId && m.Tipo == TipoMovimiento.EntradaCompra
                && m.Estado != EstadoMovimiento.Cancelado && m.RecepcionOrigenId == null);
        var previa = await vigentes
            .Where(m => (remision != null && m.Remision == remision)
                    || (cfdiRecibidoId != null && m.CfdiRecibidoId == cfdiRecibidoId)
                    || (uuid != null && m.CfdiUuidFiscal == uuid))
            .Select(m => new { m.Folio, m.Remision, m.CfdiUuidFiscal })
            .FirstOrDefaultAsync(ct);
        if (previa is null && uuid is not null && cxpDocumentos is not null)
        {
            var conCfdi = await vigentes.Where(m => m.CfdiRecibidoId != null && m.CfdiRecibidoId != cfdiRecibidoId)
                .Select(m => new { m.Folio, m.Remision, m.CfdiUuidFiscal, CfdiId = m.CfdiRecibidoId!.Value })
                .ToListAsync(ct);
            if (conCfdi.Count > 0)
            {
                var uuids = await cxpDocumentos.ObtenerUuidsFiscalesCfdiAsync(conCfdi.Select(m => m.CfdiId).Distinct().ToList(), ct);
                var mismo = conCfdi.FirstOrDefault(m => uuids.TryGetValue(m.CfdiId, out var u)
                    && string.Equals(u.Trim(), uuid, StringComparison.OrdinalIgnoreCase));
                if (mismo is not null) previa = new { mismo.Folio, mismo.Remision, mismo.CfdiUuidFiscal };
            }
        }
        if (previa is null) return (remision, null);

        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo))
        {
            // N-11: además del aviso, queda la incidencia de la OC (se resuelve con la siguiente recepción de la misma orden).
            var mensaje = $"Esta remisión o factura ya fue recibida en la orden (recepción {previa.Folio}). Para registrarla otra vez captura el motivo.";
            var referencia = remision != null && previa.Remision == remision ? $"remisión {remision}" : $"factura {uuid ?? previa.CfdiUuidFiscal ?? "del CFDI"}";
            throw new IncidenciaRechazoException("RECEPCION_YA_RECIBIDA", mensaje,
                IncidenciasAlmacen.ReferenciaDuplicada(ordenCompraId, ocFolio, previa.Folio ?? "(sin folio)", referencia, sucursalId, mensaje));
        }
        return (remision, motivo);
    }
}
