using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Reportes;

// C1.7 (F1-CON-06 / CON-07): diario, mayor, auxiliar y balanza de comprobación. Forma ADR-0036: el backend devuelve filas y totales
// tipados con columnas para el frontend, que renderiza y exporta. Cuentan las pólizas autorizadas y las anuladas con su anulación
// (suman cero, K10.3-a); los borradores no. Contabilidad es corporativa: la sucursal es un filtro opcional, como en K10.5.

/// <summary>Columna del reporte para el frontend (ADR-0036). <c>Clave</c> = propiedad camelCase de la fila.</summary>
public sealed record ColumnaReporteContable(string Clave, string Etiqueta, string Tipo, string? Alineacion = null);

public sealed record CuentaReporteRef(Guid Id, string Codigo, string Nombre, short Nivel, NaturalezaCuenta? Naturaleza);

/// <summary>Movimiento contable plano (partida + datos de su póliza) para componer las consultas en SQL.</summary>
internal sealed class MovimientoContable
{
    public Guid PolizaId { get; init; }
    public int Linea { get; init; }
    public Guid CuentaId { get; init; }
    public string CuentaCodigo { get; init; } = string.Empty;
    public string? ConceptoPartida { get; init; }
    public decimal Cargo { get; init; }
    public decimal Abono { get; init; }
    public Guid SucursalId { get; init; }
    public Guid? ClienteId { get; init; }
    public Guid? ProveedorId { get; init; }
    public Guid? CuentaBancariaId { get; init; }
    public int PeriodoAnio { get; init; }
    public int PeriodoNumero { get; init; }
    public DateOnly Fecha { get; init; }
    public string TipoClave { get; init; } = string.Empty;
    public int? Numero { get; init; }
    public string ConceptoPoliza { get; init; } = string.Empty;
    public EstadoPoliza Estado { get; init; }
}

/// <summary>Lecturas compartidas por los reportes: movimientos válidos, rango de periodos, árbol de cuentas y nombres.</summary>
public sealed class LecturaReportesContables(ContabilidadDbContext db, ISucursalContabilidadPort sucursales)
{
    internal IQueryable<MovimientoContable> Movimientos(Guid? sucursalId)
    {
        var q = from x in db.PolizasPartidas.AsNoTracking()
                join p in db.Polizas.AsNoTracking() on x.PolizaId equals p.Id
                where p.Estado != EstadoPoliza.Borrador
                select new MovimientoContable
                {
                    PolizaId = p.Id, Linea = x.Linea, CuentaId = x.CuentaId, CuentaCodigo = x.CuentaCodigo, ConceptoPartida = x.Concepto,
                    Cargo = x.Cargo, Abono = x.Abono, SucursalId = x.SucursalId, ClienteId = x.ClienteId, ProveedorId = x.ProveedorId,
                    CuentaBancariaId = x.CuentaBancariaId, PeriodoAnio = p.PeriodoAnio, PeriodoNumero = p.PeriodoNumero, Fecha = p.Fecha,
                    TipoClave = p.TipoClave, Numero = p.Numero, ConceptoPoliza = p.Concepto, Estado = p.Estado,
                };
        return sucursalId is { } s ? q.Where(m => m.SucursalId == s) : q;
    }

    /// <summary>Movimientos anteriores al periodo <paramref name="desde"/> del ejercicio (todo el historial: saldo inicial).</summary>
    internal static IQueryable<MovimientoContable> Antes(IQueryable<MovimientoContable> q, int anio, int desde) =>
        q.Where(m => m.PeriodoAnio < anio || (m.PeriodoAnio == anio && m.PeriodoNumero < desde));

    internal static IQueryable<MovimientoContable> Entre(IQueryable<MovimientoContable> q, int anio, int desde, int hasta) =>
        q.Where(m => m.PeriodoAnio == anio && m.PeriodoNumero >= desde && m.PeriodoNumero <= hasta);

    internal Task<List<CuentaContable>> CuentasAsync(CancellationToken ct) => db.Cuentas.AsNoTracking().ToListAsync(ct);

    internal async Task<IReadOnlyDictionary<Guid, SucursalContable>> SucursalesAsync(CancellationToken ct) =>
        (await sucursales.ListarAsync(ct)).ToDictionary(s => s.Id);

