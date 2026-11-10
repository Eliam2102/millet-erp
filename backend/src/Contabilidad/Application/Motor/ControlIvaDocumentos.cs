using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// C1.6 · IVA pendiente → pagado (proveedores) y no cobrado → cobrado (clientes), por flujo de efectivo:
/// <list type="bullet">
///   <item>la póliza de cada documento con IVA (#2, #7, #8) deja su <see cref="SaldoIvaDocumento"/>;</item>
///   <item>antes de la póliza de un pago o cobro (#5, #12, #11a), <see cref="PrepararAsync"/> calcula el IVA proporcional de lo aplicado y lo
///   pasa a la plantilla como <see cref="CampoIva"/>; con la póliza, <see cref="ConfirmarAsync"/> guarda la <see cref="ReclasificacionIva"/>;</item>
///   <item>un pago revertido o un cobro cancelado (C1.6-b) devuelve lo aplicado; la anulación de su póliza ya revierte las partidas;</item>
///   <item>las notas de crédito o de cargo reducen el documento (su póliza ya revierte su IVA pendiente).</item>
/// </list>
/// Un documento sin saldo (contabilizado antes de C1.6) no reclasifica. Si el documento aún no tiene póliza, el pago espera a que nazca.
/// </summary>
public sealed class ControlIvaDocumentos(ContabilidadDbContext db, IClock clock)
{
    /// <summary>Importe con nombre que leen las plantillas v2 de #5, #11a y #12.</summary>
    public const string CampoIva = "IvaReclasificado";

    private const string ImpuestoIva = "002";

    /// <summary>
    /// Pago que registra la parte en efectivo de un cobro de mostrador (la reclasifica la liquidación #11b): distinto de
    /// <see cref="NormalizadorEventos.DocCobro"/> (parte no efectivo, #11a) para que un cobro mixto deje dos movimientos.
    /// </summary>
    public static readonly (string Modulo, string Tipo) DocCobroEfectivo = ("facturacion", "cobro_mostrador_efectivo");

    /// <summary>
    /// Documento al que se aplica un pago o cobro, con el importe aplicado en la moneda del documento. <see cref="PagoId"/> distinto del
    /// documento del evento: la liquidación de efectivo (#11b) aplica cada cobro con su propio id.
    /// </summary>
    public sealed record Aplicacion((string Modulo, string Tipo) Documento, Guid DocumentoId, decimal Importe,
        (string Modulo, string Tipo)? Pago = null, Guid? PagoId = null);

    /// <summary>Resultado de preparar un pago: el evento con su IVA o el documento cuya póliza todavía no existe.</summary>
    public sealed record Preparado(EventoContable Evento, Guid? EsperaDocumentoId);

    // ---------------------------------------------------------------- Documentos con IVA (#2, #7, #8)

    public static bool EsDocumento(string tipo) => tipo is NormalizadorEventos.FacturaProveedorRegistrada
        or NormalizadorEventos.FacturaVentaTimbrada or NormalizadorEventos.FacturaAnticipoTimbrada;

    public static bool EsPago(string tipo) => tipo is NormalizadorEventos.PagoProveedorAplicado or NormalizadorEventos.PagoClienteConfirmado
        or NormalizadorEventos.CobroMostradorRegistrado;

    public static bool EsNota(string tipo) => tipo is NormalizadorEventos.NotaCreditoProveedorRegistrada or NormalizadorEventos.NotaCargoAutorizada
        or NormalizadorEventos.NotaCreditoTimbrada;

    /// <summary>
    /// Las plantillas v2 de #5, #11a, #11b y #12 leen <see cref="CampoIva"/>: sin aplicaciones (documento sin IVA o anterior a C1.6) va en
    /// cero para que el motor no rechace por dato faltante.
    /// </summary>
    public static EventoContable ConCampoIva(EventoContable e) =>
        ConRetencionAplicada((EsPago(e.Tipo) || e.Tipo == NormalizadorEventos.CajaSesionCerrada) && !e.Importes.ContainsKey(CampoIva)
            ? e with { Importes = new Dictionary<string, decimal?>(e.Importes) { [CampoIva] = 0m } }
            : e, []);

