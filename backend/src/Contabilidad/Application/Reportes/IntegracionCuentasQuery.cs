using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Reportes;

// F1-CON-08 · K10.10 (R24): integraciones del auditor desde la contabilidad. Desglosan por cuenta afectable y tercero (cliente o
// proveedor) lo que el estado de resultados muestra por rubro (ingresos, costo, gastos, financiamiento), y las compras por proveedor
// desde las cuentas colectivas de proveedores. Mismas pólizas que la balanza (autorizadas y anuladas con su anulación; sin borradores).
//
// Supuestos «a validar» con Contabilidad:
//  - El tercero de una partida de resultados sin cliente ni proveedor es el de su póliza (la partida del cliente o proveedor de la
//    misma póliza); si la póliza no tiene, la fila sale «Sin tercero».
//  - Compras = abonos a cuentas colectivas de proveedores (facturas registradas); los cargos (pagos, notas de crédito, anticipos
//    aplicados) se muestran aparte y el neto es el movimiento del periodo.

/// <summary>Integración a generar. Las cuatro primeras siguen las secciones del estado de resultados.</summary>
/// <remarks>H1 (QA CON-08): viaja como texto en el JSON; como número tronaba la pantalla (<c>tipo.toLowerCase</c>).</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegracionContable { Ingresos, Costo, Gastos, Financiamiento, Compras }

/// <summary>
/// Fila: una cuenta afectable con un tercero. <c>Importe</c> con el signo de la sección (ingresos acreedores en positivo; costo,
/// gastos y financiamiento deudores en positivo; compras = abonos − cargos a proveedores).
/// </summary>
public sealed record FilaIntegracion(
    Guid CuentaId, string CuentaCodigo, string CuentaNombre, string MayorCodigo, string? TerceroTipo, Guid? TerceroId,
    string? TerceroClave, string TerceroNombre, decimal Cargos, decimal Abonos, decimal Importe, int Polizas);

public sealed record TotalesIntegracion(decimal Cargos, decimal Abonos, decimal Importe, decimal SinTercero, int Cuentas, int Terceros);

public sealed record IntegracionResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaEstadoFinanciero> Columnas, IReadOnlyList<FilaIntegracion> Filas, TotalesIntegracion Totales,
    IReadOnlyList<string> Avisos, IntegracionContable Tipo, int Anio, int Desde, int Hasta, Guid? SucursalId);

public sealed record IntegracionQuery(IntegracionContable Tipo, int Anio, int Desde, int Hasta, Guid? SucursalId = null)
    : IRequest<IntegracionResponse>;

public sealed class IntegracionValidator : AbstractValidator<IntegracionQuery>
{
    public IntegracionValidator()
    {
        RuleFor(q => q.Tipo).IsInEnum().WithMessage("La integración no es válida.");
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("anio")
            .WithMessage($"El año debe estar entre {EjercicioContable.AnioMinimo} y {EjercicioContable.AnioMaximo}.");
        RuleFor(q => q.Desde).InclusiveBetween(1, PeriodoContable.NumeroAjuste).OverridePropertyName("desde")
            .WithMessage($"El periodo inicial debe estar entre 1 y {PeriodoContable.NumeroAjuste}.");
        RuleFor(q => q.Hasta).InclusiveBetween(1, PeriodoContable.NumeroAjuste).OverridePropertyName("hasta")
            .WithMessage($"El periodo final debe estar entre 1 y {PeriodoContable.NumeroAjuste}.");
        RuleFor(q => q).Must(q => q.Desde <= q.Hasta).OverridePropertyName("hasta")
            .WithMessage("El periodo final no puede ser anterior al inicial.");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null).OverridePropertyName("sucursalId")
            .WithMessage("La sucursal no es válida; elígela de la lista.");
    }
}