    internal static CuentaReporteRef Ref(CuentaContable c) => new(c.Id, c.Codigo, c.Nombre, c.Nivel, c.Naturaleza);

    /// <summary>La cuenta y todas sus descendientes (una cuenta de mayor acumula a sus hijas).</summary>
    internal static HashSet<Guid> Rama(IReadOnlyList<CuentaContable> todas, Guid raiz)
    {
        var hijas = todas.Where(c => c.PadreId is not null).ToLookup(c => c.PadreId!.Value);
        var res = new HashSet<Guid> { raiz };
        var cola = new Queue<Guid>([raiz]);
        while (cola.TryDequeue(out var id))
            foreach (var h in hijas[id])
                if (res.Add(h.Id)) cola.Enqueue(h.Id);
        return res;
    }

    internal static string Periodo(int anio, int desde, int hasta) =>
        desde == hasta ? $"{anio}-{desde:D2}" : $"{anio}-{desde:D2} a {anio}-{hasta:D2}";

    internal static string? Folio(string tipoClave, int? numero) => numero is { } n ? Poliza.FormatearFolio(tipoClave, n) : null;
}

/// <summary>Reglas comunes de rango de periodos (1 a 13, el 13 es el de ajuste).</summary>
internal static class ValidacionRango
{
    public static void Rango<T>(AbstractValidator<T> v, Func<T, int> anio, Func<T, int> desde, Func<T, int> hasta)
    {
        v.RuleFor(q => anio(q)).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("anio");
        v.RuleFor(q => desde(q)).InclusiveBetween(1, PeriodoContable.NumeroAjuste).OverridePropertyName("desde");
        v.RuleFor(q => hasta(q)).InclusiveBetween(1, PeriodoContable.NumeroAjuste).OverridePropertyName("hasta");
        v.RuleFor(q => q).Must(q => desde(q) <= hasta(q)).OverridePropertyName("desde")
            .WithMessage("El periodo inicial no puede ser posterior al final.");
    }
}

// ─── Balanza de comprobación ─────────────────────────────────────────────────

/// <summary>
/// Fila de la balanza. Saldos con signo deudor (cargo − abono); <c>SaldoDeudor</c>/<c>SaldoAcreedor</c> los separan para mostrar.
/// </summary>
public sealed record FilaBalanzaResponse(
    Guid CuentaId, string Codigo, string Nombre, short Nivel, NaturalezaCuenta? Naturaleza, bool TieneHijas,
    decimal SaldoInicial, decimal Cargos, decimal Abonos, decimal SaldoFinal, decimal SaldoDeudor, decimal SaldoAcreedor);

/// <summary>
/// Totales de la balanza (siempre sobre todas las cuentas, aunque se oculten las que no tienen saldo ni movimientos).
/// <c>Cuadra</c> = cargos = abonos y saldos deudores = saldos acreedores (inicial y final).
/// </summary>
public sealed record TotalesBalanzaResponse(
    decimal SaldoInicialDeudor, decimal SaldoInicialAcreedor, decimal Cargos, decimal Abonos, decimal SaldoFinalDeudor,
    decimal SaldoFinalAcreedor, bool Cuadra, int Cuentas);

public sealed record BalanzaResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaReporteContable> Columnas, IReadOnlyList<FilaBalanzaResponse> Filas, TotalesBalanzaResponse Totales,
    int Anio, int Desde, int Hasta, short? Nivel, Guid? SucursalId);

/// <summary>
/// Balanza de comprobación (C1.7-a). Del periodo (<c>Acumulada=false</c>): movimientos del periodo <c>Hasta</c>. Acumulada: del
/// periodo 1 al <c>Hasta</c> del ejercicio. El saldo inicial es todo lo anterior al primer periodo del rango. <c>Nivel</c> (1 a 9)
/// acumula cada cuenta en su ancestro de ese nivel; sin nivel se listan las cuentas que recibieron movimientos.
/// </summary>
public sealed record BalanzaQuery(int Anio, int Hasta, bool Acumulada = false, short? Nivel = null, Guid? SucursalId = null, bool SoloConSaldo = true)
    : IRequest<BalanzaResponse>
{
    public int Desde => Acumulada ? 1 : Hasta;
}

