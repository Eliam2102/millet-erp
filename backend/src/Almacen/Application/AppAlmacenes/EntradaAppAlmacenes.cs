using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Partida de ENTRADA_ALMACEN ya resuelta a ids del ERP (cantidad en la unidad del saldo: m² en vidrio).</summary>
public sealed record EntradaApp(
    Guid ArticuloId, string UnidadMedida, Guid? ProductoAwVarianteId, decimal Cantidad, decimal? AnchoMm = null, decimal? AltoMm = null);

/// <summary>Puertos que usa la entrada de la app (lectura de la OC, conversión de unidades, periodo contable y outbox).</summary>
public sealed record EntradaAppPuertos(
    IComprasOcReadPort Ocs, IConversionUnidadPort Conversion, IPeriodoContableReadPort Periodo, IIntegrationEventPublisher Eventos);

/// <summary>
/// ENTRADA_ALMACEN de la app (M-159, contrato v8): la app ya acomodó las hojas en sus racks; el ERP sube existencia por
/// sucursal + artículo A+W + medida, sin pedir rack, en el almacén general (ubicación ÚNICA) de la sucursal. No guarda: el
/// llamador confirma junto con el <c>MensajeApp</c>.
/// <para>
/// Si <c>compra</c> es una OC del ERP, la entrada ES la recepción de esa OC: cada partida se liga a su línea (artículo +
/// medida), entra al precio de la línea en pesos (regla de oro: precio de inventario = OC; Fernando Alejos 9-nov), respeta
/// el pendiente exacto (<c>RECEPCION_EXCEDE_PENDIENTE</c>) y publica <c>almacen.oc_recepcion.registrada.v1</c> con factura
/// pendiente, igual que la recepción con lista de empaque, para el sub-estado Recepción de Compras y la conciliación de CxP.
/// Una entrada de la app = una recepción (se marca con <see cref="ReferenciaEntrada"/>). Si no es una OC del ERP, entra al
/// promedio vigente de la medida en el almacén general; sin promedio no entra (nunca a costo cero).
/// </para>
/// Periodo cerrado o no abierto en Contabilidad, OC no recibible o cualquier regla rota → ERROR con el motivo, sin mover saldo.
/// </summary>
public static class EntradaAppAlmacenes
{
    /// <summary>Marca de la recepción hecha desde la app; ocupa el lugar de la lista de empaque (factura pendiente).</summary>
    public static string ReferenciaEntrada(string entrada) => $"app-almacenes:{entrada}";