    /// <summary>
    /// 86e3zcbjw · pago a proveedor (#5 v3): <see cref="RetencionesContables.CampoAplicada"/> = retención que el pago descuenta de las
    /// facturas registradas por el total, <see cref="RetencionesContables.CampoTotalMas"/> = pago + esa retención (cargo al proveedor) y
    /// una línea de grupo <see cref="LineaEventoContable.GrupoRetencion"/> por concepto (abono a su rol de retenciones por pagar).
    /// </summary>
    private static EventoContable ConRetencionAplicada(EventoContable e, IReadOnlyList<RetencionAplicadaIva> retenciones)
    {
        if (e.Tipo != NormalizadorEventos.PagoProveedorAplicado) return e;
        var lineas = RetencionesContables.Lineas(retenciones.Select(r => ((string?)r.Clasificador, r.Importe)));
        var aplicada = lineas.Sum(l => l.Importe);
        var total = e.Importes.GetValueOrDefault("Total") ?? 0m;
        return e with
        {
            Importes = new Dictionary<string, decimal?>(e.Importes)
            {
                [RetencionesContables.CampoAplicada] = aplicada, [RetencionesContables.CampoTotalMas] = total + aplicada,
            },
            Lineas = [.. e.Lineas.Where(l => l.Grupo != LineaEventoContable.GrupoRetencion), .. lineas],
        };
    }

    /// <summary>Con la póliza del documento, guarda su saldo de IVA (una vez por documento).</summary>
    public async Task RegistrarDocumentoAsync(EventoContable e, PolizaRegistrada poliza, CancellationToken ct)
    {
        var d = NormalizadorEventos.LeerContrato<DocumentoIvaContrato>(e.Payload);
        var iva = e.Importes.GetValueOrDefault("Iva") ?? 0m;
        var retenido = (d.Retenciones ?? []).Where(EsIva).Sum(r => r.Importe);
        var retenciones = e.Lineas.Where(l => l.Grupo == LineaEventoContable.GrupoRetencion && l.Importe > 0m)
            .Select(l => (l.Clasificador ?? string.Empty, l.Importe)).ToList();
        // M-209: base e IVA por tratamiento. Con detalle, también un documento solo exento, al 0 % o no objeto crea su saldo (la DIOT
        // necesita esas bases); sin detalle, como hoy (todo al 16 %, y sin IVA no hay saldo).
        var detalle = ImpuestosTrasladados.Agrupar(d.Impuestos);
        var tratamientos = ImpuestosTrasladados.ParaSaldo(detalle, e.Importes.GetValueOrDefault("Subtotal"), iva);
        var soloBases = detalle.Count > 0 && detalle.Any(t => t.Base > 0m);
        if (iva <= 0m && retenido <= 0m && retenciones.Count == 0 && !soloBases) return;
        var origen = (e.Documento.Modulo, e.Documento.Tipo);
        if (await db.SaldosIvaDocumento.IgnoreQueryFilters().AnyAsync(s => s.EmpresaId == e.EmpresaId && s.OrigenModulo == origen.Modulo
                && s.OrigenTipo == origen.Tipo && s.DocumentoId == e.Documento.Id, ct))
            return;
        var lado = e.Tipo == NormalizadorEventos.FacturaProveedorRegistrada ? LadoIva.Acreditable : LadoIva.Trasladado;
        var @base = e.Importes.GetValueOrDefault("TotalMenosRetenciones") ?? e.Importes.GetValueOrDefault("Total") ?? 0m;
        // 86e3zcbjw: la retención va al pago solo si la póliza de la factura no la registró (plantilla #2 sin retenciones por pagar).
        var alPago = lado == LadoIva.Acreditable && retenciones.Count > 0 && !await PlantillaRegistraRetencionAsync(e, ct);
        db.SaldosIvaDocumento.Add(SaldoIvaDocumento.Crear(e.EmpresaId, lado, origen, e.Documento.Id, e.Documento.Folio, e.Documento.Uuid,
            d.MetodoPago, e.SucursalId, lado == LadoIva.Acreditable ? e.ProveedorId : e.ClienteId, e.FechaContable, e.Moneda, e.TipoCambio,
            @base, iva, retenido, poliza.Id, retenciones, alPago, tratamientos));
        // El mismo evento en paralelo: si la otra petición ya guardó el saldo, no hay nada más que hacer.
        await GuardarUnaVezAsync(ct);
    }