public sealed class BalanzaValidator : AbstractValidator<BalanzaQuery>
{
    public BalanzaValidator()
    {
        ValidacionRango.Rango(this, q => q.Anio, q => q.Desde, q => q.Hasta);
        RuleFor(q => q.Nivel).InclusiveBetween((short)1, (short)9).When(q => q.Nivel is not null);
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
    }
}

public sealed class BalanzaHandler(LecturaReportesContables lectura, IClock clock) : IRequestHandler<BalanzaQuery, BalanzaResponse>
{
    private static readonly ColumnaReporteContable[] Columnas =
    [
        new("codigo", "Cuenta", "texto"),
        new("nombre", "Nombre", "texto"),
        new("saldoInicial", "Saldo inicial", "moneda", "derecha"),
        new("cargos", "Cargos", "moneda", "derecha"),
        new("abonos", "Abonos", "moneda", "derecha"),
        new("saldoDeudor", "Saldo deudor", "moneda", "derecha"),
        new("saldoAcreedor", "Saldo acreedor", "moneda", "derecha"),
    ];

    public async Task<BalanzaResponse> Handle(BalanzaQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var movs = lectura.Movimientos(request.SucursalId);
        var iniciales = await LecturaReportesContables.Antes(movs, request.Anio, request.Desde)
            .GroupBy(m => m.CuentaId).Select(g => new { Cuenta = g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) })
            .ToDictionaryAsync(x => x.Cuenta, x => x.Saldo, ct);
        var periodo = await LecturaReportesContables.Entre(movs, request.Anio, request.Desde, request.Hasta)
            .GroupBy(m => m.CuentaId).Select(g => new { Cuenta = g.Key, Cargos = g.Sum(m => m.Cargo), Abonos = g.Sum(m => m.Abono) })
            .ToDictionaryAsync(x => x.Cuenta, x => (x.Cargos, x.Abonos), ct);

        var cuentas = await lectura.CuentasAsync(ct);
        var filas = Calcular(cuentas, iniciales, periodo, request.Nivel);

