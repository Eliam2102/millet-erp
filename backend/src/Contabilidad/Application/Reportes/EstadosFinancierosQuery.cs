using System.Globalization;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Reportes;

// F1-CON-08 · K10.10 (R21): balance general y estado de resultados salen del ERP, no de Excel. Se arman desde los saldos de la balanza
// (mismas pólizas: autorizadas y anuladas con su anulación; sin borradores) y la estructura de reporte del catálogo (P24): cada cuenta
// de nivel 1 pertenece a un RUBRO y el rubro lleva el grupo de reporte (balance, resultados o notas). Forma ADR-0036.
//
// Supuestos «a validar» con Contabilidad (D13, formato actual de sus estados financieros, pendiente):
//  - La sección sale del primer dígito del código del rubro: 1 activo, 2 pasivo, 3 capital, 4 ingresos, 5 costo, 6 gastos de operación,
//    7 costo de financiamiento (como el catálogo del 5-nov); otro dígito en resultados = «Otros resultados».
//  - El comparativo es el mismo periodo del año anterior.
//  - El capital incluye el resultado del ejercicio y los resultados de ejercicios anteriores aún en cuentas de resultados, para que
//    activo = pasivo + capital antes del cierre anual (CON-12). Las cuentas de orden (grupo «Notas») no entran a los estados.

/// <summary>Estado financiero a generar.</summary>
public enum EstadoFinanciero { BalanceGeneral, EstadoResultados }

/// <summary>Sección NIF del estado. El orden es el de presentación.</summary>
public enum SeccionEstadoFinanciero { Activo, Pasivo, Capital, Ingresos, Costo, Gastos, Financiamiento, OtrosResultados }

public sealed record ColumnaEstadoFinanciero(string Clave, string Etiqueta, string Tipo, string? Alineacion = null, int? Decimales = null);

/// <summary>
/// Renglón del estado. <c>Tipo</c>: <c>seccion</c> (encabezado), <c>rubro</c> (encabezado de rubro), <c>cuenta</c> (cuenta de mayor),
/// <c>calculo</c> (resultado del ejercicio en el balance), <c>subtotal</c> (total de rubro), <c>total</c> (total de sección) o
/// <c>resultado</c> (utilidad bruta, de operación, del periodo; total pasivo y capital). Importes con el signo de su sección
/// (activo, costos y gastos en positivo cuando son deudores; pasivo, capital e ingresos en positivo cuando son acreedores).
/// </summary>
public sealed record FilaEstadoFinanciero(
    string Clave, string Tipo, string Concepto, string? Cuenta, int Sangria, decimal? Actual, decimal? Comparativo, decimal? Variacion,
    decimal? Porcentaje, Guid? CuentaId);

/// <summary>Cuenta de nivel 1 con saldo que no tiene rubro o cuyo rubro no tiene un grupo de reporte reconocible (K10.10-b).</summary>
public sealed record CuentaSinClasificar(Guid CuentaId, string Codigo, string Nombre, decimal Saldo, string Motivo);

/// <summary>
/// Totales del estado. Balance: activo, pasivo, capital (incluye resultados) y <c>Cuadra</c> = activo = pasivo + capital.
/// Resultados: ingresos, costo, utilidad bruta, gastos, utilidad de operación, financiamiento, otros y utilidad del periodo.
/// </summary>
public sealed record TotalesEstadoFinanciero(
    decimal Activo, decimal Pasivo, decimal Capital, decimal PasivoMasCapital, decimal Diferencia,
    decimal Ingresos, decimal Costo, decimal UtilidadBruta, decimal Gastos, decimal UtilidadOperacion, decimal Financiamiento,
    decimal OtrosResultados, decimal UtilidadPeriodo, decimal SinClasificar, bool Cuadra);

public sealed record EstadoFinancieroResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaEstadoFinanciero> Columnas, IReadOnlyList<FilaEstadoFinanciero> Filas, TotalesEstadoFinanciero Totales,
    TotalesEstadoFinanciero? TotalesComparativo, IReadOnlyList<CuentaSinClasificar> SinClasificar, IReadOnlyList<string> Avisos,
    EstadoFinanciero Estado, int Anio, int Periodo, int Desde, bool Comparar, Guid? SucursalId);

