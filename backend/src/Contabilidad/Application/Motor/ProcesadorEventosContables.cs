using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// C1.5: completa el evento normalizado con datos que Contabilidad ya tiene (p. ej. de la póliza del documento origen). Se ejecuta
/// dentro del motor: si lanza <see cref="RechazoMotorException"/>, el evento va a la bandeja con esa causa.
/// </summary>
public interface IEnriquecedorEventoContable
{
    Task<EventoContable> EnriquecerAsync(EventoContable evento, CancellationToken ct);
}

/// <summary>
/// C1.5 · entrada única de los listeners y de «Reprocesar» de la bandeja. Enruta por tipo:
/// <list type="bullet">
///   <item>cancelaciones (#1b, #3, #6, #10) → anulación en negativo por documento (K10.3);</item>
///   <item>cobro de mostrador (#11a) → póliza de la parte no efectivo; el efectivo se guarda para la liquidación;</item>
///   <item>sesión de caja cerrada (#11b) → una póliza con los cobros en efectivo de la sesión;</item>
///   <item>cobro cancelado (#11c) → anula su póliza; el efectivo pendiente se excluye y el ya liquidado se reversa;</item>
///   <item>el resto → plantilla del motor (C1.4). La salida (#14) guarda además la cuenta de consumo de cada línea (V64).</item>
/// </list>
/// Una póliza que otro evento esperaba (cancelación o pago que llegó antes que su original) dispara el reproceso de ese rechazo.
/// </summary>
public sealed class ProcesadorEventosContables(ContabilidadDbContext db, IMediator mediator, IPolizaContablePort puerto, IClock clock)
{
    /// <summary>Detalle de un rechazo que espera la póliza de otro documento; se reprocesa solo cuando esa póliza nace.</summary>
    public static string Espera(Guid documentoId) => $"Espera:{documentoId}";

    public async Task<ResultadoMotor> ProcesarAsync(string tipo, Guid eventoId, string payload, string usuario, CancellationToken ct)
    {
        try
        {
            if (NormalizadorEventos.Anulacion(tipo, payload) is { } anulacion)
                return await AnularAsync(tipo, eventoId, payload, anulacion, usuario, ct);

            return tipo switch
            {
                NormalizadorEventos.CobroMostradorRegistrado => await CobroAsync(tipo, eventoId, payload, usuario, ct),
                NormalizadorEventos.CobroMostradorCancelado => await CobroCanceladoAsync(tipo, eventoId, payload, usuario, ct),
                NormalizadorEventos.CajaSesionCerrada => await LiquidacionAsync(eventoId, payload, usuario, ct),
                NormalizadorEventos.DevolucionInternaAplicada => await GenerarAsync(DevolucionInterna(eventoId, payload), usuario, ct),
                NormalizadorEventos.SalidaRequisicionRegistrada => await SalidaAsync(eventoId, payload, usuario, ct),
                _ => await GenerarAsync(NormalizadorEventos.Normalizar(tipo, eventoId, payload), usuario, ct),
            };
        }
        catch (EventoIlegibleException ex) { throw new BusinessRuleException("CONTAB_EVENTO_ILEGIBLE", ex.Message); }
        catch (System.Text.Json.JsonException ex) { throw new BusinessRuleException("CONTAB_EVENTO_ILEGIBLE", ex.Message); }
    }

    /// <summary>Motor de C1.4 y, si nace la póliza, reproceso de lo que la esperaba.</summary>
    private async Task<ResultadoMotor> GenerarAsync(EventoContable evento, string usuario, CancellationToken ct)
    {
        if (NormalizadorEventos.SinMovimiento(evento)) return Nada;
        var resultado = await mediator.Send(new GenerarPolizaDesdeEventoCommand(evento, usuario), ct);
        if (resultado.Generada) await ReprocesarEsperasAsync(evento.Documento.Id, ct);
        return resultado;
    }

    private static readonly ResultadoMotor Nada = new(null, null, null, null, null);

    // ---------------------------------------------------------------- Anulaciones (#1b, #3, #6, #10)