        var totales = Totales(filas);
        var sucursales = await lectura.SucursalesAsync(ct);
        var filtros = new Dictionary<string, string?>
        {
            ["Periodo"] = LecturaReportesContables.Periodo(request.Anio, request.Desde, request.Hasta),
            ["Tipo"] = request.Acumulada ? "Acumulada del ejercicio" : "Del periodo",
            ["Nivel"] = request.Nivel?.ToString(CultureInfo.InvariantCulture) ?? "Cuentas con movimiento",
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas",
            ["Solo con saldo o movimientos"] = request.SoloConSaldo ? "Sí" : "No",
        };
        var visibles = request.SoloConSaldo
            ? filas.Where(f => f.SaldoInicial != 0m || f.Cargos != 0m || f.Abonos != 0m || f.SaldoFinal != 0m).ToList()
            : filas;
        return new BalanzaResponse("Balanza de comprobación", clock.UtcNow, filtros, Columnas, visibles, totales,
            request.Anio, request.Desde, request.Hasta, request.Nivel, request.SucursalId);
    }

    /// <summary>
    /// Arma las filas: sin nivel, una por cuenta con saldo o movimientos; con nivel, cada cuenta suma en su ancestro de ese nivel
    /// (las de nivel menor quedan como están) y se listan todas las cuentas de ese nivel o hojas menos profundas.
    /// </summary>
    internal static List<FilaBalanzaResponse> Calcular(
        IReadOnlyList<CuentaContable> cuentas, IReadOnlyDictionary<Guid, decimal> iniciales,
        IReadOnlyDictionary<Guid, (decimal Cargos, decimal Abonos)> periodo, short? nivel)
    {
        var porId = cuentas.ToDictionary(c => c.Id);
        var conHijas = cuentas.Where(c => c.PadreId is not null).Select(c => c.PadreId!.Value).ToHashSet();
        Guid Destino(Guid id)
        {
            if (nivel is not { } n || !porId.TryGetValue(id, out var c)) return id;
            while (c.Nivel > n && c.PadreId is { } padre && porId.TryGetValue(padre, out var p)) c = p;
            return c.Id;
        }

        var acumulado = new Dictionary<Guid, (decimal Inicial, decimal Cargos, decimal Abonos)>();
        foreach (var id in iniciales.Keys.Union(periodo.Keys))
        {
            var d = Destino(id);
            var a = acumulado.GetValueOrDefault(d);
            var (cargos, abonos) = periodo.GetValueOrDefault(id);
            acumulado[d] = (a.Inicial + iniciales.GetValueOrDefault(id), a.Cargos + cargos, a.Abonos + abonos);
        }
        if (nivel is { } nv)
            foreach (var c in cuentas.Where(c => !c.EsRubro && (c.Nivel == nv || (c.Nivel < nv && !conHijas.Contains(c.Id)))))
                acumulado.TryAdd(c.Id, default);

        return [.. acumulado
            .Select(kv =>
            {
                var c = porId.GetValueOrDefault(kv.Key);
                var (inicial, cargos, abonos) = kv.Value;
                var final = inicial + cargos - abonos;
                return new FilaBalanzaResponse(kv.Key, c?.Codigo ?? kv.Key.ToString(), c?.Nombre ?? "(cuenta no encontrada)", c?.Nivel ?? 0,
                    c?.Naturaleza, conHijas.Contains(kv.Key), inicial, cargos, abonos, final, final > 0m ? final : 0m, final < 0m ? -final : 0m);
            })
            .OrderBy(f => f.Codigo, StringComparer.Ordinal)];
    }

    internal static TotalesBalanzaResponse Totales(IReadOnlyList<FilaBalanzaResponse> filas)
    {
        var iniDeudor = filas.Where(f => f.SaldoInicial > 0m).Sum(f => f.SaldoInicial);
        var iniAcreedor = -filas.Where(f => f.SaldoInicial < 0m).Sum(f => f.SaldoInicial);
        var cargos = filas.Sum(f => f.Cargos);
        var abonos = filas.Sum(f => f.Abonos);
        var finDeudor = filas.Sum(f => f.SaldoDeudor);
        var finAcreedor = filas.Sum(f => f.SaldoAcreedor);
        return new TotalesBalanzaResponse(iniDeudor, iniAcreedor, cargos, abonos, finDeudor, finAcreedor,
            cargos == abonos && iniDeudor == iniAcreedor && finDeudor == finAcreedor, filas.Count);
    }
}

// ─── Mayor por cuenta ────────────────────────────────────────────────────────

public sealed record MovimientoMayorResponse(
    Guid PolizaId, string? PolizaFolio, int Linea, DateOnly Fecha, int PeriodoAnio, int PeriodoNumero, string Concepto, string CuentaCodigo,
    Guid SucursalId, string? SucursalNombre, EstadoPoliza EstadoPoliza, decimal Cargo, decimal Abono, decimal Saldo);

public sealed record MayorResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaReporteContable> Columnas, CuentaReporteRef Cuenta, decimal SaldoInicial, decimal Cargos, decimal Abonos,
    decimal SaldoFinal, IReadOnlyList<MovimientoMayorResponse> Filas, int Anio, int Desde, int Hasta, Guid? SucursalId);

/// <summary>
/// Mayor de una cuenta (C1.7): saldo inicial, movimientos del rango con saldo corrido y saldo final. Una cuenta de mayor incluye los
/// movimientos de todas sus descendientes. Orden: periodo, fecha, folio y línea.
/// </summary>
public sealed record MayorQuery(Guid CuentaId, int Anio, int Desde, int Hasta, Guid? SucursalId = null) : IRequest<MayorResponse>;

public sealed class MayorValidator : AbstractValidator<MayorQuery>
{
    public MayorValidator()
    {
        RuleFor(q => q.CuentaId).NotEmpty();
        ValidacionRango.Rango(this, q => q.Anio, q => q.Desde, q => q.Hasta);
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
    }
}