/// <summary>
/// Balance general al cierre de <paramref name="Periodo"/> (todo el historial hasta ese periodo) o estado de resultados del ejercicio
/// (periodos 1 a <paramref name="Periodo"/> con <paramref name="Acumulado"/>, o solo ese periodo). <paramref name="Comparar"/> agrega el
/// mismo periodo del año anterior. La sucursal es un filtro opcional (Contabilidad es corporativa; una razón social, R26).
/// </summary>
public sealed record EstadoFinancieroQuery(
    EstadoFinanciero Estado, int Anio, int Periodo, bool Acumulado = true, bool Comparar = false, Guid? SucursalId = null)
    : IRequest<EstadoFinancieroResponse>
{
    public int Desde => Estado == EstadoFinanciero.EstadoResultados && !Acumulado ? Periodo : 1;
}

public sealed class EstadoFinancieroValidator : AbstractValidator<EstadoFinancieroQuery>
{
    public EstadoFinancieroValidator()
    {
        RuleFor(q => q.Estado).IsInEnum().WithMessage("El estado financiero no es válido.");
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo + 1, EjercicioContable.AnioMaximo).OverridePropertyName("anio")
            .WithMessage($"El año debe estar entre {EjercicioContable.AnioMinimo + 1} y {EjercicioContable.AnioMaximo}.");
        RuleFor(q => q.Periodo).InclusiveBetween(1, PeriodoContable.NumeroAjuste).OverridePropertyName("periodo")
            .WithMessage($"El periodo debe estar entre 1 y {PeriodoContable.NumeroAjuste}.");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null).OverridePropertyName("sucursalId")
            .WithMessage("La sucursal no es válida; elígela de la lista.");
    }
}

