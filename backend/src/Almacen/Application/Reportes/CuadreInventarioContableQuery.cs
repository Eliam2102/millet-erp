using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Reportes;

/// <summary>
/// F1-ALM-13-a: cuadre del cierre de mes. Compara la valuación del ALFAK al último día del mes (la foto del cierre si el mes ya se
/// cerró) contra el saldo de la cuenta de inventario en Contabilidad, por sucursal. La cuenta es la del rol <c>INVENTARIO</c> con
/// la que Contabilidad registra los movimientos de almacén; si no hay cuenta configurada se dice, no se inventa. El detalle por
/// almacén solo trae la valuación: la cuenta se configura por sucursal.
/// <para>Cuadre contra el kardex (misma cuenta que hará el kardex 86e3tac22): por almacén y artículo, valuación al cierre =
/// valuación al cierre anterior + movimientos del mes valorizados con el costo de cada línea. Solo se listan los que no cuadran;
/// la diferencia es un cambio de valor sin movimiento (p. ej. incrementables de CE o restauración de costo al cancelar).</para>
/// </summary>
public sealed record CuadreInventarioContableQuery(int Anio, int Mes) : IRequest<ReporteResponse<CuadreInventarioFila>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.reportes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class CuadreInventarioContableQueryValidator : AbstractValidator<CuadreInventarioContableQuery>
{
    public CuadreInventarioContableQueryValidator()
    {
        RuleFor(x => x.Anio).InclusiveBetween(2000, 2999);
        RuleFor(x => x.Mes).InclusiveBetween(1, 12);
    }
}

/// <summary><c>Nivel</c> = «Sucursal» (con cuenta, saldo y diferencia) o «Almacén» (solo valuación). Diferencia = valuación − saldo.</summary>
/// «Kardex» = almacén y artículo cuya valuación no es la del cierre anterior más los movimientos del mes (<c>SaldoContable</c> nulo).
public sealed record CuadreInventarioFila(
    string Nivel, Guid SucursalId, string? Sucursal, string? Almacen, string? Cuenta,
    decimal Valuacion, decimal? SaldoContable, decimal? Diferencia, string? Observacion, string? Articulo = null);

