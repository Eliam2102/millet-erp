using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.CuentaPuente;

/// <summary>Columna del reporte para el frontend (ADR-0036). <c>Clave</c> = propiedad camelCase de la fila.</summary>
public sealed record ColumnaReportePuente(string Clave, string Etiqueta, string Tipo, string? Alineacion = null);

public sealed record CuentaPuenteRef(Guid Id, string Codigo, string Nombre);

/// <summary>Saldo de la puente de una OC al corte (una fila del reporte).</summary>
public sealed record SaldoPuenteOcResponse(
    Guid OrdenCompraId, string? OrdenCompraFolio, Guid? ProveedorId, string? ProveedorClave, string? ProveedorNombre,
    IReadOnlyList<Guid> SucursalIds, string? SucursalNombre, decimal Cargos, decimal Abonos, decimal Saldo,
    NaturalezaSaldoPuente Naturaleza, string NaturalezaEtiqueta, DateOnly? FechaPrimerMovimientoSinSaldar, int? DiasAntiguedad,
    TramoAntiguedadPuente? Tramo, string? TramoEtiqueta, decimal? DiferenciaCambiaria, DateOnly UltimoMovimiento);

/// <summary>
/// Totales de la conciliación. <c>SaldoBalanza</c> = saldo de la cuenta puente en balanza al corte (misma sucursal si se filtró);
/// <c>PartidasSinOc</c> = lo que la puente tiene sin OC (pólizas manuales o ajustes). Con filtro de proveedor no se concilia contra
/// balanza (la balanza no se abre por proveedor): esos tres campos van en null.
/// </summary>
public sealed record TotalesConciliacionPuente(
    decimal TotalReporte, decimal? SaldoBalanza, decimal? PartidasSinOc, bool? Cuadra, int OrdenesConSaldo,
    decimal FacturadoSinRecibir, decimal RecibidoSinFacturar, decimal Tramo0a30, decimal Tramo31a60, decimal TramoMasDe60);

/// <summary>Reporte de conciliación de la cuenta puente recepción–factura (K10.5), forma ADR-0036 con filas y totales tipados.</summary>
public sealed record ConciliacionPuenteResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaReportePuente> Columnas, IReadOnlyList<SaldoPuenteOcResponse> Filas, TotalesConciliacionPuente Totales,
    DateOnly Corte, CuentaPuenteRef CuentaPuente);

/// <summary>
/// Conciliación de la cuenta puente por OC al corte (K10.5-a/b/c/f). Por cada OC con movimientos hasta el corte: saldo, naturaleza,
/// antigüedad desde el primer movimiento sin saldar y tramo. <c>SoloConSaldo</c> oculta las OC saldadas (no cambia los totales).
/// Contabilidad es corporativa: como la balanza y la bandeja de pólizas, la consulta no se limita por las sucursales del usuario; el
/// filtro de sucursal es opcional.
/// </summary>
public sealed record ConciliacionPuenteQuery(DateOnly Corte, Guid? SucursalId = null, Guid? ProveedorId = null, bool SoloConSaldo = true)
    : IRequest<ConciliacionPuenteResponse>;

public sealed class ConciliacionPuenteValidator : AbstractValidator<ConciliacionPuenteQuery>
{
    public ConciliacionPuenteValidator()
    {
        RuleFor(q => q.Corte.Year).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("corte");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
        RuleFor(q => q.ProveedorId).NotEqual(Guid.Empty).When(q => q.ProveedorId is not null);
    }
}

/// <summary>Resuelve la cuenta puente configurada y los nombres (proveedor, sucursal) del reporte.</summary>
public sealed class LecturaCuentaPuente(ContabilidadDbContext db, IOptions<CuentaPuenteOpciones> opciones)
{
    public async Task<CuentaPuenteRef> CuentaAsync(CancellationToken ct)
    {
        var codigo = FormatoCatalogo.Codigo(opciones.Value.CodigoCuenta);
        if (codigo is null)
            throw new BusinessRuleException("PUENTE_CUENTA_NO_CONFIGURADA",
                $"No está configurada la cuenta puente recepción–factura ({CuentaPuenteOpciones.Seccion}:CodigoCuenta).");
        return await db.Cuentas.AsNoTracking().Where(c => c.Codigo == codigo).Select(c => new CuentaPuenteRef(c.Id, c.Codigo, c.Nombre)).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("PUENTE_CUENTA_NO_ENCONTRADA", $"La cuenta puente configurada ({codigo}) no existe en el catálogo.");
    }
}

