using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// Motivos de cancelación de una recepción. PLATFORM-TODO(&lt;CatalogoMotivosCancelacionRecepcion&gt;): catálogo DEMO a la
/// espera de que Fernando Alejos fije el definitivo (86e3tabdp, pregunta 6); cuando exista, pasa a tabla administrable.
/// </summary>
public static class MotivosCancelacionRecepcion
{
    public static readonly IReadOnlyList<string> Codigos = ["ARTICULO", "CANTIDAD", "OC", "UBICACION", "OTRO"];

    /// <summary>Largo máximo del texto libre: el motivo guardado es <c>CODIGO: texto</c> y la columna admite 500.</summary>
    public const int TextoMaximo = 450;
}

/// <summary>
/// 86e3tabdp: cancela con motivo una recepción mal capturada. Revierte inventario y costo promedio con movimientos
/// <see cref="TipoMovimiento.CancelacionEntradaCompra"/> (uno por movimiento de la recepción: principal y MAT-REV),
/// marca las entradas Canceladas y publica <c>almacen.recepcion_oc.cancelada.v1</c> para que Compras devuelva lo
/// pendiente a la OC y CxP desligue la factura. Idempotente: repetir devuelve los mismos folios CEN.
/// </summary>
public sealed record CancelarRecepcionCommand(Guid RecepcionId, string MotivoCodigo, string MotivoTexto)
    : IRequest<CancelarRecepcionResponse>;

public sealed record CancelarRecepcionResponse(Guid RecepcionId, IReadOnlyList<string> FoliosCancelacion);

public sealed class CancelarRecepcionValidator : AbstractValidator<CancelarRecepcionCommand>
{
    public CancelarRecepcionValidator()
    {
        RuleFor(c => c.RecepcionId).NotEqual(Guid.Empty);
        RuleFor(c => c.MotivoCodigo).NotEmpty().WithMessage("Selecciona el motivo de la cancelación.")
            .Must(c => MotivosCancelacionRecepcion.Codigos.Contains((c ?? "").Trim().ToUpperInvariant()))
            .WithMessage($"El motivo debe ser uno de: {string.Join(", ", MotivosCancelacionRecepcion.Codigos)}.");
        RuleFor(c => c.MotivoTexto).NotEmpty().WithMessage("Explica qué se capturó mal.")
            .MaximumLength(MotivosCancelacionRecepcion.TextoMaximo)
            .WithMessage($"Explica el motivo (máximo {MotivosCancelacionRecepcion.TextoMaximo} caracteres).");
    }
}