public sealed class EstadoFinancieroHandler(LecturaReportesContables lectura, IClock clock)
    : IRequestHandler<EstadoFinancieroQuery, EstadoFinancieroResponse>
{
    private static readonly string[] Meses =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic", "ajuste"];

    public async Task<EstadoFinancieroResponse> Handle(EstadoFinancieroQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuentas = await lectura.CuentasAsync(ct);
        var actual = await SaldosAsync(request, request.Anio, ct);
        var comparativo = request.Comparar ? await SaldosAsync(request, request.Anio - 1, ct) : null;

        var armado = Armar(request.Estado, cuentas, actual, comparativo);
        var sucursales = await lectura.SucursalesAsync(ct);
        var filtros = new Dictionary<string, string?>
        {
            ["Periodo"] = Etiqueta(request, request.Anio),
            ["Comparativo"] = request.Comparar ? Etiqueta(request, request.Anio - 1) : "Sin comparativo",
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas (consolidado)",
        };
        var columnas = new List<ColumnaEstadoFinanciero>
        {
            new("concepto", "Concepto", "texto"),
            new("cuenta", "Cuenta", "texto"),
            new("actual", Etiqueta(request, request.Anio), "moneda", "derecha"),
        };
        if (request.Comparar)
        {
            columnas.Add(new("comparativo", Etiqueta(request, request.Anio - 1), "moneda", "derecha"));
            columnas.Add(new("variacion", "Variación", "moneda", "derecha"));
        }
        columnas.Add(new("porcentaje", request.Estado == EstadoFinanciero.BalanceGeneral ? "% del activo" : "% de ingresos", "numero",
            "derecha", 1));

        var titulo = request.Estado == EstadoFinanciero.BalanceGeneral ? "Balance general" : "Estado de resultados";
        return new EstadoFinancieroResponse(titulo, clock.UtcNow, filtros, columnas, armado.Filas, armado.Totales,
            armado.TotalesComparativo, armado.SinClasificar, armado.Avisos, request.Estado, request.Anio, request.Periodo, request.Desde,
            request.Comparar, request.SucursalId);
    }

    /// <summary>Saldo deudor (cargo − abono) por cuenta: al corte (balance) o del rango (resultados), y antes del ejercicio.</summary>
    private async Task<Saldos> SaldosAsync(EstadoFinancieroQuery q, int anio, CancellationToken ct)
    {
        var movs = lectura.Movimientos(q.SucursalId);
        if (q.Estado == EstadoFinanciero.EstadoResultados)
        {
            var rango = await LecturaReportesContables.Entre(movs, anio, q.Desde, q.Periodo)
                .GroupBy(m => m.CuentaId).Select(g => new { g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) })
                .ToDictionaryAsync(x => x.Key, x => x.Saldo, ct);
            return new Saldos(rango, new Dictionary<Guid, decimal>());
        }
        var alCorte = await movs.Where(m => m.PeriodoAnio < anio || (m.PeriodoAnio == anio && m.PeriodoNumero <= q.Periodo))
            .GroupBy(m => m.CuentaId).Select(g => new { g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) })
            .ToDictionaryAsync(x => x.Key, x => x.Saldo, ct);
        var antes = await movs.Where(m => m.PeriodoAnio < anio)
            .GroupBy(m => m.CuentaId).Select(g => new { g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) })
            .ToDictionaryAsync(x => x.Key, x => x.Saldo, ct);
        return new Saldos(alCorte, antes);
    }

    private static string Etiqueta(EstadoFinancieroQuery q, int anio)
    {
        var hasta = $"{Meses[q.Periodo - 1]} {anio}";
        if (q.Estado == EstadoFinanciero.BalanceGeneral) return $"Al cierre de {hasta}";
        return q.Desde == q.Periodo ? hasta : $"ene a {hasta}";
    }

    /// <summary>Saldos deudores por cuenta: <c>Periodo</c> (al corte o del rango) y <c>AntesDelEjercicio</c> (solo balance).</summary>
    internal sealed record Saldos(IReadOnlyDictionary<Guid, decimal> Periodo, IReadOnlyDictionary<Guid, decimal> AntesDelEjercicio);

    internal sealed record Armado(
        List<FilaEstadoFinanciero> Filas, TotalesEstadoFinanciero Totales, TotalesEstadoFinanciero? TotalesComparativo,
        List<CuentaSinClasificar> SinClasificar, List<string> Avisos);

    /// <summary>Ubicación de una cuenta de nivel 1 en el estado: su sección y su rubro, o el motivo por el que no se clasifica.</summary>
    internal sealed record Ubicacion(SeccionEstadoFinanciero? Seccion, CuentaContable? Rubro, bool EsNota, string? Motivo);

    /// <summary>Importes por cuenta de nivel 1 ya con el signo de su sección, más los cálculos del balance.</summary>
    private sealed record Importes(
        Dictionary<Guid, decimal> PorRaiz, decimal ResultadoEjercicio, decimal ResultadosAnteriores, Dictionary<Guid, decimal> SinClasificar);

    internal static bool Acreedora(SeccionEstadoFinanciero s) =>
        s is SeccionEstadoFinanciero.Pasivo or SeccionEstadoFinanciero.Capital or SeccionEstadoFinanciero.Ingresos
            or SeccionEstadoFinanciero.OtrosResultados;

    internal static bool EsDeBalance(SeccionEstadoFinanciero s) => s <= SeccionEstadoFinanciero.Capital;

    /// <summary>Grupo de reporte del catálogo: balance, resultados o notas (cuentas de orden). Null si no se reconoce.</summary>
    internal static string? Grupo(string? grupoReporte)
    {
        if (string.IsNullOrWhiteSpace(grupoReporte)) return null;
        var g = SinAcentos(grupoReporte).ToLowerInvariant();
        if (g.Contains("balance") || g.Contains("posicion")) return "balance";
        if (g.Contains("resultado") || g.Contains("perdidas")) return "resultados";
        if (g.Contains("nota") || g.Contains("orden")) return "notas";
        return null;
    }

    /// <summary>Sección por el primer dígito del código del rubro (supuesto a validar; catálogo del 5-nov).</summary>
    internal static SeccionEstadoFinanciero? Seccion(string grupo, string codigoRubro)
    {
        var digito = codigoRubro.FirstOrDefault(char.IsDigit);
        return (grupo, digito) switch
        {
            ("balance", '1') => SeccionEstadoFinanciero.Activo,
            ("balance", '2') => SeccionEstadoFinanciero.Pasivo,
            ("balance", '3') => SeccionEstadoFinanciero.Capital,
            ("resultados", '4') => SeccionEstadoFinanciero.Ingresos,
            ("resultados", '5') => SeccionEstadoFinanciero.Costo,
            ("resultados", '6') => SeccionEstadoFinanciero.Gastos,
            ("resultados", '7') => SeccionEstadoFinanciero.Financiamiento,
            ("resultados", _) => SeccionEstadoFinanciero.OtrosResultados,
            _ => null,
        };
    }

    internal static Ubicacion Ubicar(CuentaContable raiz, IReadOnlyDictionary<Guid, CuentaContable> porId)
    {
        if (raiz.RubroId is not { } rubroId || !porId.TryGetValue(rubroId, out var rubro))
            return new(null, null, false, "No tiene rubro de reporte");
        var grupo = Grupo(rubro.GrupoReporte) ?? Grupo(raiz.GrupoReporte);
        if (grupo == "notas") return new(null, rubro, true, null);
        if (grupo is null) return new(null, rubro, false, $"El rubro {rubro.Codigo} no tiene grupo de reporte");
        return Seccion(grupo, rubro.Codigo) is { } s
            ? new(s, rubro, false, null)
            : new(null, rubro, false, $"El rubro {rubro.Codigo} no corresponde a activo, pasivo ni capital");
    }

    /// <summary>Cuenta de nivel 1 (raíz del árbol) de cada cuenta.</summary>
    internal static Dictionary<Guid, CuentaContable> Raices(IReadOnlyList<CuentaContable> cuentas)
    {
        var porId = cuentas.ToDictionary(c => c.Id);
        var res = new Dictionary<Guid, CuentaContable>();
        foreach (var c in cuentas)
        {
            var r = c;
            var vueltas = 0;
            while (r.PadreId is { } p && porId.TryGetValue(p, out var padre) && vueltas++ < 20) r = padre;
            res[c.Id] = r;
        }
        return res;
    }

    private static Importes Calcular(
        EstadoFinanciero estado, Dictionary<Guid, CuentaContable> raices, Dictionary<Guid, Ubicacion> ubicaciones,
        Saldos saldos)
    {
        var porRaiz = new Dictionary<Guid, decimal>();
        var sinClasificar = new Dictionary<Guid, decimal>();
        decimal resultadoEjercicio = 0m, anteriores = 0m;
        foreach (var (cuentaId, saldo) in saldos.Periodo)
        {
            if (!raices.TryGetValue(cuentaId, out var raiz) || raiz.EsRubro) continue;
            var u = ubicaciones[raiz.Id];
            if (u.EsNota) continue;
            if (u.Seccion is not { } s)
            {
                sinClasificar[raiz.Id] = sinClasificar.GetValueOrDefault(raiz.Id) + saldo;
                continue;
            }
            var deBalance = EsDeBalance(s);
            if (estado == EstadoFinanciero.BalanceGeneral && !deBalance)
            {
                // Cuentas de resultados aún sin cierre anual: su saldo es capital (acreedor = utilidad).
                var antes = saldos.AntesDelEjercicio.GetValueOrDefault(cuentaId);
                anteriores -= antes;
                resultadoEjercicio -= saldo - antes;
                continue;
            }
            if (estado == EstadoFinanciero.EstadoResultados && deBalance) continue;
            porRaiz[raiz.Id] = porRaiz.GetValueOrDefault(raiz.Id) + (Acreedora(s) ? -saldo : saldo);
        }
        return new Importes(porRaiz, resultadoEjercicio, anteriores, sinClasificar);
    }

    /// <summary>Arma el estado completo (renglones, totales, cuentas sin clasificar y avisos) a partir de los saldos por cuenta.</summary>
    internal static Armado Armar(EstadoFinanciero estado, IReadOnlyList<CuentaContable> cuentas, Saldos actual, Saldos? comparativo)
    {
        var porId = cuentas.ToDictionary(c => c.Id);
        var raices = Raices(cuentas);
        var ubicaciones = raices.Values.DistinctBy(r => r.Id).Where(r => !r.EsRubro).ToDictionary(r => r.Id, r => Ubicar(r, porId));
        var a = Calcular(estado, raices, ubicaciones, actual);
        var c = comparativo is null ? null : Calcular(estado, raices, ubicaciones, comparativo);

        var filas = new List<FilaEstadoFinanciero>();
        var secciones = estado == EstadoFinanciero.BalanceGeneral
            ? new[] { SeccionEstadoFinanciero.Activo, SeccionEstadoFinanciero.Pasivo, SeccionEstadoFinanciero.Capital }
            : [SeccionEstadoFinanciero.Ingresos, SeccionEstadoFinanciero.Costo, SeccionEstadoFinanciero.Gastos,
                SeccionEstadoFinanciero.Financiamiento, SeccionEstadoFinanciero.OtrosResultados];
        var totalSeccion = new Dictionary<SeccionEstadoFinanciero, (decimal A, decimal? C)>();

        foreach (var s in secciones)
        {
            var raicesSeccion = ubicaciones.Where(u => u.Value.Seccion == s)
                .Where(u => a.PorRaiz.ContainsKey(u.Key) || (c?.PorRaiz.ContainsKey(u.Key) ?? false))
                .Select(u => (Raiz: porId[u.Key], Rubro: u.Value.Rubro!))
                .ToList();
            var esCapital = estado == EstadoFinanciero.BalanceGeneral && s == SeccionEstadoFinanciero.Capital;
            var conCalculos = esCapital && (a.ResultadoEjercicio != 0m || a.ResultadosAnteriores != 0m
                || (c is not null && (c.ResultadoEjercicio != 0m || c.ResultadosAnteriores != 0m)));
            if (raicesSeccion.Count == 0 && !conCalculos && s == SeccionEstadoFinanciero.OtrosResultados) continue;

            filas.Add(Fila($"S:{s}", "seccion", NombreSeccion(s).ToUpperInvariant(), null, 0, null, null));
            decimal sumA = 0m;
            decimal? sumC = c is null ? null : 0m;
            foreach (var grupo in raicesSeccion.GroupBy(x => x.Rubro.Id).OrderBy(g => g.First().Rubro.Codigo, StringComparer.Ordinal))
            {
                var rubro = grupo.First().Rubro;
                filas.Add(Fila($"R:{rubro.Id}", "rubro", rubro.Nombre, null, 1, null, null));
                decimal rubA = 0m;
                decimal? rubC = c is null ? null : 0m;
                foreach (var (raiz, _) in grupo.OrderBy(x => x.Raiz.Codigo, StringComparer.Ordinal))
                {
                    var va = a.PorRaiz.GetValueOrDefault(raiz.Id);
                    decimal? vc = c?.PorRaiz.GetValueOrDefault(raiz.Id);
                    filas.Add(Fila($"C:{raiz.Id}", "cuenta", raiz.Nombre, raiz.Codigo, 2, va, vc, raiz.Id));
                    rubA += va;
                    rubC += vc;
                }
                filas.Add(Fila($"T:{rubro.Id}", "subtotal", $"Total {rubro.Nombre}", null, 1, rubA, rubC));
                sumA += rubA;
                sumC += rubC;
            }
            if (esCapital)
            {
                filas.Add(Fila("X:anteriores", "calculo", "Resultados de ejercicios anteriores (sin cierre anual)", null, 1,
                    a.ResultadosAnteriores, c?.ResultadosAnteriores));
                filas.Add(Fila("X:ejercicio", "calculo", "Resultado del ejercicio", null, 1, a.ResultadoEjercicio, c?.ResultadoEjercicio));
                sumA += a.ResultadosAnteriores + a.ResultadoEjercicio;
                sumC += c is null ? null : c.ResultadosAnteriores + c.ResultadoEjercicio;
            }
            filas.Add(Fila($"TS:{s}", "total", $"Total {NombreSeccion(s).ToLowerInvariant()}", null, 0, sumA, sumC));
            totalSeccion[s] = (sumA, sumC);

            if (estado == EstadoFinanciero.EstadoResultados)
                AgregarResultadosIntermedios(filas, s, totalSeccion);
        }

        decimal T(SeccionEstadoFinanciero s) => totalSeccion.GetValueOrDefault(s).A;
        decimal? TC(SeccionEstadoFinanciero s) => c is null ? null : totalSeccion.GetValueOrDefault(s).C ?? 0m;
        if (estado == EstadoFinanciero.BalanceGeneral)
            filas.Add(Fila("RES:pasivo-capital", "resultado", "Total pasivo y capital", null, 0,
                T(SeccionEstadoFinanciero.Pasivo) + T(SeccionEstadoFinanciero.Capital),
                TC(SeccionEstadoFinanciero.Pasivo) + TC(SeccionEstadoFinanciero.Capital)));
        else if (!filas.Any(f => f.Clave == "RES:periodo"))
            AgregarResultadosIntermedios(filas, SeccionEstadoFinanciero.OtrosResultados, totalSeccion);

        // K10.10-b: las cuentas sin clasificar no se omiten; salen en su propia sección y en avisos.
        var sinClasificar = new List<CuentaSinClasificar>();
        var idsSin = a.SinClasificar.Keys.Union(c?.SinClasificar.Keys ?? Enumerable.Empty<Guid>()).Select(id => porId[id])
            .OrderBy(r => r.Codigo, StringComparer.Ordinal).ToList();
        if (idsSin.Count > 0)
        {
            filas.Add(Fila("S:sin", "seccion", "SIN CLASIFICAR (revisar rubro en el catálogo)", null, 0, null, null));
            foreach (var r in idsSin)
            {
                var va = a.SinClasificar.GetValueOrDefault(r.Id);
                decimal? vc = c?.SinClasificar.GetValueOrDefault(r.Id);
                filas.Add(Fila($"C:{r.Id}", "cuenta", r.Nombre, r.Codigo, 2, va, vc, r.Id));
                if (va != 0m) sinClasificar.Add(new(r.Id, r.Codigo, r.Nombre, va, ubicaciones[r.Id].Motivo ?? "Sin rubro"));
            }
            filas.Add(Fila("TS:sin", "total", "Total sin clasificar (saldo deudor)", null, 0, a.SinClasificar.Values.Sum(),
                c?.SinClasificar.Values.Sum()));
        }

        var totales = Totales(totalSeccion, s => s.A, a.SinClasificar.Values.Sum());
        var totalesC = c is null ? null : Totales(totalSeccion, s => s.C ?? 0m, c.SinClasificar.Values.Sum());

        // Porcentaje: sobre el total del activo (balance) o de los ingresos (resultados) del periodo actual.
        var baseP = estado == EstadoFinanciero.BalanceGeneral ? totales.Activo : totales.Ingresos;
        filas = [.. filas.Select(f => f with
        {
            Porcentaje = f.Actual is { } v && baseP != 0m ? Math.Round(v * 100m / baseP, 1, MidpointRounding.AwayFromZero) : null,
        })];

        var avisos = new List<string>();
        foreach (var s in sinClasificar)
            avisos.Add($"La cuenta {s.Codigo} {s.Nombre} tiene saldo y no se puede ubicar en el estado ({s.Motivo.ToLowerInvariant()}): " +
                "sale en «Sin clasificar». Asígnale un rubro en el catálogo de cuentas.");
        if (estado == EstadoFinanciero.BalanceGeneral && !totales.Cuadra)
            avisos.Add($"El activo no es igual al pasivo más el capital: la diferencia es {totales.Diferencia.ToString("N2", CultureInfo.GetCultureInfo("es-MX"))}." +
                (sinClasificar.Count > 0 ? " Revisa las cuentas sin clasificar." : ""));
        return new Armado(filas, totales, totalesC, sinClasificar, avisos);
    }

    private static void AgregarResultadosIntermedios(
        List<FilaEstadoFinanciero> filas, SeccionEstadoFinanciero s, Dictionary<SeccionEstadoFinanciero, (decimal A, decimal? C)> t)
    {
        var comparar = t.Values.Any(v => v.C is not null);
        decimal A(params SeccionEstadoFinanciero[] ss) => ss.Sum(x => t.GetValueOrDefault(x).A);
        decimal? C(params SeccionEstadoFinanciero[] ss) => comparar ? ss.Sum(x => t.GetValueOrDefault(x).C ?? 0m) : null;
        const SeccionEstadoFinanciero I = SeccionEstadoFinanciero.Ingresos, K = SeccionEstadoFinanciero.Costo,
            G = SeccionEstadoFinanciero.Gastos, F = SeccionEstadoFinanciero.Financiamiento, O = SeccionEstadoFinanciero.OtrosResultados;
        switch (s)
        {
            case K:
                filas.Add(Fila("RES:bruta", "resultado", "Utilidad (pérdida) bruta", null, 0, A(I) - A(K), C(I) - C(K)));
                break;
            case G:
                filas.Add(Fila("RES:operacion", "resultado", "Utilidad (pérdida) de operación", null, 0, A(I) - A(K, G), C(I) - C(K, G)));
                break;
            case O:
                filas.Add(Fila("RES:periodo", "resultado", "Utilidad (pérdida) del periodo", null, 0, A(I, O) - A(K, G, F),
                    C(I, O) - C(K, G, F)));
                break;
        }
    }

    private static TotalesEstadoFinanciero Totales(
        Dictionary<SeccionEstadoFinanciero, (decimal A, decimal? C)> t, Func<(decimal A, decimal? C), decimal> v, decimal sinClasificar)
    {
        decimal S(SeccionEstadoFinanciero s) => t.TryGetValue(s, out var x) ? v(x) : 0m;
        var activo = S(SeccionEstadoFinanciero.Activo);
        var pasivo = S(SeccionEstadoFinanciero.Pasivo);
        var capital = S(SeccionEstadoFinanciero.Capital);
        var ingresos = S(SeccionEstadoFinanciero.Ingresos);
        var costo = S(SeccionEstadoFinanciero.Costo);
        var gastos = S(SeccionEstadoFinanciero.Gastos);
        var fin = S(SeccionEstadoFinanciero.Financiamiento);
        var otros = S(SeccionEstadoFinanciero.OtrosResultados);
        var diferencia = activo - (pasivo + capital);
        return new TotalesEstadoFinanciero(activo, pasivo, capital, pasivo + capital, diferencia, ingresos, costo, ingresos - costo, gastos,
            ingresos - costo - gastos, fin, otros, ingresos + otros - costo - gastos - fin, sinClasificar, diferencia == 0m);
    }

    private static FilaEstadoFinanciero Fila(
        string clave, string tipo, string concepto, string? cuenta, int sangria, decimal? actual, decimal? comparativo, Guid? cuentaId = null) =>
        new(clave, tipo, concepto, cuenta, sangria, actual, comparativo,
            actual is { } x && comparativo is { } y ? x - y : null, null, cuentaId);

    internal static string NombreSeccion(SeccionEstadoFinanciero s) => s switch
    {
        SeccionEstadoFinanciero.Activo => "Activo",
        SeccionEstadoFinanciero.Pasivo => "Pasivo",
        SeccionEstadoFinanciero.Capital => "Capital contable",
        SeccionEstadoFinanciero.Ingresos => "Ingresos",
        SeccionEstadoFinanciero.Costo => "Costo",
        SeccionEstadoFinanciero.Gastos => "Gastos de operación",
        SeccionEstadoFinanciero.Financiamiento => "Costo de financiamiento",
        _ => "Otros resultados",
    };

    private static string SinAcentos(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }
}