public sealed class ConciliacionPuenteHandler(
    ContabilidadDbContext db, LecturaCuentaPuente lectura, ISaldosContablesReadPort saldos, ITerceroContabilidadPort terceros,
    ISucursalContabilidadPort sucursales, IClock clock) : IRequestHandler<ConciliacionPuenteQuery, ConciliacionPuenteResponse>
{
    private static readonly ColumnaReportePuente[] Columnas =
    [
        new("ordenCompraFolio", "Orden de compra", "texto"),
        new("proveedorNombre", "Proveedor", "texto"),
        new("sucursalNombre", "Sucursal", "texto"),
        new("cargos", "Facturado (cargos)", "moneda", "derecha"),
        new("abonos", "Recibido (abonos)", "moneda", "derecha"),
        new("saldo", "Saldo", "moneda", "derecha"),
        new("naturalezaEtiqueta", "Naturaleza", "texto"),
        new("fechaPrimerMovimientoSinSaldar", "Desde", "fecha"),
        new("diasAntiguedad", "Días", "numero", "derecha"),
        new("tramoEtiqueta", "Antigüedad", "texto"),
        new("diferenciaCambiaria", "Diferencia cambiaria (informativa)", "moneda", "derecha"),
    ];

    public async Task<ConciliacionPuenteResponse> Handle(ConciliacionPuenteQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuenta = await lectura.CuentaAsync(ct);
        var q = db.MovimientosPuente.AsNoTracking().Where(m => m.CuentaId == cuenta.Id && m.Fecha <= request.Corte);
        if (request.SucursalId is { } s) q = q.Where(m => m.SucursalId == s);
        var movimientos = await q.ToListAsync(ct);

        var porOc = movimientos.GroupBy(m => m.OrdenCompraId)
            .Select(g => new
            {
                OcId = g.Key,
                Folio = g.Select(m => m.OrdenCompraFolio).FirstOrDefault(f => f is not null),
                ProveedorId = g.OrderBy(m => m.Fecha).Select(m => m.ProveedorId).FirstOrDefault(p => p is not null),
                Sucursales = g.Select(m => m.SucursalId).Distinct().ToList(),
                Ultimo = g.Max(m => m.Fecha),
                Saldo = ConciliacionPuente.Calcular(g, request.Corte),
            })
            .Where(x => request.ProveedorId is null || x.ProveedorId == request.ProveedorId)
            .ToList();

        var proveedores = await terceros.ObtenerAsync(TipoAuxiliar.Proveedor,
            [.. porOc.Where(x => x.ProveedorId is not null).Select(x => x.ProveedorId!.Value).Distinct()], ct);
        var suc = (await sucursales.ListarAsync(ct)).ToDictionary(x => x.Id);

        var filas = porOc
            .Select(x =>
            {
                var prov = x.ProveedorId is { } p ? proveedores.GetValueOrDefault(p) : null;
                var sucNombre = string.Join(", ", x.Sucursales.Select(id => suc.GetValueOrDefault(id)?.Nombre ?? id.ToString()).Order(StringComparer.Ordinal));
                var r = x.Saldo;
                return new SaldoPuenteOcResponse(x.OcId, x.Folio, x.ProveedorId, prov?.Clave, prov?.Nombre, x.Sucursales, sucNombre,
                    r.Cargos, r.Abonos, r.Saldo, r.Naturaleza, ConciliacionPuente.Etiqueta(r.Naturaleza), r.FechaPrimerMovimientoSinSaldar,
                    r.DiasAntiguedad, r.Tramo, r.Tramo is { } t ? ConciliacionPuente.Etiqueta(t) : null, r.DiferenciaCambiaria, x.Ultimo);
            })
            .OrderByDescending(f => f.DiasAntiguedad ?? -1).ThenBy(f => f.OrdenCompraFolio ?? f.OrdenCompraId.ToString(), StringComparer.Ordinal)
            .ToList();

        var total = filas.Sum(f => f.Saldo);
        decimal? balanza = null, sinOc = null;
        bool? cuadra = null;
        if (request.ProveedorId is null)
        {
            var saldo = await saldos.ObtenerAsync(request.Corte, [cuenta.Id], request.SucursalId is { } su ? [su] : null, ct);
            balanza = saldo.GetValueOrDefault(cuenta.Id);
            sinOc = balanza - total;
            cuadra = Math.Abs(sinOc.Value) < MovimientoPuente.Tolerancia;
        }
        var conSaldo = filas.Where(f => f.Naturaleza != NaturalezaSaldoPuente.Saldada).ToList();
        decimal Tramo(TramoAntiguedadPuente t) => conSaldo.Where(f => f.Tramo == t).Sum(f => f.Saldo);
        var totales = new TotalesConciliacionPuente(total, balanza, sinOc, cuadra, conSaldo.Count,
            conSaldo.Where(f => f.Saldo > 0m).Sum(f => f.Saldo), conSaldo.Where(f => f.Saldo < 0m).Sum(f => f.Saldo),
            Tramo(TramoAntiguedadPuente.De0a30), Tramo(TramoAntiguedadPuente.De31a60), Tramo(TramoAntiguedadPuente.MasDe60));

        var filtros = new Dictionary<string, string?>
        {
            ["Corte"] = request.Corte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Cuenta puente"] = $"{cuenta.Codigo} {cuenta.Nombre}",
            ["Sucursal"] = request.SucursalId is { } fs ? suc.GetValueOrDefault(fs)?.Nombre ?? fs.ToString() : "Todas",
            ["Proveedor"] = request.ProveedorId is { } fp
                ? (await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, [fp], ct)).GetValueOrDefault(fp)?.Nombre ?? fp.ToString() : "Todos",
            ["Solo con saldo"] = request.SoloConSaldo ? "Sí" : "No",
        };
        return new ConciliacionPuenteResponse("Conciliación de la cuenta puente recepción–factura", clock.UtcNow, filtros, Columnas,
            request.SoloConSaldo ? conSaldo : filas, totales, request.Corte, cuenta);
    }
}