    /// <summary>Factura de proveedor o comprobante de venta cancelado: su saldo deja de contar como pendiente.</summary>
    public async Task CancelarDocumentoAsync(Guid empresaId, string modulo, string tipo, Guid documentoId, CancellationToken ct)
    {
        var saldo = await db.SaldosIvaDocumento.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.EmpresaId == empresaId
            && s.OrigenModulo == modulo && s.OrigenTipo == tipo && s.DocumentoId == documentoId, ct);
        if (saldo is null || saldo.Estado == EstadoSaldoIva.Cancelado) return;
        saldo.Cancelar();
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Pagos y cobros (#5, #12, #11a)

    /// <summary>
    /// Calcula el IVA a reclasificar de las aplicaciones y lo agrega al evento como <see cref="CampoIva"/> (sin guardar nada). Las
    /// aplicaciones se simulan en orden sobre una copia del saldo, igual que después en <see cref="ConfirmarAsync"/>. Idempotente: un pago
    /// ya aplicado (reproceso) usa lo guardado. Con <paramref name="esperar"/>, un documento sin saldo ni póliza detiene el pago.
    /// </summary>
    public async Task<Preparado> PrepararAsync(EventoContable e, IReadOnlyList<Aplicacion> aplicaciones, bool esperar, CancellationToken ct)
    {
        var total = 0m;
        var alPago = new List<RetencionAplicadaIva>();
        var porTratamiento = new List<(string Tratamiento, decimal Iva)>();
        var saldos = new Dictionary<Guid, SaldoIvaDocumento>();
        foreach (var a in aplicaciones)
        {
            var saldo = await SaldoAsync(e.EmpresaId, a, rastreado: false, ct);
            if (saldo is null)
            {
                if (esperar && !await TienePolizaAsync(e.EmpresaId, a, ct)) return new(e, a.DocumentoId);
                continue;
            }
            saldo = saldos.TryGetValue(saldo.Id, out var simulado) ? simulado : saldos[saldo.Id] = saldo;
            if (await PreviaAsync(e, a, saldo.Id, ct) is { } previa)
            {
                total += previa.AnuladaEn is null ? previa.Iva : 0m;
                if (previa.AnuladaEn is null && saldo.RetencionAlPago) alPago.AddRange(previa.Retenciones);
                if (previa.AnuladaEn is null) porTratamiento.AddRange(PorTratamiento(previa.Tratamientos, previa.Iva));
                continue;
            }
            var (iva, retenido) = saldo.Calcular(a.Importe);
            var retenciones = saldo.CalcularRetenciones(a.Importe);
            var tratamientos = saldo.CalcularTratamientos(a.Importe, iva);
            saldo.Aplicar(PagoDe(e, a), PagoIdDe(e, a), e.FechaContable, a.Importe, iva, retenido, Guid.Empty, retenciones: retenciones,
                tratamientos: tratamientos);
            porTratamiento.AddRange(PorTratamiento(tratamientos, iva));
            total += iva;
            if (saldo.RetencionAlPago) alPago.AddRange(retenciones);
        }
        var importes = new Dictionary<string, decimal?>(e.Importes) { [CampoIva] = (e.Importes.GetValueOrDefault(CampoIva) ?? 0m) + total };
        // M-209: el IVA que pasa a pagado o cobrado sale del pendiente de cada tratamiento (líneas del grupo IVA, rol por clasificador);
        // suman exacto lo de CampoIva. Sin IVA que reclasificar (o un pago sin documentos), sin líneas: la plantilla usa CampoIva.
        var lineasIva = e.Importes.ContainsKey(CampoIva) && (e.Importes[CampoIva] ?? 0m) != 0m
            ? new List<LineaEventoContable>() : ImpuestosTrasladados.Lineas(porTratamiento);
        var conIva = e with { Importes = importes, Lineas = [.. e.Lineas.Where(l => l.Grupo != LineaEventoContable.GrupoIva), .. lineasIva] };
        return new(ConRetencionAplicada(conIva, alPago), null);
    }

    /// <summary>
    /// M-209: IVA de una aplicación por tratamiento. Si el detalle no suma el IVA reclasificado (saldo anterior sin tratamientos), la
    /// diferencia va como TASA16, para que las líneas sumen exacto lo de la póliza.
    /// </summary>
    internal static IEnumerable<(string Tratamiento, decimal Iva)> PorTratamiento(IEnumerable<TratamientoAplicadoIva> tratamientos, decimal iva)
    {
        var lista = tratamientos.Where(t => t.Iva != 0m).Select(t => (t.Tratamiento, t.Iva)).ToList();
        var diferencia = iva - lista.Sum(t => t.Iva);
        if (diferencia != 0m) lista.Add((ImpuestosTrasladados.Tasa16, diferencia));
        return lista;
    }

    /// <summary>Con la póliza del pago o cobro, aplica el importe y el IVA a cada documento (una vez por pago y documento).</summary>
    public async Task ConfirmarAsync(EventoContable e, IReadOnlyList<Aplicacion> aplicaciones, PolizaRegistrada poliza, CancellationToken ct)
    {
        foreach (var a in aplicaciones)
            for (var intento = 0; intento < Intentos; intento++)
            {
                var saldo = await SaldoAsync(e.EmpresaId, a, rastreado: true, ct);
                if (saldo is null || await PreviaAsync(e, a, saldo.Id, ct) is not null) break;
                var (iva, retenido) = saldo.Calcular(a.Importe);
                // Mismo tipo de cambio que aplicó el motor a la póliza (el del pago).
                var tc = e.Moneda == "MXN" ? 1m : e.TipoCambio ?? 1m;
                db.ReclasificacionesIva.Add(saldo.Aplicar(PagoDe(e, a), PagoIdDe(e, a), e.FechaContable, a.Importe, iva, retenido, poliza.Id, tc,
                    saldo.CalcularRetenciones(a.Importe)));
                if (await GuardarUnaVezAsync(ct)) break;
            }
    }

    /// <summary>IVA ya reclasificado (vigente) de un pago o cobro: la reversa de un cobro en efectivo liquidado lo devuelve en negativo.</summary>
    public Task<decimal> IvaAplicadoAsync(Guid empresaId, (string Modulo, string Tipo) pago, Guid pagoId, CancellationToken ct) =>
        db.ReclasificacionesIva.IgnoreQueryFilters().Where(r => r.EmpresaId == empresaId && r.PagoModulo == pago.Modulo && r.PagoTipo == pago.Tipo
            && r.PagoId == pagoId && r.Tipo == TipoMovimientoIva.Pago && r.AnuladaEn == null).SumAsync(r => r.Iva, ct);

    /// <summary>C1.6-b: pago revertido o cobro cancelado; el saldo vuelve a pendiente.</summary>
    public async Task RevertirPagoAsync(Guid empresaId, string modulo, string tipo, Guid pagoId, CancellationToken ct)
    {
        var aplicadas = await db.ReclasificacionesIva.IgnoreQueryFilters().Where(r => r.EmpresaId == empresaId && r.PagoModulo == modulo
            && r.PagoTipo == tipo && r.PagoId == pagoId && r.Tipo == TipoMovimientoIva.Pago && r.AnuladaEn == null).ToListAsync(ct);
        if (aplicadas.Count == 0) return;
        var ids = aplicadas.Select(r => r.SaldoIvaDocumentoId).ToList();
        var saldos = await db.SaldosIvaDocumento.IgnoreQueryFilters().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var r in aplicadas) saldos[r.SaldoIvaDocumentoId].Revertir(r, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Notas de crédito y de cargo (#3b, #3c, #9)

    /// <summary>Reduce el documento relacionado por la nota (base, IVA e IVA retenido); una vez por nota.</summary>
    public async Task RegistrarNotaAsync(EventoContable e, PolizaRegistrada poliza, CancellationToken ct)
    {
        var n = NormalizadorEventos.LeerContrato<NotaIvaContrato>(e.Payload);
        (string Modulo, string Tipo) documento;
        Guid? relacionado;
        decimal @base, iva;
        if (e.Tipo == NormalizadorEventos.NotaCreditoTimbrada)
        {
            // Una NC que aplica un anticipo no reduce el IVA de la factura (no lo mueve su póliza #9): queda fuera (a validar).
            if (n.AnticipoOrigenId is not null) return;
            documento = NormalizadorEventos.DocComprobante;
            relacionado = n.FacturaRelacionadaId;
            @base = e.Importes.GetValueOrDefault("TotalDescuento") ?? 0m;
            iva = e.Importes.GetValueOrDefault("IvaDescuento") ?? 0m;
        }
        else
        {
            documento = NormalizadorEventos.DocFacturaProveedor;
            relacionado = n.FacturaOrigenId;
            @base = e.Importes.GetValueOrDefault("TotalMenosRetenciones") ?? 0m;
            iva = e.Importes.GetValueOrDefault("Iva") ?? 0m;
        }
        if (relacionado is not { } facturaId) return;
        var aplicacion = new Aplicacion(documento, facturaId, 0m);
        var retenido = (n.Retenciones ?? []).Where(EsIva).Sum(r => r.Importe);
        var retenciones = (n.Retenciones ?? []).Where(r => r.Importe > 0m && RetencionesContables.Impuesto(r.Impuesto) is not null)
            .Select(r => (RetencionesContables.Impuesto(r.Impuesto)!, r.Importe)).ToList();
        for (var intento = 0; intento < Intentos; intento++)
        {
            var saldo = await SaldoAsync(e.EmpresaId, aplicacion, rastreado: true, ct);
            if (saldo is null || await PreviaAsync(e, aplicacion, saldo.Id, ct) is not null) return;
            db.ReclasificacionesIva.Add(saldo.Reducir((e.Documento.Modulo, e.Documento.Tipo), e.Documento.Id, e.FechaContable, @base, iva,
                retenido, poliza.Id, retenciones));
            if (await GuardarUnaVezAsync(ct)) return;
        }
    }

    // ---------------------------------------------------------------- Apoyo

    private const int Intentos = 3;

    /// <summary>
    /// Guarda el saldo o la reclasificación. Si el mismo evento se procesa en paralelo (o dos pagos tocan el mismo saldo a la vez), el
    /// índice único o la versión del saldo lo detectan: se descarta lo propio y devuelve false para que el llamador relea y decida (la
    /// previa ya existe → nada que hacer; si no, reintenta sobre el saldo actualizado).
    /// </summary>
    private async Task<bool> GuardarUnaVezAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is Npgsql.PostgresException
            {
                SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_saldos_iva_documento_documento" or "ux_reclasificaciones_iva_pago_documento",
            })
        {
            foreach (var entrada in db.ChangeTracker.Entries().Where(x => x.Entity is SaldoIvaDocumento or ReclasificacionIva).ToList())
                entrada.State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>A qué documentos aplica el pago o cobro, según su contrato.</summary>
    public static IReadOnlyList<Aplicacion> Aplicaciones(EventoContable e)
    {
        switch (e.Tipo)
        {
            case NormalizadorEventos.PagoProveedorAplicado:
            {
                var p = NormalizadorEventos.LeerContrato<PagoProveedorIvaContrato>(e.Payload);
                return [new(NormalizadorEventos.DocFacturaProveedor, p.FacturaProveedorId, p.Monto)];
            }
            case NormalizadorEventos.PagoClienteConfirmado:
            {
                var p = NormalizadorEventos.LeerContrato<PagoClienteIvaContrato>(e.Payload);
                return [.. (p.Facturas ?? []).Where(f => f.ImporteAplicado > 0m).GroupBy(f => f.FacturaVentaId)
                    .Select(g => new Aplicacion(NormalizadorEventos.DocComprobante, g.Key, g.Sum(f => f.ImporteAplicado)))];
            }
            case NormalizadorEventos.CobroMostradorRegistrado:
            {
                // C1.6: la parte no efectivo reclasifica con su póliza (#11a); el efectivo, en la liquidación de la sesión (#11b).
                var c = NormalizadorEventos.LeerContrato<NormalizadorEventos.CobroRegistradoContrato>(e.Payload);
                var importe = (c.FormasPago ?? []).Where(f => f.FormaPago != NormalizadorEventos.FormaEfectivo).Sum(f => f.Importe);
                return c.ComprobanteId is { } comprobante && importe > 0m
                    ? [new(NormalizadorEventos.DocComprobante, comprobante, importe)]
                    : [];
            }
            default:
                return [];
        }
    }

    /// <summary>¿La plantilla con la que se contabilizó el documento abona retenciones por pagar? (versiones anteriores a 86e3zcbjw).</summary>
    private async Task<bool> PlantillaRegistraRetencionAsync(EventoContable e, CancellationToken ct)
    {
        var plantilla = await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == e.EmpresaId && p.TipoEvento == e.Tipo && p.VigenteDesde <= e.FechaContable)
            .OrderByDescending(p => p.NumeroVersion).FirstOrDefaultAsync(ct);
        return plantilla?.Lineas.Any(l => l.Rol.StartsWith(RetencionesContables.RolPorPagar, StringComparison.Ordinal)) ?? true;
    }

    private Task<SaldoIvaDocumento?> SaldoAsync(Guid empresaId, Aplicacion a, bool rastreado, CancellationToken ct)
    {
        var q = db.SaldosIvaDocumento.IgnoreQueryFilters();
        return (rastreado ? q : q.AsNoTracking()).FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.OrigenModulo == a.Documento.Modulo
            && s.OrigenTipo == a.Documento.Tipo && s.DocumentoId == a.DocumentoId, ct);
    }

    private static (string Modulo, string Tipo) PagoDe(EventoContable e, Aplicacion a) => a.Pago ?? (e.Documento.Modulo, e.Documento.Tipo);

    private static Guid PagoIdDe(EventoContable e, Aplicacion a) => a.PagoId ?? e.Documento.Id;

    private Task<bool> TienePolizaAsync(Guid empresaId, Aplicacion a, CancellationToken ct) =>
        db.Polizas.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.EmpresaId == empresaId && p.OrigenModulo == a.Documento.Modulo
            && p.OrigenTipo == a.Documento.Tipo && p.OrigenDocumentoId == a.DocumentoId, ct);

