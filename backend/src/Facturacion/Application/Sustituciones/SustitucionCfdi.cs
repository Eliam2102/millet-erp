using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Sustituciones;

/// <summary>
/// Sustitución de una factura de venta con relación 04 (FAC-08b). La factura nueva (B)
/// relaciona el UUID de la original (A); al timbrar B el ERP arma la solicitud de
/// cancelación de A con motivo 01 y B como sustituto, que sigue los dos pasos de F6.5.
/// </summary>
public static class SustitucionCfdi
{
    /// <summary>Clave SAT <c>c_TipoRelacion</c> de la sustitución de CFDI previos.</summary>
    public const string TipoRelacion = "04";

    public const string MotivoSustitucion = "01";

    /// <summary>Motivo SAT con el que se cancela la sustituta cuando se rechaza la sustitución.</summary>
    public const string MotivoOperacionNoRealizada = "03";

    /// <summary>
    /// Revisa que la factura original se pueda sustituir: timbrada, sin cancelación en
    /// curso, sin otra sustituta vigente y sin documentos que la nueva no reemplaza.
    /// </summary>
    public static async Task<FacturaVenta> CargarSustituibleAsync(
        FacturacionDbContext db, Guid facturaId, CancellationToken cancellationToken)
    {
        var original = await db.FacturasVenta
            .Include(f => f.Relaciones)
            .FirstOrDefaultAsync(f => f.Id == facturaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {facturaId}.");

        if (original.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(original.Uuid))
            throw new BusinessRuleException(
                "SUSTITUCION_NO_PERMITIDA",
                $"Solo una factura timbrada y vigente se puede sustituir (estado actual: {original.Estado}).");

        if (await SustitutaVigenteDeAsync(db, original.Uuid, cancellationToken) is { } sustituta)
            throw new BusinessRuleException(
                "SUSTITUCION_YA_SUSTITUIDA", $"La factura ya tiene la sustituta {sustituta.Folio}.");

        if (await db.SolicitudesCancelacion.AnyAsync(s => s.ComprobanteId == original.Id
                && (s.Estado == EstadoSolicitudCancelacion.PendienteAutorizacion
                    || s.Estado == EstadoSolicitudCancelacion.Solicitada
                    || s.Estado == EstadoSolicitudCancelacion.EnProceso), cancellationToken))
            throw new BusinessRuleException(
                "SUSTITUCION_CANCELACION_EN_CURSO", "La factura tiene una cancelación en curso; resuélvela antes de sustituirla.");

        // Supuesto a validar: la sustituta no rehace anticipos, NC, complemento CCE ni la
        // autorización de activo fijo; esos casos se cancelan y reemiten por su flujo.
        if (original.Relaciones.Any(r => r.TipoRelacion == "07"))
            throw new BusinessRuleException(
                "SUSTITUCION_CON_ANTICIPOS", "La factura amortiza anticipos; cancela sus NC de amortización antes de sustituirla.");
        if (await db.NotasCredito.AnyAsync(n => n.FacturaRelacionadaId == original.Id
                && n.Estado != EstadoTimbrado.Cancelado && n.Estado != EstadoTimbrado.Descartada, cancellationToken))
            throw new BusinessRuleException(
                "SUSTITUCION_CON_NOTAS_CREDITO", "La factura tiene notas de crédito vigentes; cancélalas antes de sustituirla.");
        if (original.ComportamientoFiscal is ComportamientoFiscal.ExportacionConCce or ComportamientoFiscal.VentaActivoFijo)
            throw new BusinessRuleException(
                "SUSTITUCION_NO_SOPORTADA", "Las facturas de exportación con CCE o de venta de activo fijo no se sustituyen desde aquí.");

        return original;
    }

    /// <summary>
    /// Motivos 02 y 04: la nueva se emite DESPUÉS de cancelar la original (sin relación). La original debe estar
    /// cancelada por una solicitud aceptada (o vencida) con ese mismo motivo; el 04 solo aplica a público en general (global).
    /// </summary>
    public static async Task<FacturaVenta> CargarCanceladaParaMotivoAsync(
        FacturacionDbContext db, Guid facturaId, string motivo, CancellationToken cancellationToken)
    {
        var original = await db.FacturasVenta.Include(f => f.Relaciones)
            .FirstOrDefaultAsync(f => f.Id == facturaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {facturaId}.");
        if (motivo == "04" && original.ReceptorRfc != DatosFiscalesReceptor.RfcGenericoNacional)
            throw new BusinessRuleException(
                "SUSTITUCION_04_SOLO_GLOBAL", "El motivo 04 aplica solo a una factura global (público en general).");
        var cancelada = original.Estado == EstadoTimbrado.Cancelado && await db.SolicitudesCancelacion.AnyAsync(
            s => s.ComprobanteId == original.Id && s.MotivoSat == motivo
                && (s.Estado == EstadoSolicitudCancelacion.Aceptada || s.Estado == EstadoSolicitudCancelacion.Vencida),
            cancellationToken);
        if (!cancelada)
            throw new BusinessRuleException(
                "SUSTITUCION_CANCELAR_PRIMERO", $"Con el motivo {motivo} primero se cancela la factura con ese motivo; después se emite la nueva.");
        if (original.Relaciones.Any(r => r.TipoRelacion == "07"))
            throw new BusinessRuleException(
                "SUSTITUCION_CON_ANTICIPOS", "La factura amortiza anticipos; cancela sus NC de amortización antes de reemitirla.");
        if (original.ComportamientoFiscal is ComportamientoFiscal.ExportacionConCce or ComportamientoFiscal.VentaActivoFijo)
            throw new BusinessRuleException(
                "SUSTITUCION_NO_SOPORTADA", "Las facturas de exportación con CCE o de venta de activo fijo no se reemiten desde aquí.");
        return original;
    }

    /// <summary>La factura que sustituye a <paramref name="uuidOriginal"/> y no está cancelada ni descartada.</summary>
    public static Task<Comprobante?> SustitutaVigenteDeAsync(
        FacturacionDbContext db, string uuidOriginal, CancellationToken cancellationToken) =>
        db.Comprobantes
            .Where(c => c.Estado != EstadoTimbrado.Cancelado && c.Estado != EstadoTimbrado.Descartada
                && c.Relaciones.Any(r => r.TipoRelacion == TipoRelacion && r.UuidRelacionado == uuidOriginal))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Al quedar timbrada la sustituta, crea la solicitud de cancelación de la original con
    /// motivo 01 y el UUID de la sustituta. No hace nada si la factura no sustituye a otra.
    /// </summary>
    public static async Task<SolicitudCancelacion?> SolicitarCancelacionOriginalAsync(
        FacturacionDbContext db, Comprobante sustituta, Guid? usuarioId, string? usuarioNombre,
        DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        if (sustituta.Estado != EstadoTimbrado.Timbrado || sustituta.Uuid is null) return null;
        var uuidOriginal = sustituta.Relaciones.FirstOrDefault(r => r.TipoRelacion == TipoRelacion)?.UuidRelacionado;
        if (uuidOriginal is null) return null;

        var original = await db.Comprobantes.FirstOrDefaultAsync(c => c.Uuid == uuidOriginal, cancellationToken);
        if (original is not { Estado: EstadoTimbrado.Timbrado }) return null;

        var enCurso = db.SolicitudesCancelacion.Local.Any(s => s.ComprobanteId == original.Id && s.EstaViva)
            || await db.SolicitudesCancelacion.AnyAsync(s => s.ComprobanteId == original.Id
                && (s.Estado == EstadoSolicitudCancelacion.PendienteAutorizacion
                    || s.Estado == EstadoSolicitudCancelacion.Solicitada
                    || s.Estado == EstadoSolicitudCancelacion.EnProceso), cancellationToken);
        if (enCurso) return null;

        // PLATFORM-TODO(86e3txv0n): avisar al Contador General que hay una sustitución por autorizar cuando exista el aviso real.
        var solicitud = SolicitudCancelacion.Crear(
            original.EmpresaId, original.Id, MotivoSustitucion, sustituta.Uuid, ahora, usuarioId, usuarioNombre);
        db.SolicitudesCancelacion.Add(solicitud);
        return solicitud;
    }

    /// <summary>La factura timbrada que la solicitud (motivo 01) usa como sustituta de su comprobante, si la hay.</summary>
    public static async Task<FacturaVenta?> SustitutaDeSolicitudAsync(
        FacturacionDbContext db, SolicitudCancelacion solicitud, CancellationToken cancellationToken)
    {
        if (solicitud.MotivoSat != MotivoSustitucion || string.IsNullOrWhiteSpace(solicitud.UuidSustituto)) return null;
        var uuidOriginal = await db.Comprobantes.Where(c => c.Id == solicitud.ComprobanteId)
            .Select(c => c.Uuid).FirstOrDefaultAsync(cancellationToken);
        if (uuidOriginal is null) return null;
        return await db.FacturasVenta
            .Include(f => f.Relaciones)
            .FirstOrDefaultAsync(f => f.Uuid == solicitud.UuidSustituto
                && f.Estado == EstadoTimbrado.Timbrado
                && f.Relaciones.Any(r => r.TipoRelacion == TipoRelacion && r.UuidRelacionado == uuidOriginal), cancellationToken);
    }

    /// <summary>
    /// Cancelación de la original aceptada: la sustituta toma su pedido y salida. Devuelve
    /// true si hubo traspaso (el pedido no vuelve a facturable).
    /// </summary>
    public static async Task<bool> TraspasarOrigenAsync(
        FacturacionDbContext db, Comprobante original, SolicitudCancelacion solicitud, CancellationToken cancellationToken)
    {
        if (original is not FacturaVenta factura) return false;
        var sustituta = await SustitutaDeSolicitudAsync(db, solicitud, cancellationToken);
        if (sustituta is null) return false;

        if (factura.PedidoFacturableId is null && factura.SalidaFacturableId is null) return true;
        sustituta.HeredarOrigenDeSustituida(factura.PedidoFacturableId, factura.SalidaFacturableId);
        if (factura.PedidoFacturableId is Guid pedidoId)
        {
            var pedido = await db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);
            if (pedido is { Estado: EstadoPedidoFacturable.Facturado } && pedido.ComprobanteVigenteId == factura.Id)
                pedido.ReasignarComprobanteVigente(sustituta.Id);
        }
        return true;
    }

    /// <summary>
    /// Supuesto a validar (propuesta del 2-nov en la tarea): si el Contador General no autoriza
    /// la cancelación por sustitución, la sustituta se cancela con motivo 03 por el mismo flujo
    /// de dos pasos. La solicitud queda a nombre de quien pidió la sustitución.
    /// </summary>
    public static async Task<SolicitudCancelacion?> SolicitarCancelacionSustitutaAsync(
        FacturacionDbContext db, SolicitudCancelacion rechazada, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        var sustituta = await SustitutaDeSolicitudAsync(db, rechazada, cancellationToken);
        if (sustituta is null) return null;
        if (await db.SolicitudesCancelacion.AnyAsync(s => s.ComprobanteId == sustituta.Id
                && (s.Estado == EstadoSolicitudCancelacion.PendienteAutorizacion
                    || s.Estado == EstadoSolicitudCancelacion.Solicitada
                    || s.Estado == EstadoSolicitudCancelacion.EnProceso), cancellationToken))
            return null;

        var solicitud = SolicitudCancelacion.Crear(
            sustituta.EmpresaId, sustituta.Id, MotivoOperacionNoRealizada, null, ahora,
            rechazada.SolicitadaPorId, rechazada.SolicitadaPorNombre);
        db.SolicitudesCancelacion.Add(solicitud);
        return solicitud;
    }
}