    private async Task<ResultadoMotor> AnularAsync(string tipo, Guid eventoId, string payload,
        (string Modulo, string Tipo, Guid DocumentoId, string Motivo) a, string usuario, CancellationToken ct)
    {
        var empresaId = NormalizadorEventos.LeerContrato<EmpresaContrato>(payload).EmpresaId;
        var existe = await db.Polizas.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(p => p.EmpresaId == empresaId && p.OrigenModulo == a.Modulo && p.OrigenTipo == a.Tipo && p.OrigenDocumentoId == a.DocumentoId, ct);
        if (!existe)
            return await RechazarAsync(empresaId, tipo, eventoId, payload, CausaRechazoContable.DatoFaltante, Espera(a.DocumentoId),
                "La póliza del documento cancelado aún no existe; la anulación se aplica en cuanto se registre.", ct);
        try
        {
            var anuladas = await puerto.AnularPorDocumentoOrigenAsync(a.Modulo, a.Tipo, a.DocumentoId, a.Motivo, ct);
            var poliza = anuladas.Count > 0 ? anuladas[0] : null;
            if (poliza is not null) await ResolverRechazoAsync(empresaId, tipo, eventoId, poliza.Id, usuario, ct);
            return new(poliza, null, null, null, null);
        }
        catch (BusinessRuleException ex) when (GenerarPolizaDesdeEventoHandler.Clasificar(ex) is { } r)
        {
            db.ChangeTracker.Clear();
            return await RechazarAsync(empresaId, tipo, eventoId, payload, r.Causa, r.Detalle, ex.Message, ct);
        }
    }

    // ---------------------------------------------------------------- Caja (#11a, #11b, #11c)

    private async Task<ResultadoMotor> CobroAsync(string tipo, Guid eventoId, string payload, string usuario, CancellationToken ct)
    {
        var c = NormalizadorEventos.LeerContrato<NormalizadorEventos.CobroRegistradoContrato>(payload);
        // Venta directa (sin pedido): Facturación publica el cobro sin cliente; se toma de la RF o AN del comprobante cobrado, antes de
        // guardar el efectivo para que la liquidación también lo lleve. Si esa póliza aún no existe, el cobro espera a que nazca.
        var cliente = c.ClienteId;
        if (cliente is null && c.ComprobanteId is { } comprobante)
        {
            cliente = await EnriquecedorPolizasOrigen.ClienteDelComprobanteAsync(db, c.EmpresaId, comprobante, ct);
            if (cliente is null)
                return await RechazarAsync(c.EmpresaId, tipo, eventoId, payload, CausaRechazoContable.DatoFaltante, Espera(comprobante),
                    "Falta la póliza del comprobante cobrado (de ahí sale el cliente); el cobro se contabiliza en cuanto se registre.", ct);
        }

        var efectivo = (c.FormasPago ?? []).Where(f => f.FormaPago == NormalizadorEventos.FormaEfectivo).Sum(f => f.Importe);
        if (efectivo != 0m && !await db.CobrosEfectivoCaja.IgnoreQueryFilters().AnyAsync(x => x.CobroId == c.CobroMostradorId, ct))
        {
            var cobro = new CobroEfectivoCaja(c.CobroMostradorId, c.EmpresaId, c.CajaSesionId, c.SucursalId, cliente, efectivo, c.OcurridoEn);
            db.CobrosEfectivoCaja.Add(cobro);
            await db.SaveChangesAsync(ct);
            // Cobro que llega después de liquidada su sesión: su propia póliza de efectivo (no se reabre la liquidación).
            if (await SesionLiquidadaAsync(c.EmpresaId, c.CajaSesionId, ct))
            {
                var tardio = await GenerarAsync(NormalizadorEventos.Liquidacion(eventoId, c.EmpresaId, FechaContable.HoyLocal(c.OcurridoEn),
                    NormalizadorEventos.DocCobro, c.CobroMostradorId, null, c.SucursalId, [(cliente, efectivo)], payload,
                    $"{NormalizadorEventos.CajaSesionCerrada}:cobro:{c.CobroMostradorId}"), usuario, ct);
                if (tardio.Poliza is { } p) await MarcarLiquidadosAsync([c.CobroMostradorId], p.Id, ct);
            }
        }
        return await GenerarAsync(NormalizadorEventos.Normalizar(tipo, eventoId, payload) with { ClienteId = cliente }, usuario, ct);
    }