public sealed class MayorHandler(LecturaReportesContables lectura, IClock clock) : IRequestHandler<MayorQuery, MayorResponse>
{
    private static readonly ColumnaReporteContable[] Columnas =
    [
        new("fecha", "Fecha", "fecha"),
        new("polizaFolio", "Póliza", "texto"),
        new("concepto", "Concepto", "texto"),
        new("cuentaCodigo", "Cuenta", "texto"),
        new("sucursalNombre", "Sucursal", "texto"),
        new("cargo", "Cargo", "moneda", "derecha"),
        new("abono", "Abono", "moneda", "derecha"),
        new("saldo", "Saldo", "moneda", "derecha"),
    ];

    public async Task<MayorResponse> Handle(MayorQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuentas = await lectura.CuentasAsync(ct);
        var cuenta = cuentas.FirstOrDefault(c => c.Id == request.CuentaId)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.CuentaId}'.");
        var rama = LecturaReportesContables.Rama(cuentas, cuenta.Id).ToList();

        var movs = lectura.Movimientos(request.SucursalId).Where(m => rama.Contains(m.CuentaId));
        var inicial = await LecturaReportesContables.Antes(movs, request.Anio, request.Desde).SumAsync(m => m.Cargo - m.Abono, ct);
        var lista = (await LecturaReportesContables.Entre(movs, request.Anio, request.Desde, request.Hasta).ToListAsync(ct))
            .OrderBy(m => m.PeriodoNumero).ThenBy(m => m.Fecha).ThenBy(m => m.TipoClave, StringComparer.Ordinal)
            .ThenBy(m => m.Numero ?? int.MaxValue).ThenBy(m => m.PolizaId).ThenBy(m => m.Linea).ToList();

        var sucursales = await lectura.SucursalesAsync(ct);
        var saldo = inicial;
        var filas = new List<MovimientoMayorResponse>(lista.Count);
        foreach (var m in lista)
        {
            saldo += m.Cargo - m.Abono;
            filas.Add(new MovimientoMayorResponse(m.PolizaId, LecturaReportesContables.Folio(m.TipoClave, m.Numero), m.Linea, m.Fecha,
                m.PeriodoAnio, m.PeriodoNumero, m.ConceptoPartida ?? m.ConceptoPoliza, m.CuentaCodigo, m.SucursalId,
                sucursales.GetValueOrDefault(m.SucursalId)?.Nombre, m.Estado, m.Cargo, m.Abono, saldo));
        }
        var cargos = lista.Sum(m => m.Cargo);
        var abonos = lista.Sum(m => m.Abono);
        var filtros = new Dictionary<string, string?>
        {
            ["Cuenta"] = $"{cuenta.Codigo} {cuenta.Nombre}",
            ["Periodo"] = LecturaReportesContables.Periodo(request.Anio, request.Desde, request.Hasta),
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas",
        };
        return new MayorResponse("Mayor de la cuenta", clock.UtcNow, filtros, Columnas, LecturaReportesContables.Ref(cuenta), inicial, cargos,
            abonos, inicial + cargos - abonos, filas, request.Anio, request.Desde, request.Hasta, request.SucursalId);
    }
}

// ─── Auxiliar por tercero ────────────────────────────────────────────────────

public sealed record FilaAuxiliarResponse(
    Guid? AuxiliarId, string? Clave, string Nombre, decimal SaldoInicial, decimal Cargos, decimal Abonos, decimal SaldoFinal);

/// <summary>Totales del auxiliar. <c>Cuadra</c> = la suma de los auxiliares es el saldo de la cuenta en su mayor (mismo rango).</summary>
public sealed record TotalesAuxiliarResponse(decimal SaldoInicial, decimal Cargos, decimal Abonos, decimal SaldoFinal, decimal SinAuxiliar, bool Cuadra);

public sealed record AuxiliarResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaReporteContable> Columnas, CuentaReporteRef Cuenta, TipoAuxiliar Tipo, IReadOnlyList<FilaAuxiliarResponse> Filas,
    TotalesAuxiliarResponse Totales, int Anio, int Desde, int Hasta, Guid? SucursalId);

