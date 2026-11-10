using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Cierre;

/// <summary>Saldo de una cuenta de resultados en el ejercicio, por sucursal y dimensiones (positivo = deudor).</summary>
public sealed record SaldoResultadoEjercicio(Guid CuentaId, string CuentaCodigo, Guid SucursalId, Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id, decimal Saldo);

public sealed record CierreAnualResponse(int Anio, decimal Ingresos, decimal CostosYGastos, decimal Resultado, string Tipo,
    int CuentasConSaldo, IReadOnlyList<PolizaResponse> Polizas);

/// <summary>
/// CON-12 · cierre anual (R20, Cuestionario 08 C-13; CA10.13): las cuentas de resultados del ejercicio se cancelan contra «resultado del
/// ejercicio». Supuestos a validar con Contabilidad:
/// <list type="bullet">
///   <item>Cuenta de resultados = grupo de reporte «Estado de Pérdidas y Ganancias» del catálogo; sin grupo, los rubros 400 a 799.</item>
///   <item>Contrapartida por rol: UTILIDAD_EJERCICIO (305.01) si el ejercicio dejó utilidad, PERDIDA_EJERCICIO (305.02) si dejó pérdida;
///   una contrapartida por sucursal, para que la balanza de cada sucursal también quede con resultados en cero.</item>
///   <item>Va en el periodo 13 (31-dic) como póliza manual en borrador que el Contador General revisa y autoriza (el 13 solo admite
///   pólizas manuales autorizadas, CA10.12). Si después hay ajustes en el 13, se genera otra por la diferencia.</item>
/// </list>
/// </summary>
public sealed class CalculadoraCierreAnual(ContabilidadDbContext db)
{
    public const string Modulo = "contabilidad", TipoDocumento = "cierre_anual";
    public const string RolUtilidad = "UTILIDAD_EJERCICIO", RolPerdida = "PERDIDA_EJERCICIO";
    public const string GrupoResultados = "Estado de Pérdidas y Ganancias (Resultado)";
    public const string ClaveTipoPoliza = "AS";
    /// <summary>La póliza admite hasta 500 partidas: 498 de resultados + hasta 2 contrapartidas de sucursal por póliza.</summary>
    public const int PartidasPorPoliza = 400;

    public static string Folio(int anio) => $"CIERRE-{anio:D4}";

