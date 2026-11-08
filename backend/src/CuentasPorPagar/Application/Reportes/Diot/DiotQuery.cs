using System.Globalization;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Reportes.Diot;

/// <summary>F1-CXP-10 · DIOT del mes (flujo de efectivo): renglones por tercero y tipo de operación.</summary>
public sealed record DiotQuery(int Anio, int Mes) : IRequest<ReporteJsonResponse>;

/// <summary>F1-CXP-10 · Archivo TXT de carga masiva al SAT del mismo mes.</summary>
public sealed record DiotArchivoQuery(int Anio, int Mes) : IRequest<DiotArchivo>;

public sealed record DiotArchivo(string NombreArchivo, byte[] Contenido, int Renglones);

public sealed class DiotQueryValidator : AbstractValidator<DiotQuery>
{
    public DiotQueryValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2020, 2100).WithMessage("Año inválido.");
        RuleFor(q => q.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

public sealed class DiotArchivoQueryValidator : AbstractValidator<DiotArchivoQuery>
{
    public DiotArchivoQueryValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2020, 2100).WithMessage("Año inválido.");
        RuleFor(q => q.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

/// <summary>
/// Lee los movimientos del mes que cuentan para la DIOT:
/// <list type="bullet">
/// <item>Pagos de Tesorería (<see cref="PagoProveedorLocal"/>) no revertidos, por su fecha de pago.</item>
/// <item>Pagos sin Tesorería (tarjeta corporativa): movimiento de pago del pasivo, neto del mes.</item>
/// <item>Notas de crédito de descuento o devolución por su fecha de CFDI, como devoluciones.</item>
/// </list>
/// Las facturas canceladas no cuentan. Caja chica y viáticos no tienen pago propio (se paga la reposición): quedan fuera.
/// </summary>
public sealed class LectorDiot(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
{
    public async Task<(IReadOnlyList<RenglonDiot> Renglones, int Pagos)> LeerAsync(int anio, int mes, CancellationToken ct)
    {
        if (anio is < 2020 or > 2100 || mes is < 1 or > 12)
            throw new BusinessRuleException("CXP_DIOT_PERIODO_INVALIDO", "Elige un mes válido para la DIOT.");
        var desde = new DateOnly(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);

        var pagos = await db.PagosProveedorLocal.AsNoTracking()
            .Where(p => p.FechaPago >= desde && p.FechaPago <= hasta && !p.Revertido && p.Importe > 0)
            .Select(p => new { p.FacturaProveedorId, p.Importe, p.TipoCambio })
            .ToListAsync(ct);

        var conTesoreria = db.PagosProveedorLocal.Select(p => p.FacturaProveedorId);
        var otrosPagos = await db.MovimientosPasivo.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimientoPasivo.Pago && m.Fecha >= desde && m.Fecha <= hasta && !conTesoreria.Contains(m.FacturaProveedorId))
            .GroupBy(m => m.FacturaProveedorId)
            .Select(g => new { FacturaProveedorId = g.Key, Importe = g.Sum(m => m.Monto) })
            .ToListAsync(ct);

        var notas = await db.NotasCreditoProveedor.AsNoTracking()
            .Where(n => (n.Tipo == TipoNotaCredito.Descuento || n.Tipo == TipoNotaCredito.Devolucion)
                && n.Estado != EstadoNotaCredito.Cancelada && n.FechaCfdi >= new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                && n.FechaCfdi < new DateTimeOffset(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .Select(n => new { n.Id, n.ProveedorId, n.FacturaOrigenId, n.Moneda, n.TipoCambio, n.Subtotal, n.ImpuestosTrasladados, n.Total })
            .ToListAsync(ct);

        var idsFacturas = pagos.Select(p => p.FacturaProveedorId).Concat(otrosPagos.Select(p => p.FacturaProveedorId))
            .Concat(notas.Where(n => n.FacturaOrigenId != null).Select(n => n.FacturaOrigenId!.Value)).Distinct().ToList();
        var facturas = await db.FacturasProveedor.AsNoTracking().Include(f => f.Lineas)
            .Where(f => idsFacturas.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var movimientos = new List<MovimientoDiot>();
        foreach (var p in pagos)
            if (facturas.TryGetValue(p.FacturaProveedorId, out var f) && f.Estado != EstadoPasivo.Cancelada)
                movimientos.Add(Pago(f, p.Importe, p.TipoCambio));
        foreach (var p in otrosPagos.Where(x => x.Importe > 0))
            if (facturas.TryGetValue(p.FacturaProveedorId, out var f) && f.Estado != EstadoPasivo.Cancelada)
                movimientos.Add(Pago(f, p.Importe, null));
        foreach (var n in notas)
        {
            var origen = n.FacturaOrigenId is { } id && facturas.TryGetValue(id, out var fo) ? fo : null;
            movimientos.Add(new MovimientoDiot(n.ProveedorId, n.Id, true, n.Total, n.Total, n.Subtotal, n.ImpuestosTrasladados, 0m,
                origen?.ClasificacionNoDeducible is null ? 0m : n.ImpuestosTrasladados,
                TipoCambio(n.Moneda, null, n.TipoCambio),
                origen is null ? CalculadoraDiot.OperacionOtros : CalculadoraDiot.TipoOperacionPorClaves(origen.Lineas.Select(l => l.ClaveProdServ))));
        }

        var fiscales = await proveedores.ObtenerFiscalesPorIdsAsync(movimientos.Select(m => m.ProveedorId).Distinct().ToList(), ct);
        var terceros = fiscales.ToDictionary(kv => kv.Key,
            kv => new TerceroDiot(kv.Key, kv.Value.Rfc, kv.Value.RazonSocial, kv.Value.Extranjero, kv.Value.Pais, kv.Value.IdFiscal));
        return (CalculadoraDiot.Calcular(movimientos, terceros), pagos.Count + otrosPagos.Count(x => x.Importe > 0));
    }

    private static MovimientoDiot Pago(Domain.FacturaProveedor.FacturaProveedor f, decimal importe, decimal? tipoCambioPago) =>
        new(f.ProveedorId, f.Id, false, importe, f.Total, f.Subtotal - f.Descuentos, f.ImpuestosTrasladados,
            f.RetencionesDetalle?.Where(r => r.Impuesto == "002").Sum(r => r.Importe) ?? 0m,
            f.IvaNoAcreditable, TipoCambio(f.Moneda, tipoCambioPago, f.TipoCambio),
            CalculadoraDiot.TipoOperacionPorClaves(f.Lineas.Select(l => l.ClaveProdServ)));

    /// <summary>Pesos: 1. Otra moneda: el tipo de cambio del pago y, si no viene (pagos anteriores), el del documento.</summary>
    private static decimal TipoCambio(string? moneda, decimal? delPago, decimal? delDocumento) =>
        string.IsNullOrWhiteSpace(moneda) || moneda is "MXN" or "MXP" ? 1m : delPago ?? delDocumento ?? 1m;

    internal static string Periodo(int anio, int mes) =>
        new DateTime(anio, mes, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-MX"));
}

public sealed class DiotQueryHandler(LectorDiot lector, IClock clock) : IRequestHandler<DiotQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(DiotQuery request, CancellationToken cancellationToken)
    {
        var (renglones, pagos) = await lector.LeerAsync(request.Anio, request.Mes, cancellationToken);
        var filas = renglones.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["tipo_tercero"] = r.TipoTercero,
            ["tipo_operacion"] = r.TipoOperacion,
            ["rfc"] = r.TipoTercero == CalculadoraDiot.TerceroExtranjero ? null : r.Tercero.Rfc,
            ["id_fiscal"] = r.TipoTercero == CalculadoraDiot.TerceroExtranjero ? r.Tercero.IdFiscal : null,
            ["nombre"] = r.Tercero.Nombre,
            ["pais"] = r.TipoTercero == CalculadoraDiot.TerceroExtranjero ? r.Tercero.Pais : null,
            ["actos_16"] = r.Actos16,
            ["devoluciones_16"] = r.Devoluciones16,
            ["actos_rfn_8"] = r.ActosRfn8,
            ["devoluciones_rfn_8"] = r.DevolucionesRfn8,
            ["actos_0"] = r.ActosTasa0,
            ["iva_pagado"] = r.IvaPagado,
            ["iva_no_acreditable"] = r.IvaNoAcreditable,
            ["iva_retenido"] = r.IvaRetenido,
            ["documentos"] = r.Documentos,
            ["en_archivo"] = DiotArchivoQueryHandler.VaAlArchivo(r),
        }).ToList();

        string[] sumables = ["actos_16", "devoluciones_16", "actos_rfn_8", "devoluciones_rfn_8", "actos_0", "iva_pagado", "iva_no_acreditable", "iva_retenido", "documentos"];
        var totales = sumables.ToDictionary(k => k, k => (object?)filas.Sum(f => Convert.ToDecimal(f[k], CultureInfo.InvariantCulture)));

        return new ReporteJsonResponse($"DIOT · {LectorDiot.Periodo(request.Anio, request.Mes)}", clock.UtcNow,
            [new("Periodo", LectorDiot.Periodo(request.Anio, request.Mes)), new("Base", "Flujo de efectivo (pagos del mes)"), new("Pagos leídos", pagos.ToString(CultureInfo.InvariantCulture))],
            [
                new("tipo_tercero", "Tipo de tercero", TipoColumnaReporte.Texto, AlineacionColumna.Centro),
                new("tipo_operacion", "Tipo de operación", TipoColumnaReporte.Texto, AlineacionColumna.Centro),
                new("rfc", "RFC", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
                new("id_fiscal", "ID fiscal", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
                new("nombre", "Tercero", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
                new("pais", "País", TipoColumnaReporte.Texto, AlineacionColumna.Centro),
                new("actos_16", "Actos pagados 16 %", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("devoluciones_16", "Dev., desc. y bonif. 16 %", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("actos_rfn_8", "Actos pagados RFN 8 %", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("devoluciones_rfn_8", "Dev., desc. y bonif. RFN", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("actos_0", "Actos pagados 0 %", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("iva_pagado", "IVA pagado", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("iva_no_acreditable", "IVA no acreditable", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("iva_retenido", "IVA retenido", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("documentos", "Documentos", TipoColumnaReporte.Entero, AlineacionColumna.Derecha),
                new("en_archivo", "En el archivo", TipoColumnaReporte.Booleano, AlineacionColumna.Centro),
            ],
            filas, totales);
    }
}

public sealed class DiotArchivoQueryHandler(LectorDiot lector) : IRequestHandler<DiotArchivoQuery, DiotArchivo>
{
    /// <summary>
    /// Los proveedores extranjeros no van en el archivo de esta entrega: Contabilidad los declara por la importación
    /// (pedimento, IVA pagado en aduana), que llega en la segunda entrega de CXP-10.
    /// </summary>
    public static bool VaAlArchivo(RenglonDiot r) => r.TipoTercero != CalculadoraDiot.TerceroExtranjero && LayoutDiot.TieneMontos(r);

    public async Task<DiotArchivo> Handle(DiotArchivoQuery request, CancellationToken cancellationToken)
    {
        var (renglones, _) = await lector.LeerAsync(request.Anio, request.Mes, cancellationToken);
        var incluidos = renglones.Where(VaAlArchivo).ToList();
        var texto = LayoutDiot.Archivo(incluidos);
        return new DiotArchivo($"DIOT_{request.Anio:D4}{request.Mes:D2}.txt", new UTF8Encoding(false).GetBytes(texto), incluidos.Count);
    }
}