/// <summary>
/// Auxiliar de una cuenta por cliente, proveedor o banco (C1.7): saldo inicial, cargos, abonos y saldo final de cada tercero. Las
/// partidas sin ese dato se agrupan en «Sin auxiliar» para que la suma cuadre con el mayor. Activo fijo y empleado no se guardan en la
/// partida hoy, por eso no hay auxiliar de ellos.
/// </summary>
public sealed record AuxiliarQuery(Guid CuentaId, TipoAuxiliar Tipo, int Anio, int Desde, int Hasta, Guid? SucursalId = null, bool SoloConSaldo = true)
    : IRequest<AuxiliarResponse>;

public sealed class AuxiliarValidator : AbstractValidator<AuxiliarQuery>
{
    public AuxiliarValidator()
    {
        RuleFor(q => q.CuentaId).NotEmpty();
        RuleFor(q => q.Tipo).IsInEnum();
        ValidacionRango.Rango(this, q => q.Anio, q => q.Desde, q => q.Hasta);
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
    }
}

public sealed class AuxiliarHandler(LecturaReportesContables lectura, ITerceroContabilidadPort terceros, ICuentaBancariaContabilidadPort bancos,
    IClock clock) : IRequestHandler<AuxiliarQuery, AuxiliarResponse>
{
    private static readonly ColumnaReporteContable[] Columnas =
    [
        new("clave", "Clave", "texto"),
        new("nombre", "Nombre", "texto"),
        new("saldoInicial", "Saldo inicial", "moneda", "derecha"),
        new("cargos", "Cargos", "moneda", "derecha"),
        new("abonos", "Abonos", "moneda", "derecha"),
        new("saldoFinal", "Saldo final", "moneda", "derecha"),
    ];

    public async Task<AuxiliarResponse> Handle(AuxiliarQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuentas = await lectura.CuentasAsync(ct);
        var cuenta = cuentas.FirstOrDefault(c => c.Id == request.CuentaId)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.CuentaId}'.");
        var rama = LecturaReportesContables.Rama(cuentas, cuenta.Id).ToList();
        var movs = lectura.Movimientos(request.SucursalId).Where(m => rama.Contains(m.CuentaId));
        var conTercero = request.Tipo switch
        {
            TipoAuxiliar.Cliente => movs.Select(m => new { Tercero = m.ClienteId, m.Cargo, m.Abono, m.PeriodoAnio, m.PeriodoNumero }),
            TipoAuxiliar.Proveedor => movs.Select(m => new { Tercero = m.ProveedorId, m.Cargo, m.Abono, m.PeriodoAnio, m.PeriodoNumero }),
            _ => movs.Select(m => new { Tercero = m.CuentaBancariaId, m.Cargo, m.Abono, m.PeriodoAnio, m.PeriodoNumero }),
        };
        var (anio, desde, hasta) = (request.Anio, request.Desde, request.Hasta);
        var iniciales = await conTercero.Where(m => m.PeriodoAnio < anio || (m.PeriodoAnio == anio && m.PeriodoNumero < desde))
            .GroupBy(m => m.Tercero).Select(g => new { Tercero = g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) }).ToListAsync(ct);
        var periodo = await conTercero.Where(m => m.PeriodoAnio == anio && m.PeriodoNumero >= desde && m.PeriodoNumero <= hasta)
            .GroupBy(m => m.Tercero).Select(g => new { Tercero = g.Key, Cargos = g.Sum(m => m.Cargo), Abonos = g.Sum(m => m.Abono) }).ToListAsync(ct);

        var claves = iniciales.Select(x => x.Tercero).Union(periodo.Select(x => x.Tercero)).ToList();
        var ids = claves.Where(t => t is not null).Select(t => t!.Value).ToList();
        var nombres = request.Tipo == TipoAuxiliar.Banco ? await bancos.ObtenerAsync(ids, ct) : await terceros.ObtenerAsync(request.Tipo, ids, ct);

        var filas = claves.Select(t =>
            {
                var inicial = iniciales.Where(x => x.Tercero == t).Sum(x => x.Saldo);
                var mov = periodo.FirstOrDefault(x => x.Tercero == t);
                var (cargos, abonos) = (mov?.Cargos ?? 0m, mov?.Abonos ?? 0m);
                var aux = t is { } id ? nombres.GetValueOrDefault(id) : null;
                return new FilaAuxiliarResponse(t, aux?.Clave, t is null ? "Sin auxiliar" : aux?.Nombre ?? t.Value.ToString(),
                    inicial, cargos, abonos, inicial + cargos - abonos);
            })
            .OrderBy(f => f.AuxiliarId is null).ThenBy(f => f.Nombre, StringComparer.CurrentCulture).ToList();

        var saldoMayor = await movs.Where(m => m.PeriodoAnio < anio || (m.PeriodoAnio == anio && m.PeriodoNumero <= hasta))
            .SumAsync(m => m.Cargo - m.Abono, ct);
        var totalFinal = filas.Sum(f => f.SaldoFinal);
        var totales = new TotalesAuxiliarResponse(filas.Sum(f => f.SaldoInicial), filas.Sum(f => f.Cargos), filas.Sum(f => f.Abonos), totalFinal,
            filas.Where(f => f.AuxiliarId is null).Sum(f => f.SaldoFinal), totalFinal == saldoMayor);

        var sucursales = await lectura.SucursalesAsync(ct);
        var filtros = new Dictionary<string, string?>
        {
            ["Cuenta"] = $"{cuenta.Codigo} {cuenta.Nombre}",
            ["Auxiliar"] = request.Tipo switch { TipoAuxiliar.Cliente => "Clientes", TipoAuxiliar.Proveedor => "Proveedores", _ => "Bancos" },
            ["Periodo"] = LecturaReportesContables.Periodo(anio, desde, hasta),
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas",
            ["Solo con saldo o movimientos"] = request.SoloConSaldo ? "Sí" : "No",
        };
        var visibles = request.SoloConSaldo
            ? filas.Where(f => f.SaldoInicial != 0m || f.Cargos != 0m || f.Abonos != 0m || f.SaldoFinal != 0m).ToList()
            : filas;
        return new AuxiliarResponse("Auxiliar de la cuenta", clock.UtcNow, filtros, Columnas, LecturaReportesContables.Ref(cuenta), request.Tipo,
            visibles, totales, anio, desde, hasta, request.SucursalId);
    }
}

