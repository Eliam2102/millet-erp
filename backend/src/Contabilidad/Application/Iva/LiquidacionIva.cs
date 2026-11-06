using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Iva;

// ─── K10.7 · liquidación mensual de IVA por flujo de efectivo ─────────────────

/// <summary>Totales del mes en MXN, sumados de las reclasificaciones de C1.6 (pagos y cobros del mes).</summary>
/// <param name="IvaRetenidoAProveedores">IVA que Millet retuvo a proveedores (fletes): se entera aparte; no resta del IVA a cargo.</param>
/// <param name="IvaACargo">Trasladado cobrado − acreditable pagado − retenido por clientes, si es positivo.</param>
/// <param name="IvaAFavor">El mismo cálculo cuando es negativo, en positivo.</param>
public sealed record ResumenLiquidacionIva(decimal IvaTrasladadoCobrado, decimal IvaAcreditablePagado, decimal IvaRetenidoPorClientes,
    decimal IvaRetenidoAProveedores, decimal IvaACargo, decimal IvaAFavor);

/// <summary>K10.7-a: el IVA por documentos contra el movimiento del mes en las cuentas del rol.</summary>
public sealed record CuadreRolIva(string Rol, string Descripcion, IReadOnlyList<string> Cuentas, decimal PorDocumentos, decimal EnCuentas,
    decimal Diferencia, bool Cuadra);

/// <summary>Documento con IVA pagado o cobrado en el mes y su marca de cumplimiento (K10.7-b).</summary>
public sealed record DocumentoLiquidacionIva(Guid SaldoId, LadoIva Lado, string OrigenTipo, Guid DocumentoId, string? Folio, string? Uuid,
    string? MetodoPago, DateOnly Fecha, string Moneda, decimal Base, decimal Iva, decimal AplicadoMes, decimal IvaMes, decimal IvaRetenidoMes,
    decimal IvaPendiente, decimal Saldo, bool Completo, IReadOnlyList<string> Motivos);

public sealed record LiquidacionIvaResponse(int Anio, int Mes, ResumenLiquidacionIva Resumen, IReadOnlyList<CuadreRolIva> Cuadre, bool Cuadra,
    IReadOnlyList<DocumentoLiquidacionIva> Documentos, int Pendientes);

public sealed record LiquidacionIvaQuery(int Anio, int Mes) : IRequest<LiquidacionIvaResponse>;

/// <summary>K10.7: borrador de la póliza de liquidación del mes con las cuentas de los roles; el contador la revisa y autoriza.</summary>
public sealed record GenerarPolizaLiquidacionIvaCommand(int Anio, int Mes, Guid SucursalId) : IRequest<PolizaResponse>;

public sealed class LiquidacionIvaQueryValidator : AbstractValidator<LiquidacionIvaQuery>
{
    public LiquidacionIvaQueryValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2000, 2100);
        RuleFor(q => q.Mes).InclusiveBetween(1, 12);
    }
}

public sealed class GenerarPolizaLiquidacionIvaValidator : AbstractValidator<GenerarPolizaLiquidacionIvaCommand>
{
    public GenerarPolizaLiquidacionIvaValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2000, 2100);
        RuleFor(q => q.Mes).InclusiveBetween(1, 12);
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).WithMessage("Elija la sucursal de la póliza.");
    }
}