    private async Task<ResultadoMotor> LiquidacionAsync(Guid eventoId, string payload, string usuario, CancellationToken ct)
    {
        var s = NormalizadorEventos.LeerContrato<NormalizadorEventos.CajaSesionCerradaContrato>(payload);
        var cobros = await db.CobrosEfectivoCaja.IgnoreQueryFilters()
            .Where(x => x.EmpresaId == s.EmpresaId && x.CajaSesionId == s.CajaSesionId && x.Estado == EstadoCobroEfectivo.Pendiente)
            .OrderBy(x => x.CobradoEn).ToListAsync(ct);
        var evento = NormalizadorEventos.Liquidacion(eventoId, s.EmpresaId, s.DiaOperacion, NormalizadorEventos.DocCajaSesion, s.CajaSesionId,
            null, s.SucursalId, [.. cobros.Select(x => (x.ClienteId, x.Importe))], payload);
        var resultado = await GenerarAsync(evento, usuario, ct);
        if (resultado.Poliza is { } poliza) await MarcarLiquidadosAsync([.. cobros.Select(x => x.CobroId)], poliza.Id, ct);
        return resultado;
    }

    private async Task<ResultadoMotor> CobroCanceladoAsync(string tipo, Guid eventoId, string payload, string usuario, CancellationToken ct)
    {
        var c = NormalizadorEventos.LeerContrato<NormalizadorEventos.CobroCanceladoContrato>(payload);
        var efectivo = await db.CobrosEfectivoCaja.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CobroId == c.CobroMostradorId, ct);
        var conPoliza = await db.Polizas.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.EmpresaId == c.EmpresaId
            && p.OrigenModulo == NormalizadorEventos.DocCobro.Modulo && p.OrigenTipo == NormalizadorEventos.DocCobro.Tipo
            && p.OrigenDocumentoId == c.CobroMostradorId, ct);
        if (efectivo is null && !conPoliza)
            return await RechazarAsync(c.EmpresaId, tipo, eventoId, payload, CausaRechazoContable.DatoFaltante, Espera(c.CobroMostradorId),
                "El cobro cancelado aún no llega a Contabilidad; la cancelación se aplica en cuanto llegue.", ct);

        ResultadoMotor resultado = Nada;
        // Parte no efectivo (y cobro tardío con póliza propia): anulación en negativo de las pólizas del cobro.
        if (conPoliza)
        {
            var anuladas = await puerto.AnularPorDocumentoOrigenAsync(NormalizadorEventos.DocCobro.Modulo, NormalizadorEventos.DocCobro.Tipo,
                c.CobroMostradorId, "Cobro de mostrador cancelado", ct);
            if (anuladas.Count > 0) resultado = new(anuladas[0], null, null, null, null);
        }
        if (efectivo is null) return resultado;

        if (efectivo.Estado == EstadoCobroEfectivo.Pendiente)
        {
            // Sesión abierta: el cobro sale de la liquidación (C1.5 §3 #11c).
            efectivo.Cancelar();
            await db.SaveChangesAsync(ct);
            return resultado;
        }
        if (efectivo.Estado == EstadoCobroEfectivo.Liquidado && !await EsPolizaDelCobroAsync(efectivo, ct))
        {
            // Ya liquidado en la póliza de la sesión: reversa solo de este cobro (cargo CLIENTES, abono EFECTIVO_CAJA_COBRANZA).
            var reversa = await GenerarAsync(NormalizadorEventos.Liquidacion(eventoId, c.EmpresaId, FechaContable.HoyLocal(c.OcurridoEn),
                NormalizadorEventos.DocCobro, c.CobroMostradorId, null, efectivo.SucursalId, [(efectivo.ClienteId, -efectivo.Importe)],
                payload, $"{tipo}:{eventoId}"), usuario, ct);
            efectivo.Cancelar();
            await db.SaveChangesAsync(ct);
            return reversa;
        }
        efectivo.Cancelar();
        await db.SaveChangesAsync(ct);
        return resultado;
    }

    private Task<bool> SesionLiquidadaAsync(Guid empresaId, Guid sesionId, CancellationToken ct) =>
        db.Polizas.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.EmpresaId == empresaId
            && p.OrigenModulo == NormalizadorEventos.DocCajaSesion.Modulo && p.OrigenTipo == NormalizadorEventos.DocCajaSesion.Tipo
            && p.OrigenDocumentoId == sesionId, ct);

    private Task<bool> EsPolizaDelCobroAsync(CobroEfectivoCaja cobro, CancellationToken ct) =>
        db.Polizas.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.Id == cobro.PolizaLiquidacionId
            && p.OrigenTipo == NormalizadorEventos.DocCobro.Tipo && p.OrigenDocumentoId == cobro.CobroId, ct);

    private async Task MarcarLiquidadosAsync(IReadOnlyList<Guid> cobros, Guid polizaId, CancellationToken ct)
    {
        var filas = await db.CobrosEfectivoCaja.IgnoreQueryFilters().Where(x => cobros.Contains(x.CobroId)).ToListAsync(ct);
        foreach (var x in filas) x.Liquidar(polizaId);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Consumo (#14, #15a)

    private async Task<ResultadoMotor> SalidaAsync(Guid eventoId, string payload, string usuario, CancellationToken ct)
    {
        var evento = NormalizadorEventos.Normalizar(NormalizadorEventos.SalidaRequisicionRegistrada, eventoId, payload);
        var resultado = await GenerarAsync(evento, usuario, ct);
        if (resultado.Poliza is not { } poliza) return resultado;

        // V64: guarda la cuenta y el centro de costo con que se cargó cada línea (partidas de cargo en el orden de las líneas).
        var salida = NormalizadorEventos.LeerContrato<NormalizadorEventos.SalidaLineasContrato>(payload);
        if (await db.ConsumosContabilizados.IgnoreQueryFilters().AnyAsync(x => x.EmpresaId == evento.EmpresaId && x.SalidaId == salida.SalidaId, ct))
            return resultado;
        var cargos = await db.PolizasPartidas.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.PolizaId == poliza.Id && x.Cargo > 0m).OrderBy(x => x.Linea).Select(x => x.CuentaId).ToListAsync(ct);
        var lineas = (salida.Lineas ?? []).Where(l => l.MontoTotalMxn != 0m).ToList();
        for (var i = 0; i < lineas.Count && i < cargos.Count; i++)
        {
            var l = lineas[i];
            db.ConsumosContabilizados.Add(new ConsumoContabilizado(l.LineaSalidaId, evento.EmpresaId, salida.SalidaId, l.ArticuloId,
                l.CentroCostoId ?? evento.CentroCostoId, cargos[i], l.MontoTotalMxn, poliza.Id));
        }
        await db.SaveChangesAsync(ct);
        await ReprocesarEsperasAsync(salida.SalidaId, ct);
        return resultado;
    }

    /// <summary>
    /// #15a: el evento solo trae artículo y monto por línea; las líneas (con la cuenta y el centro de costo de la salida) las completa
    /// <see cref="EnriquecedorPolizasOrigen"/> desde <see cref="ConsumoContabilizado"/>.
    /// </summary>
    private static EventoContable DevolucionInterna(Guid eventoId, string payload)
    {
        var d = NormalizadorEventos.LeerContrato<NormalizadorEventos.DevolucionInternaContrato>(payload);
        var total = (d.Lineas ?? []).Sum(l => l.MontoTotalMxn);
        return new(NormalizadorEventos.DevolucionInternaAplicada, eventoId, d.EmpresaId, FechaContable.HoyLocal(d.OcurridoEn),
            new DocumentoOrigenPoliza(NormalizadorEventos.DocDevolucionInterna.Modulo, NormalizadorEventos.DocDevolucionInterna.Tipo,
                d.DevolucionId, d.FolioDevolucion, null),
            d.SucursalId, null, null, null, null, "MXN", null, new Dictionary<string, decimal?> { ["Subtotal"] = total }, [], payload);
    }

    // ---------------------------------------------------------------- Bandeja

    /// <summary>Reprocesa los rechazos pendientes que esperaban la póliza de este documento (cancelación o pago llegados antes).</summary>
    private async Task ReprocesarEsperasAsync(Guid documentoId, CancellationToken ct)
    {
        var detalle = Espera(documentoId);
        var pendientes = await db.RechazosContables.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Estado == EstadoRechazoContable.Pendiente && r.Detalle == detalle)
            .Select(r => new { r.TipoEvento, r.EventoId, r.Payload }).ToListAsync(ct);
        foreach (var r in pendientes)
            await mediator.Send(new ProcesarEventoContableCommand(r.TipoEvento, r.EventoId, r.Payload), ct);
    }

    private async Task<ResultadoMotor> RechazarAsync(Guid empresaId, string tipo, Guid eventoId, string payload, CausaRechazoContable causa,
        string? detalle, string mensaje, CancellationToken ct)
    {
        var rechazo = await db.RechazosContables.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EmpresaId == empresaId && r.TipoEvento == tipo && r.EventoId == eventoId, ct);
        if (rechazo is null)
        {
            rechazo = RechazoContable.Crear(empresaId, tipo, eventoId, payload, causa, detalle, mensaje, clock.UtcNow);
            db.RechazosContables.Add(rechazo);
        }
        else if (rechazo.Estado == EstadoRechazoContable.Pendiente)
            rechazo.FallaDeNuevo(causa, detalle, mensaje, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return new(null, rechazo.Id, causa, detalle, rechazo.Mensaje);
    }

    private async Task ResolverRechazoAsync(Guid empresaId, string tipo, Guid eventoId, Guid polizaId, string usuario, CancellationToken ct)
    {
        var rechazo = await db.RechazosContables.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.EmpresaId == empresaId && r.TipoEvento == tipo
            && r.EventoId == eventoId && r.Estado == EstadoRechazoContable.Pendiente, ct);
        if (rechazo is null) return;
        rechazo.MarcarReprocesado(polizaId, usuario, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private sealed record EmpresaContrato(Guid EmpresaId);
}