public sealed class CancelarRecepcionHandler(
    AlmacenDbContext db,
    IComprasOcReadPort ocPort,
    IIntegrationEventPublisher events,
    ICurrentUserContext currentUser,
    ICurrentEmpresaContext currentEmpresa,
    IPeriodoContableReadPort periodoContable,
    IConversionUnidadPort conversion,
    ICxpDocumentosReadPort cxpDocumentos,
    IClock clock)
    : IRequestHandler<CancelarRecepcionCommand, CancelarRecepcionResponse>
{
    public async Task<CancelarRecepcionResponse> Handle(CancelarRecepcionCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var usuarioId = currentUser.UserId ?? throw new BusinessRuleException(
            "RECEPCION_SIN_USUARIO", "Se requiere usuario autenticado.");
        var empresaId = currentEmpresa.Current ?? throw new BusinessRuleException(
            "RECEPCION_SIN_EMPRESA", "El contexto de empresa es requerido.");
        var codigo = request.MotivoCodigo.Trim().ToUpperInvariant();
        var texto = request.MotivoTexto.Trim();

        // 1-2. Recepción (principal + hijo MAT-REV). Si ya está cancelada, responde igual que la primera vez.
        var previa = await CargarAsync(request.RecepcionId, rastrear: false, ct);
        if (previa.Principal.Estado == EstadoMovimiento.Cancelado)
            return await RespuestaYaCanceladaAsync(previa.Ids, request.RecepcionId, ct);
        // Sin OC no hay candado ni transacción (DevolucionOrigenLock) y el evento no tiene a quién avisar.
        if (previa.Principal.OcId is null)
            throw new BusinessRuleException("RECEPCION_SIN_OC", "Solo se puede cancelar una recepción contra orden de compra.");

        // 3. Serializa por OC (mismo lock que registrar y devolver) y relee ya con el candado.
        await using var transaccion = await DevolucionOrigenLock.AbrirAsync(db, previa.Principal.OcId, ct);
        var (principal, hijo, ids) = await CargarAsync(request.RecepcionId, rastrear: true, ct);
        if (principal.Estado == EstadoMovimiento.Cancelado)
            return await RespuestaYaCanceladaAsync(ids, request.RecepcionId, ct);
        if (principal.Estado != EstadoMovimiento.Registrado)
            throw new BusinessRuleException("RECEPCION_NO_CANCELABLE", "Solo se puede cancelar una recepción registrada.");

        // 4. Separación de funciones.
        if (principal.RegistradoPor == usuarioId)
            throw new BusinessRuleException("RECEPCION_CANCELA_MISMO_USUARIO",
                "Quien registró la recepción no puede cancelarla: pide a otra persona con permiso que la cancele.");

        // 5. Periodo: ni la fecha de la recepción ni la de hoy (fecha del contramovimiento) pueden estar cerradas.
        var hoy = FechaNegocio.Hoy(clock.UtcNow);
        await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, principal.FechaMovimiento, periodoContable, ct);
        if (hoy != principal.FechaMovimiento)
            await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, hoy, periodoContable, ct);

        // 6-9. Bloqueos de negocio.
        if (await db.RecepcionesActivos.AnyAsync(a => ids.Contains(a.RecepcionId) && a.AnuladaEn == null, ct))
            throw new BusinessRuleException("RECEPCION_CON_ACTIVOS",
                "La recepción incluye activos fijos dados de alta o por dar de alta: no se puede cancelar desde Almacén.");
        if (await db.DictamenesRevision.AnyAsync(d => ids.Contains(d.MovimientoRevisionId), ct))
            throw new BusinessRuleException("RECEPCION_CON_DICTAMEN",
                "El material en revisión de esta recepción ya tiene dictamen: no se puede cancelar.");
        if (await db.DevolucionesProveedor.AnyAsync(d => d.RecepcionOrigenId != null && ids.Contains(d.RecepcionOrigenId.Value)
                && d.Estado != EstadoDevolucionProveedor.Rechazada, ct))
            throw new BusinessRuleException("RECEPCION_CON_DEVOLUCION",
                "La recepción tiene una devolución a proveedor activa: ciérrala o recházala antes de cancelar.");

        var movimientos = hijo is null ? new[] { principal } : [principal, hijo];
        await ValidarSinSalidasAsync(movimientos, principal.RegistradoAt, ct);

        var facturas = await cxpDocumentos.ObtenerFacturasLigadasRecepcionAsync(
            ids, movimientos.Select(m => m.FacturaId).OfType<Guid>().ToList(), ct);
        if (facturas.FirstOrDefault(f => f.ImporteSaldado > 0m || f.Estado == "Pagada") is { } pagada)
            throw new BusinessRuleException("RECEPCION_FACTURA_PAGADA",
                $"La factura {pagada.Folio ?? pagada.FacturaId.ToString()} ligada a esta recepción ya tiene dinero aplicado (pago, anticipo, nota de crédito o cargo). " +
                "Usa devolución o nota de crédito del proveedor.");

        // 10. Contramovimientos CEN (uno por movimiento) y entradas a Cancelado.
        var motivo = $"{codigo}: {texto}";
        var cen = new List<MovimientoCreado>();
        foreach (var m in movimientos)
        {
            var creado = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.CancelacionEntradaCompra, hoy, motivo,
                m.Lineas.Select(l => new LineaRevisionMov(l.ArticuloId, l.UnidadMedida, l.Cantidad, l.CostoUnitarioMxn, l.UbicacionId!.Value,
                    l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm)).ToList(), ct);
            db.Movimientos.Local.Single(x => x.Id == creado.Id).VincularCancelacion(m.Id);
            m.CancelarRecepcion(motivo);
            cen.Add(creado);
        }

        // 11. El trigger descuenta el saldo al insertar las líneas CEN (SALDO_INSUFICIENTE es el respaldo de la validación de salidas).
        await db.SaveChangesAsync(ct);

        // 12. Costo promedio: la salida no lo recalcula; se quitan las unidades a su costo de entrada.
        await RestaurarCostoPromedioAsync(movimientos, ct);

        // 13. Evento y commit.
        var folios = cen.Select(c => c.Folio).ToList();
        await events.PublishAsync(await ArmarEventoAsync(empresaId, principal, movimientos, usuarioId, codigo, texto, folios, ct), ct);
        await db.SaveChangesAsync(ct);
        if (transaccion is not null) await transaccion.CommitAsync(ct);
        return new CancelarRecepcionResponse(principal.Id, folios);
    }

    private async Task<(MovimientoInventario Principal, MovimientoInventario? Hijo, List<Guid> Ids)> CargarAsync(
        Guid recepcionId, bool rastrear, CancellationToken ct)
    {
        var q = rastrear ? db.Movimientos : db.Movimientos.AsNoTracking();
        var movs = await q.Include(m => m.Lineas)
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra && (m.Id == recepcionId || m.RecepcionOrigenId == recepcionId))
            .ToListAsync(ct);
        var principal = movs.FirstOrDefault(m => m.Id == recepcionId && m.RecepcionOrigenId == null);
        if (principal is null)
        {
            // Recepción solo de activos fijos: no tiene movimiento de inventario, solo su registro por línea.
            // Las líneas de activos anuladas (#202) ya no cuentan; si todas lo están, no queda nada que cancelar.
            var activos = await db.RecepcionesActivos.AsNoTracking().Where(a => a.RecepcionId == recepcionId)
                .Select(a => a.AnuladaEn).ToListAsync(ct);
            if (activos.Any(anulada => anulada == null))
                throw new BusinessRuleException("RECEPCION_CON_ACTIVOS",
                    "La recepción incluye activos fijos dados de alta o por dar de alta: no se puede cancelar desde Almacén.");
            if (activos.Count > 0)
                throw new BusinessRuleException("RECEPCION_ACTIVOS_ANULADOS",
                    "Esta recepción solo tenía activos fijos y ya están anulados: no hay nada que cancelar.");
            throw new EntityNotFoundException("RECEPCION_NO_ENCONTRADA", "La recepción no existe.");
        }
        var hijo = movs.FirstOrDefault(m => m.RecepcionOrigenId == recepcionId);
        return (principal, hijo, movs.Select(m => m.Id).ToList());
    }

    private async Task<CancelarRecepcionResponse> RespuestaYaCanceladaAsync(List<Guid> ids, Guid recepcionId, CancellationToken ct)
    {
        var folios = await db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.CancelacionEntradaCompra && m.RecepcionOrigenId != null && ids.Contains(m.RecepcionOrigenId.Value))
            .OrderBy(m => m.Folio).Select(m => m.Folio!).ToListAsync(ct);
        return new CancelarRecepcionResponse(recepcionId, folios);
    }

    /// <summary>
    /// ponytail: revisa salidas posteriores por (ubicación, artículo, variante) sin rastrear lotes (Almacén no tiene lote). Si el saldo
    /// alcanza pero hubo una salida, igual se niega: el costo y la existencia ya no se pueden atribuir a esta recepción.
    /// Una carrera entre esta revisión y el commit la ataja el trigger con SALDO_INSUFICIENTE.
    /// </summary>
    private async Task ValidarSinSalidasAsync(IEnumerable<MovimientoInventario> movimientos, DateTimeOffset? registradoAt, CancellationToken ct)
    {
        var porBin = movimientos.SelectMany(m => m.Lineas)
            .GroupBy(l => (Ubicacion: l.UbicacionId!.Value, l.ArticuloId, Variante: l.ProductoAwVarianteId))
            .Select(g => (g.Key.Ubicacion, g.Key.ArticuloId, g.Key.Variante, Cantidad: g.Sum(l => l.Cantidad)));
        foreach (var (ubicacion, articulo, variante, cantidad) in porBin)
        {
            var saldo = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.UbicacionId == ubicacion && s.ArticuloId == articulo && s.ProductoAwVarianteId == variante)
                .Select(s => s.Cantidad).FirstOrDefaultAsync(ct);
            // Entradas y la propia cancelación de otras recepciones no son salidas de esta mercancía.
            // AjustePrecioFactura cuenta a propósito: el costo ya se ajustó contra la factura y no se revierte aquí.
            var salidaPosterior = await (from m in db.Movimientos.AsNoTracking()
                                         join l in db.LineasMovimiento.AsNoTracking() on m.Id equals l.MovimientoId
                                         where m.Estado == EstadoMovimiento.Registrado && m.RegistradoAt > registradoAt
                                            && l.UbicacionId == ubicacion && l.ArticuloId == articulo && l.ProductoAwVarianteId == variante
                                            && m.Tipo != TipoMovimiento.EntradaCompra && m.Tipo != TipoMovimiento.DevolucionSalida
                                            && m.Tipo != TipoMovimiento.AjustePositivo && m.Tipo != TipoMovimiento.ReincorporacionTrasRevision
                                            && m.Tipo != TipoMovimiento.CancelacionEntradaCompra
                                         select l.Id).AnyAsync(ct);
            if (saldo < cantidad || salidaPosterior)
                throw new BusinessRuleException("RECEPCION_CON_SALIDAS",
                    "Ya hubo salidas de este material: usa devolución a proveedor o ajuste.");
        }
    }

    private async Task RestaurarCostoPromedioAsync(IEnumerable<MovimientoInventario> movimientos, CancellationToken ct)
    {
        // Orden de bloqueo artículo, ubicación, variante (como el PR #203); #203 debe añadir el desempate por variante al rebasar.
        foreach (var g in movimientos.SelectMany(m => m.Lineas).GroupBy(l => (Ubicacion: l.UbicacionId!.Value, l.ArticuloId, Variante: l.ProductoAwVarianteId))
                     .OrderBy(g => g.Key.ArticuloId).ThenBy(g => g.Key.Ubicacion).ThenBy(g => g.Key.Variante))
        {
            // Mismo bloqueo de fila que el trigger y el ajuste de costo de expediente; el Reload evita leer un saldo rastreado viejo.
            var consulta = db.Database.IsNpgsql()
                ? db.SaldosInventario.FromSqlInterpolated($"SELECT * FROM almacen.saldos_inventario WHERE ubicacion_id = {g.Key.Ubicacion} AND articulo_id = {g.Key.ArticuloId} AND producto_aw_variante_id IS NOT DISTINCT FROM {g.Key.Variante} FOR UPDATE")
                : db.SaldosInventario.Where(s => s.UbicacionId == g.Key.Ubicacion && s.ArticuloId == g.Key.ArticuloId && s.ProductoAwVarianteId == g.Key.Variante);
            var saldo = await consulta.SingleAsync(ct);
            await db.Entry(saldo).ReloadAsync(ct);
            if (saldo.Cantidad <= 0m) continue; // sin existencia el siguiente ingreso fija su propio costo (trigger)
            // Valor tras quitar las unidades a su costo de entrada (no al promedio, que es lo que dejó el trigger). El valor guardado va
            // redondeado a centavos, así que el ajuste se mide contra él para que el promedio resultante sea exacto.
            var objetivo = saldo.Cantidad * saldo.CostoPromedioMxn + g.Sum(l => l.Cantidad * (saldo.CostoPromedioMxn - l.CostoUnitarioMxn));
            try
            {
                saldo.AjustarValor(objetivo - saldo.ValorInventarioMxn, clock.UtcNow);
            }
            catch (BusinessRuleException ex) when (ex.Code == "CE_AJUSTE_COSTO_INVALIDO")
            {
                throw new BusinessRuleException("RECEPCION_COSTO_NO_REVERTIBLE",
                    "No se puede revertir el costo promedio: un ajuste de costo posterior dejaría el valor del inventario negativo. Usa ajuste de inventario.");
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<OcRecepcionCanceladaIntegrationEvent> ArmarEventoAsync(
        Guid empresaId, MovimientoInventario principal, IReadOnlyList<MovimientoInventario> movimientos, Guid usuarioId,
        string codigo, string texto, IReadOnlyList<string> folios, CancellationToken ct)
    {
        var oc = await ocPort.ObtenerAsync(principal.OcId!.Value, ct);
        var lineas = new List<LineaRecepcionCanceladaPayload>();
        foreach (var l in movimientos.SelectMany(m => m.Lineas).OrderBy(l => l.Posicion))
        {
            var lineaOc = oc?.Lineas.FirstOrDefault(x => x.LineaId == l.LineaOcId);
            var cantidadDocumento = lineaOc is null ? l.Cantidad : Math.Round(
                l.Cantidad / await RecibidoPorLineaOc.FactorDocumentoABaseAsync(conversion, l.ArticuloId, lineaOc.UnidadMedida, ct), 4);
            lineas.Add(new LineaRecepcionCanceladaPayload(l.Id, l.LineaOcId, l.ArticuloId, lineaOc?.UnidadMedida ?? l.UnidadMedida,
                l.Cantidad, cantidadDocumento, l.CostoUnitarioMxn, l.MontoTotalMxn, l.UbicacionId!.Value));
        }
        var sub = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == lineas[0].UbicacionId).Select(u => u.SubAlmacenId).FirstAsync(ct);
        var (almacenId, sucursalId) = await Catalogo.AlmacenSucursalResolver.ResolverAsync(db, sub, ct);
        return new OcRecepcionCanceladaIntegrationEvent(empresaId, clock.UtcNow, principal.Id, principal.Folio!, principal.OcId!.Value,
            principal.FechaMovimiento, codigo, texto, usuarioId, folios, lineas, almacenId, sucursalId);
    }
}