public sealed class LiquidacionIvaHandlers(ContabilidadDbContext db, ICumplimientoIvaReadPort cumplimiento, IMediator mediator)
    : IRequestHandler<LiquidacionIvaQuery, LiquidacionIvaResponse>,
      IRequestHandler<GenerarPolizaLiquidacionIvaCommand, PolizaResponse>
{
    public const string RolTrasladadoCobrado = "IVA_TRASLADADO_COBRADO";
    public const string RolAcreditablePagado = "IVA_ACREDITABLE_PAGADO";
    public const string RolRetencionesClientes = "RETENCIONES_CLIENTES";
    public const string RolPorPagar = "IVA_POR_PAGAR";
    public const string RolAFavor = "IVA_A_FAVOR";
    public static readonly (string Modulo, string Tipo) DocLiquidacion = ("contabilidad", "liquidacion_iva");
    private const string ClaveTipoPoliza = "AS";

    public async Task<LiquidacionIvaResponse> Handle(LiquidacionIvaQuery q, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (desde, hasta) = Mes(q.Anio, q.Mes);
        var movimientos = await db.ReclasificacionesIva.AsNoTracking()
            .Where(r => r.Tipo == TipoMovimientoIva.Pago && r.AnuladaEn == null && r.Fecha >= desde && r.Fecha <= hasta)
            .ToListAsync(ct);
        var ids = movimientos.Select(r => r.SaldoIvaDocumentoId).Distinct().ToList();
        var saldos = await db.SaldosIvaDocumento.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var resumen = Resumen(movimientos, saldos);
        var (trasladado, acreditable) = (resumen.IvaTrasladadoCobrado, resumen.IvaAcreditablePagado);

        var cuadre = new List<CuadreRolIva>
        {
            await CuadreAsync(RolTrasladadoCobrado, "IVA trasladado cobrado", trasladado, acreedora: true, desde, hasta, ct),
            await CuadreAsync(RolAcreditablePagado, "IVA acreditable pagado", acreditable, acreedora: false, desde, hasta, ct),
        };

        var documentos = await DocumentosAsync(movimientos, saldos, ct);
        return new(q.Anio, q.Mes, resumen, cuadre, cuadre.All(c => c.Cuadra), documentos, documentos.Count(d => !d.Completo));
    }

    public async Task<PolizaResponse> Handle(GenerarPolizaLiquidacionIvaCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var folio = $"IVA-{c.Anio:D4}-{c.Mes:D2}";
        if (await db.Polizas.AsNoTracking().AnyAsync(p => p.OrigenModulo == DocLiquidacion.Modulo && p.OrigenTipo == DocLiquidacion.Tipo
                && p.OrigenFolio == folio && p.Estado != EstadoPoliza.Anulada, ct))
            throw new ConflictException("CONTAB_IVA_LIQUIDACION_EXISTE",
                $"Ya existe la póliza de liquidación de IVA de {folio[4..]}; anúlela para generar otra.");

        var r = (await Handle(new LiquidacionIvaQuery(c.Anio, c.Mes), ct)).Resumen;
        var fecha = Mes(c.Anio, c.Mes).Hasta;
        var roles = await db.RolesCuenta.AsNoTracking().Where(x => x.DeletedAt == null).ToListAsync(ct);
        var partidas = new List<PartidaRequest>();
        Guid? CuentaDe(string rol) => roles.Where(x => x.Codigo == rol && (x.SucursalId is null || x.SucursalId == c.SucursalId)
                && x.Moneda is null && x.VigenteEn(fecha) && x.CuentaId is not null)
            .OrderByDescending(x => x.SucursalId is null ? 0 : 1).ThenByDescending(x => x.VigenteDesde).Select(x => x.CuentaId).FirstOrDefault();
        void Agregar(string rol, string concepto, decimal cargo, decimal abono, string? clasificador = null)
        {
            if (cargo == 0m && abono == 0m) return;
            // 86e3zcbjw: la retención de cada concepto sale de la cuenta a la que la cargó la factura ({rol}_{concepto}); si no, del rol base.
            var cuenta = (clasificador is null ? null : CuentaDe($"{rol}_{clasificador}")) ?? CuentaDe(rol)
                ?? throw new BusinessRuleException("CONTAB_IVA_ROL_SIN_CUENTA",
                    $"El rol {rol} no tiene cuenta asignada: Contabilidad debe mapearlo en Roles de cuenta.");
            partidas.Add(new PartidaRequest(cuenta, concepto, cargo, abono, c.SucursalId));
        }
        Agregar(RolTrasladadoCobrado, "IVA trasladado cobrado del mes", r.IvaTrasladadoCobrado, 0m);
        Agregar(RolAcreditablePagado, "IVA acreditable pagado del mes", 0m, r.IvaAcreditablePagado);
        var porConcepto = await RetencionesClientesPorConceptoAsync(c.Anio, c.Mes, ct);
        // Centavos de redondeo entre el total y la suma por concepto: al concepto mayor, para no exigir cuenta al rol base por ellos.
        var resto = r.IvaRetenidoPorClientes - porConcepto.Sum(x => x.Importe);
        if (porConcepto.Count > 0 && resto != 0m && Math.Abs(resto) <= 0.05m)
        {
            var mayor = porConcepto.IndexOf(porConcepto.MaxBy(x => x.Importe));
            porConcepto[mayor] = (porConcepto[mayor].Clasificador, porConcepto[mayor].Importe + resto);
        }
        foreach (var (clasificador, importe) in porConcepto)
            Agregar(RolRetencionesClientes, $"IVA retenido por clientes del mes ({clasificador})", 0m, importe, clasificador);
        Agregar(RolRetencionesClientes, "IVA retenido por clientes del mes", 0m, r.IvaRetenidoPorClientes - porConcepto.Sum(x => x.Importe));
        Agregar(RolPorPagar, "IVA a cargo del mes", 0m, r.IvaACargo);
        Agregar(RolAFavor, "IVA a favor del mes", r.IvaAFavor, 0m);
        if (partidas.Count < 2)
            throw new BusinessRuleException("CONTAB_IVA_SIN_MOVIMIENTOS", "El mes no tiene IVA pagado ni cobrado que liquidar.");

        var tipo = await db.TiposDocumento.AsNoTracking().Where(t => t.Clave == ClaveTipoPoliza).Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (tipo == Guid.Empty)
            throw new BusinessRuleException("CONTAB_IVA_TIPO_POLIZA", $"No existe el tipo de póliza {ClaveTipoPoliza} (asiento contable).");
        return await mediator.Send(new CrearPolizaCommand(new PolizaManualRequest(tipo, fecha, $"Liquidación de IVA {c.Mes:D2}/{c.Anio}",
            partidas, DocumentoOrigen: new DocumentoOrigenRequest(DocLiquidacion.Modulo, DocLiquidacion.Tipo, Guid.CreateVersion7(), folio, null))), ct);
    }

    /// <summary>86e3zcbjw: IVA retenido por clientes en los cobros del mes, por concepto (en MXN, como quedó en cada cobro).</summary>
    private async Task<List<(string Clasificador, decimal Importe)>> RetencionesClientesPorConceptoAsync(int anio, int mes, CancellationToken ct)
    {
        var (desde, hasta) = Mes(anio, mes);
        var movimientos = await db.ReclasificacionesIva.AsNoTracking()
            .Where(r => r.Tipo == TipoMovimientoIva.Pago && r.AnuladaEn == null && r.Fecha >= desde && r.Fecha <= hasta)
            .Join(db.SaldosIvaDocumento.AsNoTracking().Where(s => s.Lado == LadoIva.Trasladado), r => r.SaldoIvaDocumentoId, s => s.Id, (r, s) => r)
            .ToListAsync(ct);
        return [.. movimientos.SelectMany(r => r.Retenciones)
            .Where(x => RetencionSaldoIva.ImpuestoDe(x.Clasificador) == "IVA" && x.Clasificador != "IVA")
            .GroupBy(x => x.Clasificador).Select(g => (g.Key, g.Sum(x => x.ImporteMxn))).Where(x => x.Item2 != 0m).OrderBy(x => x.Key)];
    }

    /// <summary>
    /// K10.7-a/c: IVA a cargo = trasladado cobrado − acreditable pagado − IVA retenido por clientes. El IVA retenido a proveedores
    /// (fletes, honorarios) no resta: se entera aparte y se muestra como «por enterar» (supuesto a validar, doc 15 §4).
    /// </summary>
    public static ResumenLiquidacionIva Resumen(IReadOnlyCollection<ReclasificacionIva> movimientos,
        IReadOnlyDictionary<Guid, SaldoIvaDocumento> saldos)
    {
        decimal Suma(LadoIva lado, Func<ReclasificacionIva, decimal> campo) =>
            movimientos.Where(r => saldos[r.SaldoIvaDocumentoId].Lado == lado).Sum(campo);
        var trasladado = Suma(LadoIva.Trasladado, r => r.IvaMxn);
        var acreditable = Suma(LadoIva.Acreditable, r => r.IvaMxn);
        var retenidoClientes = Suma(LadoIva.Trasladado, r => r.IvaRetenidoMxn);
        var retenidoProveedores = Suma(LadoIva.Acreditable, r => r.IvaRetenidoMxn);
        var neto = trasladado - acreditable - retenidoClientes;
        return new ResumenLiquidacionIva(trasladado, acreditable, retenidoClientes, retenidoProveedores, Math.Max(neto, 0m),
            Math.Max(-neto, 0m));
    }

    /// <summary>K10.7-b: motivos por los que el documento no tiene su soporte fiscal completo (vacío = completo).</summary>
    public static IReadOnlyList<string> MotivosPendiente(SaldoIvaDocumento s, string? metodoPago, bool? tieneRep)
    {
        var motivos = new List<string>();
        if (string.IsNullOrWhiteSpace(s.Uuid)) motivos.Add("Sin CFDI");
        if (s.Estado == EstadoSaldoIva.Cancelado) motivos.Add("Documento cancelado");
        else if (!s.Saldado) motivos.Add(s.Lado == LadoIva.Acreditable ? "Con saldo por pagar" : "Con saldo por cobrar");
        if (metodoPago == "PPD" && tieneRep != true) motivos.Add("PPD pagado sin REP");
        return motivos;
    }

    private static (DateOnly Desde, DateOnly Hasta) Mes(int anio, int mes)
    {
        var desde = new DateOnly(anio, mes, 1);
        return (desde, desde.AddMonths(1).AddDays(-1));
    }

    /// <summary>Movimiento neto del mes (sin borradores) en las cuentas del rol, con el signo de su naturaleza.</summary>
    private async Task<CuadreRolIva> CuadreAsync(string rol, string descripcion, decimal porDocumentos, bool acreedora, DateOnly desde,
        DateOnly hasta, CancellationToken ct)
    {
        var cuentas = await db.RolesCuenta.AsNoTracking().Where(r => r.Codigo == rol && r.DeletedAt == null && r.CuentaId != null)
            .Select(r => r.CuentaId!.Value).Distinct().ToListAsync(ct);
        var codigos = await db.Cuentas.AsNoTracking().Where(c => cuentas.Contains(c.Id)).Select(c => c.Codigo).OrderBy(c => c).ToListAsync(ct);
        var neto = await db.Polizas.AsNoTracking()
            .Where(p => p.Fecha >= desde && p.Fecha <= hasta && p.Estado != EstadoPoliza.Borrador)
            .SelectMany(p => p.Partidas).Where(x => cuentas.Contains(x.CuentaId))
            .SumAsync(x => acreedora ? x.Abono - x.Cargo : x.Cargo - x.Abono, ct);
        var diferencia = neto - porDocumentos;
        return new(rol, descripcion, codigos, porDocumentos, neto, diferencia, diferencia == 0m);
    }

    private async Task<List<DocumentoLiquidacionIva>> DocumentosAsync(List<ReclasificacionIva> movimientos,
        Dictionary<Guid, SaldoIvaDocumento> saldos, CancellationToken ct)
    {
        var proveedor = saldos.Values.Where(s => s.Lado == LadoIva.Acreditable).Select(s => s.DocumentoId).ToList();
        var venta = saldos.Values.Where(s => s.Lado == LadoIva.Trasladado && s.OrigenTipo == NormalizadorEventos.DocComprobante.Tipo)
            .Select(s => s.DocumentoId).ToList();
        var cxp = await cumplimiento.FacturasProveedorAsync(proveedor, ct);
        var fac = await cumplimiento.FacturasVentaAsync(venta, ct);

        return [.. movimientos.GroupBy(r => r.SaldoIvaDocumentoId).Select(g =>
        {
            var s = saldos[g.Key];
            var dato = (s.Lado == LadoIva.Acreditable ? cxp : fac).GetValueOrDefault(s.DocumentoId);
            var metodo = dato?.MetodoPago ?? s.MetodoPago;
            var motivos = MotivosPendiente(s, metodo, dato?.TieneRep);
            return new DocumentoLiquidacionIva(s.Id, s.Lado, s.OrigenTipo, s.DocumentoId, s.Folio, s.Uuid, metodo, s.Fecha, s.Moneda, s.Base,
                s.Iva, g.Sum(r => r.Importe), g.Sum(r => r.IvaMxn), g.Sum(r => r.IvaRetenidoMxn), s.IvaPendiente, s.Saldo, motivos.Count == 0,
                motivos);
        }).OrderBy(d => d.Lado).ThenBy(d => d.Completo).ThenBy(d => d.Fecha)];
    }
}