/// <summary>
/// C1.5 · datos que el evento no trae y que Contabilidad ya tiene en sus pólizas:
/// <list type="bullet">
///   <item>#5 pago a proveedor: proveedor y sucursal de la póliza TT de la factura pagada;</item>
///   <item>#12 pago de cliente: sucursal de la póliza RF de la primera factura pagada;</item>
///   <item>#15a devolución interna: cuenta y centro de costo de cada línea de la salida original (V64), repartido por artículo.</item>
/// </list>
/// Si la póliza origen todavía no existe, rechazo <see cref="CausaRechazoContable.DatoFaltante"/> que se reprocesa solo al nacer.
/// </summary>
public sealed class EnriquecedorPolizasOrigen(ContabilidadDbContext db) : IEnriquecedorEventoContable
{
    public async Task<EventoContable> EnriquecerAsync(EventoContable evento, CancellationToken ct)
    {
        var e = evento;
        switch (e.Tipo)
        {
            case NormalizadorEventos.PagoProveedorAplicado when e.FacturaOrigenId is { } factura:
            {
                var tt = await PartidaOrigenAsync(e.EmpresaId, NormalizadorEventos.DocFacturaProveedor, factura, conProveedor: true, ct)
                    ?? throw Espera(factura, "la póliza de la factura de proveedor pagada");
                return e with { ProveedorId = e.ProveedorId ?? tt.ProveedorId, SucursalId = e.SucursalId ?? tt.SucursalId, FacturaOrigenId = null };
            }
            case NormalizadorEventos.PagoClienteConfirmado when e.SucursalId is null && e.FacturaOrigenId is { } factura:
            {
                var rf = await PartidaOrigenAsync(e.EmpresaId, NormalizadorEventos.DocComprobante, factura, conProveedor: false, ct)
                    ?? throw Espera(factura, "la póliza de la factura de venta pagada");
                return e with { SucursalId = rf.SucursalId, FacturaOrigenId = null };
            }
            case NormalizadorEventos.NotaCreditoTimbrada when e.ClienteId is null:
            {
                // Venta directa: la NC llega sin cliente; sale de la RF relacionada o, si aplica un anticipo, de su AN.
                var n = NormalizadorEventos.LeerContrato<NormalizadorEventos.NotaCreditoVentaContrato>(e.Payload);
                if ((n.FacturaRelacionadaId ?? n.AnticipoOrigenId) is not { } origen) return e;
                var cliente = await ClienteDelComprobanteAsync(db, e.EmpresaId, origen, ct)
                    ?? throw Espera(origen, "la póliza del comprobante relacionado con la nota de crédito");
                return e with { ClienteId = cliente };
            }
            case NormalizadorEventos.SalidaRequisicionRegistrada
                when e.CentroCostoId is null && e.Lineas.Any(l => l.Importe != 0m && l.CentroCostoId is null):
                // La plantilla #14 carga el consumo por centro de costo: sin él, la salida no se contabiliza a ciegas.
                throw new RechazoMotorException(CausaRechazoContable.DatoFaltante, "CentroCostoId",
                    "La salida trae líneas sin centro de costo y la plantilla de consumo lo pide.");
            case NormalizadorEventos.DevolucionInternaAplicada:
                return await DevolucionInternaAsync(e, ct);
            default:
                return e;
        }
    }