// ─── Libro diario ────────────────────────────────────────────────────────────

public sealed record PartidaDiarioResponse(
    int Linea, Guid CuentaId, string CuentaCodigo, string? CuentaNombre, string? Concepto, Guid SucursalId, string? SucursalNombre,
    decimal Cargo, decimal Abono);

public sealed record PolizaDiarioResponse(
    Guid Id, string? Folio, string TipoClave, DateOnly Fecha, int PeriodoAnio, int PeriodoNumero, string Concepto, OrigenPoliza Origen,
    EstadoPoliza Estado, string? OrigenModulo, string? OrigenFolio, decimal Cargos, decimal Abonos, IReadOnlyList<PartidaDiarioResponse> Partidas);

/// <summary>Totales de todo el diario filtrado (no solo de la página).</summary>
public sealed record TotalesDiarioResponse(int Polizas, int Partidas, decimal Cargos, decimal Abonos, bool Cuadra);

public sealed record DiarioResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<PolizaDiarioResponse> Polizas, TotalesDiarioResponse Totales, int Total, int Offset, int Limit,
    int Anio, int Desde, int Hasta, Guid? SucursalId);

/// <summary>
/// Libro diario (C1.7): pólizas del rango (autorizadas y anuladas con su anulación) con sus partidas, en orden de fecha y folio,
/// paginado por póliza. Con sucursal se muestran las pólizas que tienen partidas de esa sucursal y solo esas partidas.
/// </summary>
public sealed record DiarioQuery(
    int Anio, int Desde, int Hasta, Guid? SucursalId = null, Guid? TipoOperacionId = null, OrigenPoliza? Origen = null, Guid? CuentaId = null,
    int Offset = 0, int Limit = 100) : IRequest<DiarioResponse>;

public sealed class DiarioValidator : AbstractValidator<DiarioQuery>
{
    public const int LimiteMaximo = 1000;

    public DiarioValidator()
    {
        ValidacionRango.Rango(this, q => q.Anio, q => q.Desde, q => q.Hasta);
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
        RuleFor(q => q.Origen).IsInEnum().When(q => q.Origen is not null);
        RuleFor(q => q.Offset).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Limit).InclusiveBetween(1, LimiteMaximo);
    }
}