    public static async Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, EntradaAppPuertos puertos, Guid empresaId, Guid usuarioId, Guid sucursalId, string motivo,
        string? compra, string entrada, IReadOnlyList<EntradaApp> entradas, CancellationToken ct)
    {
        // ponytail: un solo almacén general por sucursal (primero de materiales directos). El mapeo por tipo + línea
        // (EXPORTACION/OBRAS/GENERAL/IMPORTACION TEMPORAL × HEGLA 1/HEGLA 2/BYSTRONIC) llega con el catálogo de 16 llaves.
        var general = await (from u in db.Ubicaciones
                             join s in db.SubAlmacenes on u.SubAlmacenId equals s.Id
                             join a in db.Almacenes on s.AlmacenId equals a.Id
                             where a.SucursalId == sucursalId && u.EsDefault && u.Estatus == EstatusCatalogo.Activo
                                   && s.Estatus == EstatusCatalogo.Activo && a.Estatus == EstatusCatalogo.Activo
                                   && s.Tipo != TipoSubAlmacen.MaterialEnRevision && s.Tipo != TipoSubAlmacen.Transitorio
                             orderby (s.Tipo == TipoSubAlmacen.MaterialesDirectos ? 0 : 1), a.Clave, s.Clave
                             select new { u.Id, u.SubAlmacenId, s.AlmacenId }).FirstOrDefaultAsync(ct);
        if (general is null)
            return ("ERROR", null, "La sucursal no tiene un almacén general activo donde registrar la entrada.");

        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            // Mismo candado que la recepción de la pantalla (PERIODO_CERRADO / PERIODO_CONTABLE_NO_ADMITE).
            await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, fecha, puertos.Periodo, ct);

            var ocId = string.IsNullOrWhiteSpace(compra) ? null : await puertos.Ocs.ObtenerIdPorFolioAsync(compra.Trim(), ct);
            if (ocId is Guid id)
                return await RecibirContraOcAsync(db, puertos, empresaId, usuarioId, sucursalId, general.Id, general.SubAlmacenId,
                    general.AlmacenId, id, fecha, motivo, entrada, entradas, ct);

            var lineas = new List<LineaRevisionMov>(entradas.Count);
            foreach (var e in entradas)
            {
                var costo = await db.SaldosInventario.AsNoTracking()
                    .Where(s => s.UbicacionId == general.Id && s.ArticuloId == e.ArticuloId && s.ProductoAwVarianteId == e.ProductoAwVarianteId)
                    .Select(s => (decimal?)s.CostoPromedioMxn).FirstOrDefaultAsync(ct);
                if (costo is not > 0m)
                    return ("ERROR", null,
                        $"La compra {compra} no es una orden de compra del ERP y {Medida(e)} no tiene costo promedio en el almacén general; "
                        + "no se registra a costo cero. Da la entrada contra la orden de compra del ERP.");
                lineas.Add(new LineaRevisionMov(e.ArticuloId, e.UnidadMedida, e.Cantidad, costo.Value, general.Id,
                    e.ProductoAwVarianteId, e.AnchoMm, e.AltoMm));
            }
            var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.EntradaCompra, fecha, motivo, lineas, ct);
            return (SalidaAppAlmacenes.Recibido, mov.Folio, null);
        }
        catch (BusinessRuleException ex)
        {
            return ("ERROR", null, ex.Message);
        }
    }

    private static async Task<(string Estado, string? Folio, string? Error)> RecibirContraOcAsync(
        AlmacenDbContext db, EntradaAppPuertos puertos, Guid empresaId, Guid usuarioId, Guid sucursalId,
        Guid ubicacionId, Guid subAlmacenId, Guid almacenId, Guid ocId, DateOnly fecha, string motivo, string entrada,
        IReadOnlyList<EntradaApp> entradas, CancellationToken ct)
    {
        // D5: serializa con las recepciones de la pantalla de la misma OC (la transacción ya la abrió el guardado).
        await using var propia = await DevolucionesProveedor.DevolucionOrigenLock.AbrirAsync(db, ocId, ct);
        var referencia = ReferenciaEntrada(entrada);
        var previa = await db.Movimientos.AsNoTracking()
            .Where(m => m.OcId == ocId && m.PackingListBlobRef == referencia && m.Estado == EstadoMovimiento.Registrado)
            .Select(m => m.Folio).FirstOrDefaultAsync(ct);
        if (previa is not null) return ("DUPLICADO", previa, null);

        var oc = await puertos.Ocs.ObtenerAsync(ocId, ct);
        SucursalDestinoGuard.Validar(oc?.SucursalDestinoId, sucursalId, "RECEPCION_SUCURSAL_DISTINTA",
            $"La orden de compra {oc?.Folio} está dirigida a otra sucursal: no entra en el almacén de esta.");
        var recibidoPropio = await RecibidoPorLineaOc.CalcularAsync(db, puertos.Conversion, oc, ct);

        // Partida → línea de la OC por artículo + medida (la app no manda lineaErp). Varias líneas iguales: la primera con pendiente.
        var porLinea = new List<(EntradaApp E, OcLineaLectura L, decimal Factor)>(entradas.Count);
        foreach (var e in entradas)
        {
            var candidatas = oc?.Lineas.Where(l => !l.EsActivoFijo && l.ArticuloId == e.ArticuloId
                && l.ProductoAwVarianteId == e.ProductoAwVarianteId).ToList() ?? [];
            if (candidatas.Count == 0)
                throw new BusinessRuleException("RECEPCION_ARTICULO_NO_EN_OC",
                    $"La orden de compra {oc?.Folio} no tiene una línea con {Medida(e)}.");
            var linea = candidatas.FirstOrDefault(l => l.CantidadSolicitada - Math.Max(l.CantidadRecibida, recibidoPropio.GetValueOrDefault(l.LineaId)) > 0)
                ?? candidatas[0];
            porLinea.Add((e, linea, await RecibidoPorLineaOc.FactorDocumentoABaseAsync(puertos.Conversion, e.ArticuloId, linea.UnidadMedida, ct)));
        }
        // Estado de la OC, costo válido y pendiente exacto: el mismo guard de la pantalla, en la unidad de la OC.
        var validada = await RecepcionOcGuard.ValidarAsync(oc,
            porLinea.Select(x => new RegistrarRecepcionLineaInput(x.E.ArticuloId, x.L.LineaId, x.E.Cantidad / x.Factor, null, null)).ToList(),
            recibidoPropio, ct);

        var acumulado = porLinea.GroupBy(x => x.L.LineaId)
            .ToDictionary(g => g.Key, g => validada.RecibidoPorLinea[g.Key] + g.Sum(x => x.E.Cantidad / x.Factor));
        var lineas = porLinea.Select(x => new LineaRevisionMov(x.E.ArticuloId, x.E.UnidadMedida, x.E.Cantidad,
            x.L.PrecioUnitarioMxn / x.Factor, ubicacionId, x.E.ProductoAwVarianteId, x.E.AnchoMm, x.E.AltoMm,
            CentroCostoId: x.L.CentroCostoId, LineaOcId: x.L.LineaId)).ToList();
        var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.EntradaCompra, fecha, motivo, lineas, ct);
        var movimiento = db.Movimientos.Local.Single(m => m.Id == mov.Id);
        movimiento.VincularRecepcionVarianteB(ocId, null, referencia);

        var payload = movimiento.Lineas.Zip(porLinea, (lm, x) => new LineaRecepcionPayload(
            LineaRecepcionId: lm.Id, LineaOcId: x.L.LineaId, ArticuloId: x.E.ArticuloId, UnidadMedida: x.L.UnidadMedida,
            Cantidad: x.E.Cantidad / x.Factor, CostoUnitarioMxn: x.L.PrecioUnitarioMxn,
            MontoTotalMxn: Math.Round(x.E.Cantidad / x.Factor * x.L.PrecioUnitarioMxn, 2),
            UbicacionId: ubicacionId, SubAlmacenId: subAlmacenId, CantidadRecibidaAcumulada: acumulado[x.L.LineaId],
            CentroCostoId: x.L.CentroCostoId)).ToList();
        await puertos.Eventos.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(
            EmpresaId: empresaId, OcurridoEn: DateTimeOffset.UtcNow, RecepcionId: mov.Id, FolioRecepcion: mov.Folio,
            OrdenCompraId: ocId, FechaMovimiento: fecha, FacturaPendiente: true, CfdiRecibidoId: null, CfdiUuidFiscal: null,
            Observaciones: motivo, Lineas: payload, AlmacenId: almacenId, SucursalId: sucursalId, RegistradoPorId: usuarioId), ct);
        return (SalidaAppAlmacenes.Recibido, mov.Folio, null);
    }

    private static string Medida(EntradaApp e) =>
        e.AnchoMm is { } a && e.AltoMm is { } h ? $"la medida {a:0.##} × {h:0.##} mm del artículo" : "el artículo";
}