    public static bool EsResultados(string? grupoReporte, string codigo)
    {
        if (!string.IsNullOrWhiteSpace(grupoReporte))
            return grupoReporte.Contains("Resultado", StringComparison.OrdinalIgnoreCase) || grupoReporte.Contains("Pérdidas y Ganancias", StringComparison.OrdinalIgnoreCase);
        var raiz = new string(codigo.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(raiz, out var n) && n is >= 400 and <= 799;
    }

    /// <summary>
    /// Partidas de una póliza de cierre: cada saldo de resultados al lado contrario (queda en cero) y una contrapartida por sucursal a
    /// resultado del ejercicio por el neto de esa sucursal (puede ir al lado contrario del total si esa sucursal tuvo el resultado opuesto).
    /// </summary>
    public static List<PartidaRequest> Partidas(IReadOnlyCollection<SaldoResultadoEjercicio> saldos, int anio, bool perdida, Func<Guid, Guid> cuentaResultado)
    {
        var partidas = saldos.Select(s => new PartidaRequest(s.CuentaId, $"Cancelación de resultados {anio}",
            s.Saldo < 0m ? -s.Saldo : 0m, s.Saldo > 0m ? s.Saldo : 0m, s.SucursalId, s.Dim1Id, s.Dim2Id, s.Dim3Id)).ToList();
        foreach (var g in saldos.GroupBy(s => s.SucursalId))
        {
            var neto = g.Sum(s => s.Saldo);
            if (neto == 0m) continue;
            partidas.Add(new PartidaRequest(cuentaResultado(g.Key), perdida ? "Pérdida del ejercicio" : "Utilidad del ejercicio",
                neto > 0m ? neto : 0m, neto < 0m ? -neto : 0m, g.Key));
        }
        return partidas;
    }

    /// <summary>Saldos de resultados del ejercicio (periodos 1 a 13, sin borradores), incluidas las pólizas de cierre ya registradas.</summary>
    public async Task<IReadOnlyList<SaldoResultadoEjercicio>> SaldosAsync(int anio, CancellationToken ct)
    {
        var cuentas = (await db.Cuentas.AsNoTracking().Select(c => new { c.Id, c.Codigo, c.GrupoReporte }).ToListAsync(ct))
            .Where(c => EsResultados(c.GrupoReporte, c.Codigo)).ToDictionary(c => c.Id, c => c.Codigo);
        if (cuentas.Count == 0) return [];
        var ids = cuentas.Keys.ToList();
        var filas = await (from p in db.Polizas.AsNoTracking()
                           join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
                           where p.PeriodoAnio == anio && p.Estado != EstadoPoliza.Borrador && ids.Contains(d.CuentaId)
                           group d by new { d.CuentaId, d.SucursalId, d.Dim1Id, d.Dim2Id, d.Dim3Id } into g
                           select new { g.Key, Saldo = g.Sum(x => x.Cargo - x.Abono) }).ToListAsync(ct);
        return [.. filas.Where(f => f.Saldo != 0m)
            .Select(f => new SaldoResultadoEjercicio(f.Key.CuentaId, cuentas[f.Key.CuentaId], f.Key.SucursalId, f.Key.Dim1Id, f.Key.Dim2Id, f.Key.Dim3Id, f.Saldo))
            .OrderBy(f => f.CuentaCodigo).ThenBy(f => f.SucursalId)];
    }
}

public sealed record EjecutarCierreAnualCommand(int Anio) : IRequest<CierreAnualResponse>;

public sealed class EjecutarCierreAnualValidator : AbstractValidator<EjecutarCierreAnualCommand>
{
    public EjecutarCierreAnualValidator() =>
        RuleFor(c => c.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).WithMessage("Año inválido.");
}

/// <summary>
/// Genera las pólizas de cierre anual en borrador (periodo 13). Exige diciembre cerrado y el 13 abierto, y que no haya otra póliza de
/// cierre del ejercicio en borrador (se autoriza o se descarta antes de generar otra).
/// </summary>
public sealed class EjecutarCierreAnualHandler(ContabilidadDbContext db, CalculadoraCierreAnual calculadora, IMediator mediator)
    : IRequestHandler<EjecutarCierreAnualCommand, CierreAnualResponse>
{
    public async Task<CierreAnualResponse> Handle(EjecutarCierreAnualCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var periodos = await db.Periodos.AsNoTracking().Where(p => p.Anio == c.Anio && p.Numero >= 12).ToDictionaryAsync(p => p.Numero, ct);
        if (!periodos.TryGetValue(12, out var dic) || dic.Estado != EstadoPeriodo.Cerrado)
            throw new BusinessRuleException("CONTAB_CIERRE_ANUAL_DICIEMBRE_ABIERTO", $"El cierre anual de {c.Anio} se genera con diciembre cerrado.");
        if (!periodos.TryGetValue(PeriodoContable.NumeroAjuste, out var trece) || trece.Estado != EstadoPeriodo.Abierto)
            throw new BusinessRuleException("CONTAB_CIERRE_ANUAL_13_NO_ABIERTO",
                $"El cierre anual va en el periodo 13 de {c.Anio}: ábralo (o reábralo) primero.");
        var folio = CalculadoraCierreAnual.Folio(c.Anio);
        if (await db.Polizas.AsNoTracking().AnyAsync(p => p.OrigenModulo == CalculadoraCierreAnual.Modulo && p.OrigenTipo == CalculadoraCierreAnual.TipoDocumento
                && p.OrigenFolio == folio && p.Estado == EstadoPoliza.Borrador, ct))
            throw new ConflictException("CONTAB_CIERRE_ANUAL_BORRADOR",
                $"Ya hay una póliza de cierre {folio} en borrador: autorícela o descártela antes de generar otra.");

        var saldos = await calculadora.SaldosAsync(c.Anio, ct);
        if (saldos.Count == 0)
            throw new BusinessRuleException("CONTAB_CIERRE_ANUAL_SIN_SALDOS", $"Las cuentas de resultados de {c.Anio} ya están en cero: no hay nada que cancelar.");

        var fecha = new DateOnly(c.Anio, 12, 31);
        var roles = await db.RolesCuenta.AsNoTracking().Where(x => x.DeletedAt == null && (x.Codigo == CalculadoraCierreAnual.RolUtilidad
            || x.Codigo == CalculadoraCierreAnual.RolPerdida)).ToListAsync(ct);
        Guid Cuenta(string rol, Guid sucursal) => roles.Where(x => x.Codigo == rol && (x.SucursalId is null || x.SucursalId == sucursal)
                && x.Moneda is null && x.VigenteEn(fecha) && x.CuentaId is not null)
            .OrderByDescending(x => x.SucursalId is null ? 0 : 1).ThenByDescending(x => x.VigenteDesde).Select(x => x.CuentaId!.Value).FirstOrDefault() is { } id && id != Guid.Empty
            ? id
            : throw new BusinessRuleException("CONTAB_CIERRE_ANUAL_ROL_SIN_CUENTA",
                $"El rol {rol} no tiene cuenta asignada: Contabilidad debe mapearlo en Roles de cuenta (resultado del ejercicio).");

        var resultado = saldos.Sum(s => s.Saldo); // deudor neto = pérdida
        var rol = resultado > 0m ? CalculadoraCierreAnual.RolPerdida : CalculadoraCierreAnual.RolUtilidad;
        var tipo = await db.TiposDocumento.AsNoTracking().Where(t => t.Clave == CalculadoraCierreAnual.ClaveTipoPoliza).Select(t => t.Id).FirstOrDefaultAsync(ct);
        if (tipo == Guid.Empty)
            throw new BusinessRuleException("CONTAB_CIERRE_ANUAL_TIPO_POLIZA", $"No existe el tipo de póliza {CalculadoraCierreAnual.ClaveTipoPoliza} (asiento contable).");

        var polizas = new List<PolizaResponse>();
        var bloques = saldos.Chunk(CalculadoraCierreAnual.PartidasPorPoliza).ToList();
        for (var i = 0; i < bloques.Count; i++)
        {
            var partidas = CalculadoraCierreAnual.Partidas(bloques[i], c.Anio, resultado > 0m, s => Cuenta(rol, s));
            var concepto = bloques.Count == 1 ? $"Cierre anual {c.Anio}: cancelación de resultados" : $"Cierre anual {c.Anio}: cancelación de resultados ({i + 1}/{bloques.Count})";
            polizas.Add(await mediator.Send(new CrearPolizaCommand(new PolizaManualRequest(tipo, fecha, concepto, partidas, PeriodoContable.NumeroAjuste,
                new DocumentoOrigenRequest(CalculadoraCierreAnual.Modulo, CalculadoraCierreAnual.TipoDocumento, Guid.CreateVersion7(), folio, null))), ct));
        }

        var ingresos = -saldos.Where(s => s.Saldo < 0m).Sum(s => s.Saldo);
        var gastos = saldos.Where(s => s.Saldo > 0m).Sum(s => s.Saldo);
        return new CierreAnualResponse(c.Anio, ingresos, gastos, -resultado, resultado > 0m ? "Pérdida" : "Utilidad",
            saldos.Select(s => s.CuentaId).Distinct().Count(), polizas);
    }
}

/// <summary>CA10.13: para cerrar el periodo 13, las cuentas de resultados del ejercicio están en cero (pólizas de cierre autorizadas).</summary>
public sealed class VerificadorCierreAnual(CalculadoraCierreAnual calculadora, ContabilidadDbContext db) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.CierreAnual;
    public string Nombre => "Cierre anual: resultados en cero";
    public string Descripcion => "Las cuentas de resultados del ejercicio quedaron canceladas contra resultado del ejercicio (R20).";
    public int Orden => 10;
    public AlcancePasoCierre Alcance => AlcancePasoCierre.Anual;
    public string? Ruta => "/contabilidad/periodos";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var saldos = await calculadora.SaldosAsync(c.Anio, ct);
        var folio = CalculadoraCierreAnual.Folio(c.Anio);
        var borradores = await db.Polizas.AsNoTracking().CountAsync(p => p.OrigenModulo == CalculadoraCierreAnual.Modulo
            && p.OrigenTipo == CalculadoraCierreAnual.TipoDocumento && p.OrigenFolio == folio && p.Estado == EstadoPoliza.Borrador, ct);
        if (saldos.Count == 0)
            return ResultadoVerificacionPaso.Cumple($"Resultados de {c.Anio} en cero.");
        var detalle = saldos.GroupBy(s => s.CuentaCodigo).Select(g => $"{g.Key}: {PasosCierre.Dinero(g.Sum(s => s.Saldo))}");
        return ResultadoVerificacionPaso.Pendiente(borradores > 0
                ? $"La póliza de cierre {folio} está en borrador: autorícela."
                : $"Genere el cierre anual: {saldos.Select(s => s.CuentaId).Distinct().Count()} cuenta(s) de resultados con saldo.", detalle);
    }
}