/// <summary>Movimiento de la puente de una OC con el saldo acumulado (detalle del reporte).</summary>
public sealed record MovimientoPuenteResponse(
    Guid Id, Guid PolizaId, string? PolizaFolio, DateOnly Fecha, TipoDocumentoPuente TipoDocumento, Guid DocumentoId,
    Guid? FacturaOrigenId, Guid? LineaOcId, Guid SucursalId, decimal Cargo, decimal Abono, decimal SaldoAcumulado,
    string Moneda, decimal TipoCambio, decimal? ImporteOriginal, bool EsReversa, Guid? MovimientoRevertidoId);

public sealed record MovimientosPuenteOcResponse(
    Guid OrdenCompraId, string? OrdenCompraFolio, Guid? ProveedorId, string? ProveedorNombre, DateOnly Corte, CuentaPuenteRef CuentaPuente,
    SaldoPuenteOcResponse Saldo, IReadOnlyList<MovimientoPuenteResponse> Movimientos);

/// <summary>Detalle de la puente de una OC al corte: sus movimientos en orden con el saldo acumulado.</summary>
public sealed record MovimientosPuenteOcQuery(Guid OrdenCompraId, DateOnly Corte) : IRequest<MovimientosPuenteOcResponse>;

public sealed class MovimientosPuenteOcValidator : AbstractValidator<MovimientosPuenteOcQuery>
{
    public MovimientosPuenteOcValidator()
    {
        RuleFor(q => q.OrdenCompraId).NotEmpty();
        RuleFor(q => q.Corte.Year).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("corte");
    }
}

public sealed class MovimientosPuenteOcHandler(ContabilidadDbContext db, LecturaCuentaPuente lectura, ITerceroContabilidadPort terceros,
    ISucursalContabilidadPort sucursales) : IRequestHandler<MovimientosPuenteOcQuery, MovimientosPuenteOcResponse>
{
    public async Task<MovimientosPuenteOcResponse> Handle(MovimientosPuenteOcQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuenta = await lectura.CuentaAsync(ct);
        var movs = (await db.MovimientosPuente.AsNoTracking()
                .Where(m => m.CuentaId == cuenta.Id && m.OrdenCompraId == request.OrdenCompraId && m.Fecha <= request.Corte).ToListAsync(ct))
            .OrderBy(m => m.Fecha).ThenBy(m => m.EsReversa).ThenBy(m => m.Id).ToList();
        if (movs.Count == 0)
            throw new EntityNotFoundException("PUENTE_OC_SIN_MOVIMIENTOS", $"La orden de compra '{request.OrdenCompraId}' no tiene movimientos en la cuenta puente al corte.");

        var folio = movs.Select(m => m.OrdenCompraFolio).FirstOrDefault(f => f is not null);
        var proveedorId = movs.Select(m => m.ProveedorId).FirstOrDefault(p => p is not null);
        var prov = proveedorId is { } p ? (await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, [p], ct)).GetValueOrDefault(p) : null;
        var suc = (await sucursales.ListarAsync(ct)).ToDictionary(x => x.Id);
        var r = ConciliacionPuente.Calcular(movs, request.Corte);
        var sucIds = movs.Select(m => m.SucursalId).Distinct().ToList();
        var saldo = new SaldoPuenteOcResponse(request.OrdenCompraId, folio, proveedorId, prov?.Clave, prov?.Nombre, sucIds,
            string.Join(", ", sucIds.Select(id => suc.GetValueOrDefault(id)?.Nombre ?? id.ToString()).Order(StringComparer.Ordinal)),
            r.Cargos, r.Abonos, r.Saldo, r.Naturaleza, ConciliacionPuente.Etiqueta(r.Naturaleza), r.FechaPrimerMovimientoSinSaldar,
            r.DiasAntiguedad, r.Tramo, r.Tramo is { } t ? ConciliacionPuente.Etiqueta(t) : null, r.DiferenciaCambiaria, movs[^1].Fecha);

        var acumulado = 0m;
        var detalle = movs.Select(m =>
        {
            acumulado += m.Cargo - m.Abono;
            return new MovimientoPuenteResponse(m.Id, m.PolizaId, m.PolizaFolio, m.Fecha, m.TipoDocumento, m.DocumentoId, m.FacturaOrigenId,
                m.LineaOcId, m.SucursalId, m.Cargo, m.Abono, acumulado, m.Moneda, m.TipoCambio, m.ImporteOriginal, m.EsReversa, m.MovimientoRevertidoId);
        }).ToList();
        return new MovimientosPuenteOcResponse(request.OrdenCompraId, folio, proveedorId, prov?.Nombre, request.Corte, cuenta, saldo, detalle);
    }
}