/// <summary>
/// Lee los filtros del query string como texto para responder 400 <c>VALIDATION_ERROR</c> en español (y no «Failed to bind» del
/// enlazado de ASP.NET). Sin año o periodo se toma el mes de hoy; sin <c>acumulado</c>, acumulado; sin <c>comparar</c>, sin comparativo.
/// </summary>
public static class FiltrosEstadoFinanciero
{
    public static EstadoFinancieroQuery Leer(
        EstadoFinanciero estado, string? anio, string? periodo, string? acumulado, string? comparar, string? sucursalId, DateOnly? hoy = null)
    {
        var errores = new List<Millet.SharedKernel.Application.Exceptions.ValidationError>();
        var fecha = hoy ?? DateOnly.FromDateTime(DateTime.Today);
        int Entero(string? v, string campo, int omision, string etiqueta)
        {
            if (string.IsNullOrWhiteSpace(v)) return omision;
            if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
            errores.Add(new(campo, "VALOR_INVALIDO", $"{etiqueta} «{v}» no es válido; escribe un número."));
            return omision;
        }
        bool Bandera(string? v, string campo, bool omision)
        {
            if (string.IsNullOrWhiteSpace(v)) return omision;
            if (bool.TryParse(v, out var b)) return b;
            errores.Add(new(campo, "VALOR_INVALIDO", $"El valor «{v}» de {campo} no es válido; usa true o false."));
            return omision;
        }
        var a = Entero(anio, "anio", fecha.Year, "El año");
        var p = Entero(periodo, "periodo", fecha.Month, "El periodo");
        var acu = Bandera(acumulado, "acumulado", true);
        var comp = Bandera(comparar, "comparar", false);
        Guid? suc = null;
        if (!string.IsNullOrWhiteSpace(sucursalId))
        {
            if (Guid.TryParse(sucursalId, out var g) && g != Guid.Empty) suc = g;
            else errores.Add(new("sucursalId", "SUCURSAL_INVALIDA", "La sucursal no es válida; elígela de la lista."));
        }
        if (errores.Count > 0) throw new Millet.SharedKernel.Application.Exceptions.ValidationException(errores);
        return new EstadoFinancieroQuery(estado, a, p, acu, comp, suc);
    }
}