    private Task<ReclasificacionIva?> PreviaAsync(EventoContable e, Aplicacion a, Guid saldoId, CancellationToken ct)
    {
        var (modulo, tipo) = PagoDe(e, a);
        var pagoId = PagoIdDe(e, a);
        return db.ReclasificacionesIva.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.EmpresaId == e.EmpresaId
            && r.PagoModulo == modulo && r.PagoTipo == tipo && r.PagoId == pagoId && r.SaldoIvaDocumentoId == saldoId, ct);
    }

    private static bool EsIva(RetencionIvaContrato r) => r.Impuesto?.Trim().ToUpperInvariant() is ImpuestoIva or "IVA";

    // ---- Espejos de contrato (solo lo que usa el control de IVA) ----

    private sealed record DocumentoIvaContrato(string? MetodoPago, IReadOnlyList<RetencionIvaContrato>? Retenciones,
        IReadOnlyList<ImpuestosTrasladados.ImpuestoTrasladoContrato>? Impuestos = null);

    private sealed record RetencionIvaContrato(string? Impuesto, decimal Importe);

    private sealed record NotaIvaContrato(Guid? FacturaOrigenId, Guid? FacturaRelacionadaId, Guid? AnticipoOrigenId,
        IReadOnlyList<RetencionIvaContrato>? Retenciones);

    private sealed record PagoProveedorIvaContrato(Guid FacturaProveedorId, decimal Monto);

    private sealed record PagoClienteIvaContrato(IReadOnlyList<PagoClienteFacturaIvaContrato>? Facturas);

    private sealed record PagoClienteFacturaIvaContrato(Guid FacturaVentaId, decimal ImporteAplicado);
}