public sealed class IntegracionHandler(LecturaReportesContables lectura, ITerceroContabilidadPort terceros, IClock clock)
    : IRequestHandler<IntegracionQuery, IntegracionResponse>
{
    private static readonly ColumnaEstadoFinanciero[] Columnas =
    [
        new("mayorCodigo", "Mayor", "texto"),
        new("cuentaCodigo", "Cuenta", "texto"),
        new("cuentaNombre", "Nombre de la cuenta", "texto"),
        new("terceroTipo", "Tipo", "texto"),
        new("terceroClave", "Clave", "texto"),
        new("terceroNombre", "Cliente o proveedor", "texto"),
        new("polizas", "Pólizas", "numero", "derecha", 0),
        new("cargos", "Cargos", "moneda", "derecha"),
        new("abonos", "Abonos", "moneda", "derecha"),
        new("importe", "Importe", "moneda", "derecha"),
    ];

    /// <summary>Movimiento agregado por cuenta y tercero (propio de la partida, o de la póliza si la partida no lo trae).</summary>
    internal sealed record Agregado(Guid CuentaId, Guid? ClienteId, Guid? ProveedorId, decimal Cargos, decimal Abonos, int Polizas);

    public async Task<IntegracionResponse> Handle(IntegracionQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cuentas = await lectura.CuentasAsync(ct);
        var incluidas = Cuentas(request.Tipo, cuentas);
        var ids = incluidas.Keys.ToList();
        var rango = LecturaReportesContables.Entre(lectura.Movimientos(request.SucursalId), request.Anio, request.Desde, request.Hasta);
        var movs = rango.Where(m => ids.Contains(m.CuentaId));

        // Con tercero propio: agregado en SQL por cuenta y tercero.
        var propios = await movs.Where(m => m.ClienteId != null || m.ProveedorId != null)
            .GroupBy(m => new { m.CuentaId, m.ClienteId, m.ProveedorId })
            .Select(g => new Agregado(g.Key.CuentaId, g.Key.ClienteId, g.Key.ProveedorId, g.Sum(m => m.Cargo), g.Sum(m => m.Abono),
                g.Select(m => m.PolizaId).Distinct().Count()))
            .ToListAsync(ct);

        // Sin tercero: por cuenta y póliza; el tercero sale de la propia póliza (supuesto a validar).
        var sinTercero = await movs.Where(m => m.ClienteId == null && m.ProveedorId == null)
            .GroupBy(m => new { m.CuentaId, m.PolizaId })
            .Select(g => new { g.Key.CuentaId, g.Key.PolizaId, Cargos = g.Sum(m => m.Cargo), Abonos = g.Sum(m => m.Abono) })
            .ToListAsync(ct);
        var polizasSin = sinTercero.Select(x => x.PolizaId).Distinct().ToList();
        var porPoliza = new Dictionary<Guid, (Guid? ClienteId, Guid? ProveedorId)>();
        foreach (var lote in polizasSin.Chunk(5_000))
        {
            var tercerosPoliza = await rango.Where(m => lote.Contains(m.PolizaId) && (m.ClienteId != null || m.ProveedorId != null))
                .Select(m => new { m.PolizaId, m.Linea, m.ClienteId, m.ProveedorId }).ToListAsync(ct);
            foreach (var g in tercerosPoliza.GroupBy(x => x.PolizaId))
                porPoliza[g.Key] = g.OrderBy(x => x.Linea).Select(x => (x.ClienteId, x.ProveedorId)).First();
        }

        var agregados = Combinar(propios, sinTercero.Select(x =>
        {
            var (cli, prov) = porPoliza.GetValueOrDefault(x.PolizaId);
            return (x.CuentaId, x.PolizaId, cli, prov, x.Cargos, x.Abonos);
        }));

        var idsCli = agregados.Where(a => a.ClienteId is not null).Select(a => a.ClienteId!.Value).Distinct().ToList();
        var idsProv = agregados.Where(a => a.ProveedorId is not null).Select(a => a.ProveedorId!.Value).Distinct().ToList();
        var clientes = idsCli.Count > 0 ? await terceros.ObtenerAsync(TipoAuxiliar.Cliente, idsCli, ct) : new Dictionary<Guid, AuxiliarContable>();
        var proveedores = idsProv.Count > 0 ? await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, idsProv, ct) : new Dictionary<Guid, AuxiliarContable>();

        var filas = Armar(request.Tipo, agregados, incluidas, clientes, proveedores);
        var totales = Totales(filas);
        var sucursales = await lectura.SucursalesAsync(ct);
        var filtros = new Dictionary<string, string?>
        {
            ["Integración"] = Nombre(request.Tipo),
            ["Periodo"] = LecturaReportesContables.Periodo(request.Anio, request.Desde, request.Hasta),
            ["Sucursal"] = request.SucursalId is { } s ? sucursales.GetValueOrDefault(s)?.Nombre ?? s.ToString() : "Todas (consolidado)",
        };
        var avisos = new List<string>();
        if (incluidas.Count == 0)
            avisos.Add(request.Tipo == IntegracionContable.Compras
                ? "No hay cuentas colectivas de proveedores en el catálogo: márcalas como cuenta de control «Proveedores»."
                : $"Ninguna cuenta del catálogo está en un rubro de {Nombre(request.Tipo).ToLowerInvariant()}: revisa los rubros del catálogo.");
        if (totales.SinTercero != 0m)
            avisos.Add($"{totales.SinTercero.ToString("N2", CultureInfo.GetCultureInfo("es-MX"))} no tienen cliente ni proveedor en la partida " +
                "ni en su póliza: salen como «Sin tercero».");
        return new IntegracionResponse($"Integración de {Nombre(request.Tipo).ToLowerInvariant()}", clock.UtcNow, filtros, Columnas, filas,
            totales, avisos, request.Tipo, request.Anio, request.Desde, request.Hasta, request.SucursalId);
    }

    internal static string Nombre(IntegracionContable t) => t switch
    {
        IntegracionContable.Ingresos => "Ingresos",
        IntegracionContable.Costo => "Costo",
        IntegracionContable.Gastos => "Gastos de operación",
        IntegracionContable.Financiamiento => "Costo de financiamiento",
        _ => "Compras",
    };

    /// <summary>
    /// Cuentas que entran a la integración con su cuenta de mayor (nivel 1): las del rubro de la sección del estado de resultados, o
    /// las colectivas de proveedores para compras.
    /// </summary>
    internal static Dictionary<Guid, (CuentaContable Cuenta, CuentaContable Mayor)> Cuentas(
        IntegracionContable tipo, IReadOnlyList<CuentaContable> cuentas)
    {
        var porId = cuentas.ToDictionary(c => c.Id);
        var raices = EstadoFinancieroHandler.Raices(cuentas);
        var res = new Dictionary<Guid, (CuentaContable, CuentaContable)>();
        foreach (var c in cuentas.Where(c => !c.EsRubro))
        {
            var raiz = raices[c.Id];
            var entra = tipo == IntegracionContable.Compras
                ? c.CuentaControl == CuentaControl.Proveedores
                : EstadoFinancieroHandler.Ubicar(raiz, porId).Seccion == Seccion(tipo);
            if (entra) res[c.Id] = (c, raiz);
        }
        return res;
    }

    private static SeccionEstadoFinanciero? Seccion(IntegracionContable t) => t switch
    {
        IntegracionContable.Ingresos => SeccionEstadoFinanciero.Ingresos,
        IntegracionContable.Costo => SeccionEstadoFinanciero.Costo,
        IntegracionContable.Gastos => SeccionEstadoFinanciero.Gastos,
        IntegracionContable.Financiamiento => SeccionEstadoFinanciero.Financiamiento,
        _ => null,
    };

    /// <summary>Junta lo agregado con tercero propio y lo de partidas sin tercero (con el tercero de su póliza) por cuenta y tercero.</summary>
    internal static List<Agregado> Combinar(
        IEnumerable<Agregado> propios,
        IEnumerable<(Guid CuentaId, Guid PolizaId, Guid? ClienteId, Guid? ProveedorId, decimal Cargos, decimal Abonos)> sinTercero)
    {
        var res = propios.ToDictionary(a => (a.CuentaId, a.ClienteId, a.ProveedorId));
        foreach (var g in sinTercero.GroupBy(x => (x.CuentaId, x.ClienteId, x.ProveedorId)))
        {
            var cargos = g.Sum(x => x.Cargos);
            var abonos = g.Sum(x => x.Abonos);
            var polizas = g.Select(x => x.PolizaId).Distinct().Count();
            res[g.Key] = res.TryGetValue(g.Key, out var a)
                ? a with { Cargos = a.Cargos + cargos, Abonos = a.Abonos + abonos, Polizas = a.Polizas + polizas }
                : new Agregado(g.Key.CuentaId, g.Key.ClienteId, g.Key.ProveedorId, cargos, abonos, polizas);
        }
        return [.. res.Values];
    }

    internal static List<FilaIntegracion> Armar(
        IntegracionContable tipo, IReadOnlyList<Agregado> agregados,
        IReadOnlyDictionary<Guid, (CuentaContable Cuenta, CuentaContable Mayor)> cuentas,
        IReadOnlyDictionary<Guid, AuxiliarContable> clientes, IReadOnlyDictionary<Guid, AuxiliarContable> proveedores)
    {
        // Ingresos y compras se leen como acreedores (abono − cargo); costo, gastos y financiamiento como deudores.
        var acreedora = tipo is IntegracionContable.Ingresos or IntegracionContable.Compras;
        return [.. agregados
            .Where(a => a.Cargos != 0m || a.Abonos != 0m)
            .Select(a =>
            {
                var (cuenta, mayor) = cuentas[a.CuentaId];
                AuxiliarContable? aux = null;
                string? tipoTercero = null;
                if (a.ProveedorId is { } p) { aux = proveedores.GetValueOrDefault(p); tipoTercero = "Proveedor"; }
                else if (a.ClienteId is { } c) { aux = clientes.GetValueOrDefault(c); tipoTercero = "Cliente"; }
                var terceroId = a.ProveedorId ?? a.ClienteId;
                var nombre = terceroId is null ? "Sin tercero" : aux?.Nombre ?? terceroId.Value.ToString();
                var importe = acreedora ? a.Abonos - a.Cargos : a.Cargos - a.Abonos;
                return new FilaIntegracion(cuenta.Id, cuenta.Codigo, cuenta.Nombre, mayor.Codigo, tipoTercero, terceroId, aux?.Clave, nombre,
                    a.Cargos, a.Abonos, importe, a.Polizas);
            })
            .OrderBy(f => f.CuentaCodigo, StringComparer.Ordinal)
            .ThenBy(f => f.TerceroId is null)
            .ThenBy(f => f.TerceroNombre, StringComparer.CurrentCulture)];
    }

    internal static TotalesIntegracion Totales(IReadOnlyList<FilaIntegracion> filas) =>
        new(filas.Sum(f => f.Cargos), filas.Sum(f => f.Abonos), filas.Sum(f => f.Importe),
            filas.Where(f => f.TerceroId is null).Sum(f => f.Importe), filas.Select(f => f.CuentaId).Distinct().Count(),
            filas.Where(f => f.TerceroId is not null).Select(f => f.TerceroId).Distinct().Count());
}