    /// <summary>Cliente del auxiliar de la póliza vigente (RF o AN) de un comprobante de Facturación; null si aún no tiene póliza.</summary>
    internal static Task<Guid?> ClienteDelComprobanteAsync(ContabilidadDbContext db, Guid empresaId, Guid comprobanteId, CancellationToken ct) =>
        db.Polizas.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.OrigenModulo == NormalizadorEventos.DocComprobante.Modulo
                && p.OrigenTipo == NormalizadorEventos.DocComprobante.Tipo && p.OrigenDocumentoId == comprobanteId && p.PolizaAnuladaId == null)
            .SelectMany(p => p.Partidas)
            .Where(x => x.ClienteId != null)
            .OrderBy(x => x.Linea)
            .Select(x => x.ClienteId)
            .FirstOrDefaultAsync(ct);

    private async Task<(Guid SucursalId, Guid? ProveedorId)?> PartidaOrigenAsync(Guid empresaId, (string Modulo, string Tipo) doc, Guid documentoId,
        bool conProveedor, CancellationToken ct)
    {
        var partida = await db.Polizas.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.OrigenModulo == doc.Modulo && p.OrigenTipo == doc.Tipo && p.OrigenDocumentoId == documentoId
                && p.PolizaAnuladaId == null)
            .SelectMany(p => p.Partidas)
            .Where(x => !conProveedor || x.ProveedorId != null)
            .OrderBy(x => x.Linea)
            .Select(x => new { x.SucursalId, x.ProveedorId })
            .FirstOrDefaultAsync(ct);
        return partida is null ? null : (partida.SucursalId, partida.ProveedorId);
    }

    /// <summary>Reparte el monto devuelto de cada artículo entre las líneas de la salida con ese artículo, en proporción a su monto.</summary>
    private async Task<EventoContable> DevolucionInternaAsync(EventoContable e, CancellationToken ct)
    {
        var d = NormalizadorEventos.LeerContrato<NormalizadorEventos.DevolucionInternaContrato>(e.Payload);
        var consumos = await db.ConsumosContabilizados.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == e.EmpresaId && x.SalidaId == d.SalidaOrigenId).ToListAsync(ct);
        if (consumos.Count == 0) throw Espera(d.SalidaOrigenId, "la póliza de consumo de la salida original");

        var lineas = new List<LineaEventoContable>();
        foreach (var devuelta in (d.Lineas ?? []).Where(l => l.MontoTotalMxn != 0m))
        {
            var origen = consumos.Where(x => x.ArticuloId == devuelta.ArticuloId).ToList();
            if (origen.Count == 0)
                throw new RechazoMotorException(CausaRechazoContable.DatoFaltante, $"ArticuloId:{devuelta.ArticuloId}",
                    $"La salida original no tiene el artículo {devuelta.ArticuloId} devuelto.");
            var base_ = origen.Sum(x => x.Monto);
            var asignado = 0m;
            for (var i = 0; i < origen.Count; i++)
            {
                var importe = i == origen.Count - 1
                    ? devuelta.MontoTotalMxn - asignado
                    : Math.Round(devuelta.MontoTotalMxn * origen[i].Monto / base_, 2, MidpointRounding.AwayFromZero);
                asignado += importe;
                lineas.Add(new LineaEventoContable(null, importe, origen[i].CentroCostoId, false, CuentaId: origen[i].CuentaId));
            }
        }
        return e with { Lineas = lineas, SucursalId = e.SucursalId ?? await SucursalDeAsync(consumos[0].PolizaId, ct) };
    }

    private Task<Guid> SucursalDeAsync(Guid polizaId, CancellationToken ct) =>
        db.PolizasPartidas.IgnoreQueryFilters().AsNoTracking().Where(x => x.PolizaId == polizaId).OrderBy(x => x.Linea)
            .Select(x => x.SucursalId).FirstAsync(ct);

    private static RechazoMotorException Espera(Guid documentoId, string que) =>
        new(CausaRechazoContable.DatoFaltante, ProcesadorEventosContables.Espera(documentoId),
            $"Falta {que}; el evento se contabiliza en cuanto se registre.");
}