public sealed class DiarioHandler(ContabilidadDbContext db, LecturaReportesContables lectura, IClock clock) : IRequestHandler<DiarioQuery, DiarioResponse>
{
    public async Task<DiarioResponse> Handle(DiarioQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (anio, desde, hasta) = (request.Anio, request.Desde, request.Hasta);
        // Variables separadas: una consulta no puede capturar en su cierre la variable que después se reasigna (recursión infinita).
        var delRango = db.Polizas.AsNoTracking()
            .Where(p => p.Estado != EstadoPoliza.Borrador && p.PeriodoAnio == anio && p.PeriodoNumero >= desde && p.PeriodoNumero <= hasta);
        if (request.TipoOperacionId is { } tipo) delRango = delRango.Where(p => p.TipoOperacionId == tipo);
        if (request.Origen is { } origen) delRango = delRango.Where(p => p.Origen == origen);
        var partidas = db.PolizasPartidas.AsNoTracking().Where(x => delRango.Any(p => p.Id == x.PolizaId));
        if (request.SucursalId is { } suc) partidas = partidas.Where(x => x.SucursalId == suc);
        if (request.CuentaId is { } cta) partidas = partidas.Where(x => x.CuentaId == cta);
        var polizas = request.SucursalId is not null || request.CuentaId is not null
            ? delRango.Where(p => partidas.Any(x => x.PolizaId == p.Id))
            : delRango;

        var total = await polizas.CountAsync(ct);
        var sumas = await partidas.GroupBy(_ => 1)
            .Select(g => new { Partidas = g.Count(), Cargos = g.Sum(x => x.Cargo), Abonos = g.Sum(x => x.Abono) }).FirstOrDefaultAsync(ct);

        var pagina = await polizas.OrderBy(p => p.Fecha).ThenBy(p => p.TipoClave).ThenBy(p => p.Numero).ThenBy(p => p.Id)
            .Skip(request.Offset).Take(request.Limit).ToListAsync(ct);
        var ids = pagina.Select(p => p.Id).ToList();
        var lineas = (await partidas.Where(x => ids.Contains(x.PolizaId)).ToListAsync(ct)).ToLookup(x => x.PolizaId);
        var cuentaIds = lineas.SelectMany(g => g).Select(x => x.CuentaId).Distinct().ToList();
        var nombres = await db.Cuentas.AsNoTracking().Where(c => cuentaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var sucursales = await lectura.SucursalesAsync(ct);

        var items = pagina.Select(p =>
        {
            var ps = lineas[p.Id].OrderBy(x => x.Linea)
                .Select(x => new PartidaDiarioResponse(x.Linea, x.CuentaId, x.CuentaCodigo, nombres.GetValueOrDefault(x.CuentaId), x.Concepto,
                    x.SucursalId, sucursales.GetValueOrDefault(x.SucursalId)?.Nombre, x.Cargo, x.Abono)).ToList();
            return new PolizaDiarioResponse(p.Id, LecturaReportesContables.Folio(p.TipoClave, p.Numero), p.TipoClave, p.Fecha, p.PeriodoAnio,
                p.PeriodoNumero, p.Concepto, p.Origen, p.Estado, p.OrigenModulo, p.OrigenFolio, ps.Sum(x => x.Cargo), ps.Sum(x => x.Abono), ps);
        }).ToList();

        var cargos = sumas?.Cargos ?? 0m;
        var abonos = sumas?.Abonos ?? 0m;
        var filtros = new Dictionary<string, string?>
        {
            ["Periodo"] = LecturaReportesContables.Periodo(anio, desde, hasta),
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas",
            ["Origen"] = request.Origen switch { OrigenPoliza.Manual => "Manual", OrigenPoliza.Automatica => "Automática", OrigenPoliza.Apertura => "Apertura", _ => "Todos" },
        };
        return new DiarioResponse("Libro diario", clock.UtcNow, filtros, items,
            new TotalesDiarioResponse(total, sumas?.Partidas ?? 0, cargos, abonos, cargos == abonos), total, request.Offset, request.Limit,
            anio, desde, hasta, request.SucursalId);
    }
}