/// <summary>Lee los filtros de la integración como texto para responder 400 en español. Sin periodos: del 1 al mes de hoy.</summary>
public static class FiltrosIntegracion
{
    public static IntegracionQuery Leer(string? tipo, string? anio, string? desde, string? hasta, string? sucursalId, DateOnly? hoy = null)
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
        var t = IntegracionContable.Ingresos;
        if (string.IsNullOrWhiteSpace(tipo) || int.TryParse(tipo, out _) || !Enum.TryParse(tipo, true, out t))
            errores.Add(new("tipo", "INTEGRACION_INVALIDA",
                $"La integración «{tipo}» no es válida; usa Ingresos, Costo, Gastos, Financiamiento o Compras."));
        var a = Entero(anio, "anio", fecha.Year, "El año");
        var h = Entero(hasta, "hasta", fecha.Month, "El periodo final");
        var d = Entero(desde, "desde", 1, "El periodo inicial");
        Guid? suc = null;
        if (!string.IsNullOrWhiteSpace(sucursalId))
        {
            if (Guid.TryParse(sucursalId, out var g) && g != Guid.Empty) suc = g;
            else errores.Add(new("sucursalId", "SUCURSAL_INVALIDA", "La sucursal no es válida; elígela de la lista."));
        }
        if (errores.Count > 0) throw new Millet.SharedKernel.Application.Exceptions.ValidationException(errores);
        return new IntegracionQuery(t, a, d, h, suc);
    }
}
