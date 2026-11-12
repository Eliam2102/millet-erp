using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Revaluacion;

// M-317 (86e4084j4): anulación de la revaluación de un mes y comprobación posterior.

public sealed record AnularRevaluacionCierreContableCommand(int Anio, int Mes, string Motivo) : IRequest<RevaluacionCierreContableResponse>;

public sealed class AnularRevaluacionCierreContableValidator : AbstractValidator<AnularRevaluacionCierreContableCommand>
{
    public AnularRevaluacionCierreContableValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(c => c.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= Poliza.MotivoMinimo and <= Poliza.MotivoMaximo)
            .WithMessage($"Indique el motivo de la anulación (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
    }
}

/// <summary>
/// Anula (K10.3) las pólizas de la revaluación del mes, con motivo. Solo si el periodo de cada póliza sigue abierto y la reversa del
/// día 1 aún no se registró; todo o nada. Después el mes queda listo para correr la revaluación otra vez.
/// </summary>
public sealed class AnularRevaluacionCierreContableHandler(ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance,
    ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<AnularRevaluacionCierreContableCommand, RevaluacionCierreContableResponse>
{
    public async Task<RevaluacionCierreContableResponse> Handle(AnularRevaluacionCierreContableCommand c, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(AnularRevaluacionCierreContableCommand));
        var docId = CalculadoraRevaluacionCierre.DocumentoId(empresaId, c.Anio, c.Mes);
        await registro.EnTransaccionAsync(async ct =>
        {
            var originales = await db.Polizas.Include(p => p.Partidas)
                .Where(p => p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo && p.OrigenTipo == CalculadoraRevaluacionCierre.TipoRevaluacion
                    && p.OrigenDocumentoId == docId && p.AnuladaPorPolizaId == null && p.PolizaAnuladaId == null && p.Estado != EstadoPoliza.Borrador)
                .OrderBy(p => p.Numero).ToListAsync(ct);
            if (originales.Count == 0)
                throw new ConflictException("REVALUACION_NO_EJECUTADA", $"El mes {c.Mes:00}/{c.Anio} no tiene revaluación vigente que anular.");
            var ids = originales.Select(p => (Guid?)p.Id).ToList();
            if (await db.Polizas.AsNoTracking().AnyAsync(r => r.OrigenModulo == CalculadoraRevaluacionCierre.Modulo
                    && r.OrigenTipo == CalculadoraRevaluacionCierre.TipoReversa && ids.Contains(r.OrigenDocumentoId), ct))
                throw new ConflictException("REVALUACION_REVERSA_YA_CORRIDA",
                    "La reversa del día 1 de esta revaluación ya se registró: no se puede anular. Corrija con una póliza de ajuste.");
            foreach (var p in originales)
            {
                foreach (var s in p.Partidas.Select(x => x.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
                if (await registro.RechazoPeriodoAsync(p.PeriodoAnio, p.PeriodoNumero, p.Origen, ct, p.EmpresaId) is { } rechazo)
                    throw new BusinessRuleException("REVALUACION_PERIODO_NO_ABIERTO",
                        $"El periodo {PeriodoContable.Etiqueta(p.PeriodoAnio, p.PeriodoNumero)} no está abierto: la revaluación no se puede anular. {rechazo.Message}");
            }
            foreach (var p in originales)
                await registro.AnularAsync(p, c.Motivo, null, FechaContable.HoyLocal(clock.UtcNow), usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow, ct);
            await PoliticaPolizas.GuardarAnulacionAsync(db, ct);
        }, cancellationToken);
        var polizas = await RevaluacionCierreComun.PolizasDelMesAsync(db, empresaId, c.Anio, c.Mes, cancellationToken);
        return RevaluacionCierreComun.Respuesta(c.Anio, c.Mes, polizas, [], ["Revaluación anulada: el mes puede correrse otra vez."]);
    }
}

/// <summary>Una cuenta (y su auxiliar) con el saldo en divisa a TC de cierre y el saldo contable; la diferencia debe ser cero.</summary>
public sealed record ComprobacionRevaluacionFila(ControlRevaluacion Control, string CuentaCodigo, Guid SucursalId, Guid? ClienteId,
    Guid? CuentaBancariaId, string Monedas, decimal SaldoDivisaAlCierre, decimal SaldoContable, decimal Diferencia);

public sealed record ComprobacionRevaluacionResponse(int Anio, int Mes, DateOnly FechaCierre, bool Ejecutada, bool EnCero, decimal DiferenciaTotal,
    IReadOnlyList<ComprobacionRevaluacionFila> Filas, IReadOnlyList<string> Avisos);

public sealed record ComprobarRevaluacionCierreQuery(int Anio, int Mes) : IRequest<ComprobacionRevaluacionResponse>;

public sealed class ComprobarRevaluacionCierreValidator : AbstractValidator<ComprobarRevaluacionCierreQuery>
{
    public ComprobarRevaluacionCierreValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(c => c.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

/// <summary>
/// Comprobación posterior: por cuenta, sucursal y auxiliar, saldo en divisa × TC de cierre contra el saldo contable (movimientos en
/// divisa más los ajustes de revaluación y sus reversas, al último día del mes). Con la revaluación hecha sale en cero.
/// </summary>
public sealed class ComprobarRevaluacionCierreHandler(ContabilidadDbContext db, CalculadoraRevaluacionCierre calculadora, ICurrentEmpresaContext empresa)
    : IRequestHandler<ComprobarRevaluacionCierreQuery, ComprobacionRevaluacionResponse>
{
    public async Task<ComprobacionRevaluacionResponse> Handle(ComprobarRevaluacionCierreQuery q, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(ComprobarRevaluacionCierreQuery));
        var cierre = CalculadoraRevaluacionCierre.Cierre(q.Anio, q.Mes);
        var ejecutada = (await RevaluacionCierreComun.PolizasDelMesAsync(db, empresaId, q.Anio, q.Mes, ct)).Any(p => !p.EsReversa);
        var avisos = new List<string>();
        IReadOnlyList<RevaluacionCierreDetalle> detalles = [];
        try { detalles = await calculadora.CalcularAsync(q.Anio, q.Mes, ct); }
        catch (BusinessRuleException e) { avisos.Add(e.Message); }
        if (!ejecutada) avisos.Add("La revaluación del mes aún no se registra: la diferencia que se ve es la que falta asentar.");

        var filas = new List<ComprobacionRevaluacionFila>();
        foreach (var porControl in detalles.GroupBy(d => d.Control))
        {
            var cuentas = porControl.Select(d => d.CuentaId).Distinct().ToList();
            var contable = await ContableAsync(porControl.Key, cuentas, cierre, ct);
            foreach (var g in porControl.GroupBy(d => (d.CuentaId, d.CuentaCodigo, d.SucursalId, d.ClienteId, d.CuentaBancariaId)))
            {
                var alCierre = g.Sum(d => d.SaldoCierre);
                var libros = contable.GetValueOrDefault((g.Key.CuentaId, g.Key.SucursalId, g.Key.ClienteId, g.Key.CuentaBancariaId));
                filas.Add(new(porControl.Key, g.Key.CuentaCodigo, g.Key.SucursalId, g.Key.ClienteId, g.Key.CuentaBancariaId,
                    string.Join(", ", g.Select(d => d.Moneda).Distinct()), alCierre, libros, alCierre - libros));
            }
        }
        var total = filas.Sum(f => Math.Abs(f.Diferencia));
        return new(q.Anio, q.Mes, cierre, ejecutada, ejecutada && total == 0m, total, filas, avisos);
    }

    /// <summary>Saldo en pesos al cierre: partidas en divisa del auxiliar más las pólizas de revaluación y sus reversas (en pesos).</summary>
    private async Task<Dictionary<(Guid, Guid, Guid?, Guid?), decimal>> ContableAsync(ControlRevaluacion control, List<Guid> cuentas, DateOnly cierre,
        CancellationToken ct)
    {
        var partidas = await (from p in db.Polizas.AsNoTracking()
                              join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
                              where p.Fecha <= cierre && p.Estado != EstadoPoliza.Borrador && cuentas.Contains(d.CuentaId)
                                  && (d.Moneda != Poliza.MonedaNacional
                                      || (p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo
                                          && (p.OrigenTipo == CalculadoraRevaluacionCierre.TipoRevaluacion || p.OrigenTipo == CalculadoraRevaluacionCierre.TipoReversa)))
                              select new { d.CuentaId, d.SucursalId, d.ClienteId, d.CuentaBancariaId, d.Cargo, d.Abono }).ToListAsync(ct);
        return partidas.GroupBy(x => (x.CuentaId, x.SucursalId, control == ControlRevaluacion.Clientes ? x.ClienteId : null,
                control == ControlRevaluacion.Bancos ? x.CuentaBancariaId : null))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cargo - x.Abono));
    }
}