public sealed class CuadreInventarioContableHandler(
    AlmacenDbContext db, IInventarioContableReadPort contabilidad, ISucursalReadPort sucursales, ICurrentEmpresaContext empresa,
    IArticuloReadPort articulos)
    : IRequestHandler<CuadreInventarioContableQuery, ReporteResponse<CuadreInventarioFila>>
{
    // ponytail: tolerancia fija por redondeo a centavos de cada grupo (medida, lote); si Contabilidad pide cero exacto, valuar sin redondear por grupo.
    public const decimal ToleranciaKardex = 0.10m;
    public const string SinCuenta = "Sin cuenta configurada (rol INVENTARIO en Contabilidad)";

    public async Task<ReporteResponse<CuadreInventarioFila>> Handle(CuadreInventarioContableQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var corte = new DateOnly(request.Anio, request.Mes, DateTime.DaysInMonth(request.Anio, request.Mes));
        var valuacion = await AlfakHistorialAlmacenHandler.AplicarFotoCierreAsync(db, corte,
            await AlfakHistorialAlmacenHandler.CalcularAsync(db, corte, corte, null, null, request.SucursalesPermitidas, ct), ct);
        var fotoCierre = await db.ValuacionesCierre.AnyAsync(v => v.Anio == request.Anio && v.Mes == request.Mes, ct);

        var almacenes = await (from s in db.SubAlmacenes.AsNoTracking()
                               join a in db.Almacenes.AsNoTracking() on s.AlmacenId equals a.Id
                               where request.SucursalesPermitidas == null || request.SucursalesPermitidas.Contains(a.SucursalId)
                               select new { SubAlmacenId = s.Id, a.Clave, a.Nombre, a.SucursalId }).ToListAsync(ct);
        var porSub = almacenes.ToDictionary(x => x.SubAlmacenId);
        var valorPorAlmacen = (from f in valuacion
                               where porSub.ContainsKey(f.SubAlmacenId)
                               let a = porSub[f.SubAlmacenId]
                               group f.ValorInventarioFinal by (a.SucursalId, Almacen: $"{a.Clave} · {a.Nombre}"))
            .ToDictionary(g => g.Key, g => g.Sum());

        // Sucursales de la empresa con almacén; una sucursal que no resuelve (doble de pruebas) entra si tiene valuación.
        var ids = new List<Guid>();
        var nombres = new Dictionary<Guid, string?>();
        foreach (var id in almacenes.Select(a => a.SucursalId).Distinct())
        {
            var s = await sucursales.ObtenerAsync(id, ct);
            if (s is null ? !valorPorAlmacen.Keys.Any(k => k.SucursalId == id) : s.EmpresaId != empresa.Current) continue;
            ids.Add(id);
            nombres[id] = s is null ? null : $"{s.Clave} · {s.Nombre}";
        }
        var cuentas = await contabilidad.ObtenerAsync(corte, ids, ct);

        var filas = new List<CuadreInventarioFila>();
        foreach (var id in ids.OrderBy(i => nombres[i]))
        {
            var detalle = valorPorAlmacen.Where(x => x.Key.SucursalId == id).OrderBy(x => x.Key.Almacen).ToList();
            var total = detalle.Sum(x => x.Value);
            var c = cuentas.GetValueOrDefault(id);
            filas.Add(c?.CuentaId is null
                ? new("Sucursal", id, nombres[id], null, null, total, null, null, SinCuenta)
                : new("Sucursal", id, nombres[id], null, $"{c.CuentaCodigo} · {c.CuentaNombre}", total, c.Saldo ?? 0m,
                    total - (c.Saldo ?? 0m), total == (c.Saldo ?? 0m) ? "Cuadra" : "No cuadra"));
            filas.AddRange(detalle.Select(x => new CuadreInventarioFila("Almacén", id, nombres[id], x.Key.Almacen, null, x.Value, null, null, null)));
        }

        var kardex = await KardexAsync(request, corte, valuacion, porSub.ToDictionary(x => x.Key, x => (x.Value.SucursalId, $"{x.Value.Clave} · {x.Value.Nombre}")), ct);
        filas.AddRange(kardex.Where(k => ids.Contains(k.SucursalId)).Select(k => k with { Sucursal = nombres[k.SucursalId] }));

        var conCuenta = filas.Where(f => f.Nivel == "Sucursal" && f.SaldoContable is not null).ToList();
        return new ReporteResponse<CuadreInventarioFila>(
            Titulo: "CUADRE-INVENTARIO-CONTABILIDAD",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["corte"] = corte.ToString("yyyy-MM-dd"),
                ["foto_cierre"] = fotoCierre ? "sí" : "no",
                ["kardex_diferencias"] = kardex.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Columnas: Columnas,
            Filas: filas,
            Totales: new Dictionary<string, decimal>
            {
                ["valuacion"] = filas.Where(f => f.Nivel == "Sucursal").Sum(f => f.Valuacion),
                ["saldoContable"] = conCuenta.Sum(f => f.SaldoContable!.Value),
                ["diferencia"] = conCuenta.Sum(f => f.Diferencia!.Value),
            });
    }

    /// <summary>Almacén y artículo donde cierre ≠ cierre anterior + movimientos valorizados del mes (tolerancia por redondeo: <see cref="ToleranciaKardex"/>).</summary>
    private async Task<List<CuadreInventarioFila>> KardexAsync(CuadreInventarioContableQuery request, DateOnly corte,
        List<AlfakHistorialFila> cierre, Dictionary<Guid, (Guid SucursalId, string Almacen)> porSub, CancellationToken ct)
    {
        var inicio = new DateOnly(corte.Year, corte.Month, 1);
        var anteriorCorte = inicio.AddDays(-1);
        var anterior = await AlfakHistorialAlmacenHandler.AplicarFotoCierreAsync(db, anteriorCorte,
            await AlfakHistorialAlmacenHandler.CalcularAsync(db, anteriorCorte, anteriorCorte, null, null, request.SucursalesPermitidas, ct), ct);
        var lineas = await AlfakHistorialAlmacenHandler.LineasKardex(db, null, null, request.SucursalesPermitidas, inicio, corte).ToListAsync(ct);
        var filas = Conciliar(
            cierre.Select(f => (f.SubAlmacenId, f.ArticuloId, f.ValorInventarioFinal)),
            anterior.Select(f => (f.SubAlmacenId, f.ArticuloId, f.ValorInventarioFinal)),
            lineas.Select(l => (l.SubAlmacenId, l.ArticuloId, (TipoMovimientoExtensions.EsEntrada(l.Tipo) ? 1m : -1m) * l.Cantidad * l.CostoUnitarioMxn)),
            porSub);
        var nombres = filas.Count == 0 ? new Dictionary<Guid, ArticuloLectura>()
            : await articulos.ObtenerPorIdsAsync(filas.Select(f => f.ArticuloId).Distinct().ToArray(), ct);
        return [.. filas.Select(f => new CuadreInventarioFila("Kardex", f.SucursalId, null, f.Almacen, null, f.Cierre, null, f.Diferencia,
            $"Cierre anterior {f.Anterior:N2} + movimientos {f.Movimientos:N2} = {f.Anterior + f.Movimientos:N2}",
            nombres.TryGetValue(f.ArticuloId, out var a) ? $"{a.Clave} · {a.Descripcion}" : f.ArticuloId.ToString()))];
    }

    /// <summary>Cuenta del kardex por almacén y artículo: diferencia = cierre − (anterior + movimientos). Devuelve solo las que no cuadran.</summary>
    internal static List<(Guid SucursalId, string Almacen, Guid ArticuloId, decimal Cierre, decimal Anterior, decimal Movimientos, decimal Diferencia)> Conciliar(
        IEnumerable<(Guid Sub, Guid Articulo, decimal Valor)> cierre, IEnumerable<(Guid Sub, Guid Articulo, decimal Valor)> anterior,
        IEnumerable<(Guid Sub, Guid Articulo, decimal Valor)> movimientos, IReadOnlyDictionary<Guid, (Guid SucursalId, string Almacen)> porSub)
    {
        static Dictionary<(Guid, string, Guid), decimal> Sumar(IEnumerable<(Guid Sub, Guid Articulo, decimal Valor)> xs,
            IReadOnlyDictionary<Guid, (Guid SucursalId, string Almacen)> porSub) =>
            xs.Where(x => porSub.ContainsKey(x.Sub))
                .GroupBy(x => (porSub[x.Sub].SucursalId, porSub[x.Sub].Almacen, x.Articulo))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Valor));
        var c = Sumar(cierre, porSub); var a = Sumar(anterior, porSub); var m = Sumar(movimientos, porSub);
        return [.. c.Keys.Union(a.Keys).Union(m.Keys)
            .Select(k => (k.Item1, k.Item2, k.Item3, Cierre: c.GetValueOrDefault(k), Anterior: a.GetValueOrDefault(k), Movs: Math.Round(m.GetValueOrDefault(k), 2)))
            .Select(x => (x.Item1, x.Item2, x.Item3, x.Cierre, x.Anterior, x.Movs, Diferencia: x.Cierre - x.Anterior - x.Movs))
            .Where(x => Math.Abs(x.Diferencia) > ToleranciaKardex)
            .OrderBy(x => x.Item2).ThenBy(x => x.Item3)];
    }

    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("nivel", "Nivel", "texto"),
        new("sucursal", "Sucursal", "texto"),
        new("almacen", "Almacén", "texto"),
        new("articulo", "Artículo", "texto"),
        new("cuenta", "Cuenta de inventario", "texto"),
        new("valuacion", "Valuación", "moneda", "right"),
        new("saldoContable", "Saldo contable", "moneda", "right"),
        new("diferencia", "Diferencia", "moneda", "right"),
        new("observacion", "Observación", "texto"),
    ];
}
