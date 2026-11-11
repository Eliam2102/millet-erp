using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>
/// Opciones de la reapertura acotada (M-213), sección <c>Contabilidad:Reapertura</c>. <c>MaximoDias</c> (0 = sin tope) es un
/// supuesto a validar con Contabilidad: la ficha no fija un máximo de vigencia.
/// </summary>
public sealed class ReaperturaOpciones
{
    public const string Seccion = "Contabilidad:Reapertura";
    public int MaximoDias { get; set; } = 30;
    public bool Deshabilitado { get; set; }
    public int IntervaloMinutos { get; set; } = 5;
}

/// <summary>
/// Evalúa la reapertura vigente de un periodo contra lo que se intenta registrar (M-213). El vencimiento se compara siempre con la
/// hora del servidor (<see cref="IClock"/>), nunca con la del cliente: una reapertura vencida equivale a periodo cerrado aunque el
/// proceso de cierre formal aún no haya corrido.
/// </summary>
public sealed class ValidadorReapertura(ContabilidadDbContext db, IClock clock)
{
    /// <summary>Reapertura vigente del periodo. Con <paramref name="empresaId"/> (procesos sin request) se acota a esa empresa.</summary>
    public Task<ReaperturaPeriodo?> VigenteAsync(int anio, int numero, Guid? empresaId, CancellationToken ct)
    {
        var q = empresaId is { } e
            ? db.Reaperturas.IgnoreQueryFilters().Where(r => r.EmpresaId == e)
            : db.Reaperturas.AsQueryable();
        return q.AsNoTracking().Where(r => r.Vigente && r.Anio == anio && r.Numero == numero).OrderByDescending(r => r.CreadaEn)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Vencida → rechazo de periodo cerrado; vigente → rechazo si lo registrado cae fuera del alcance; sin reapertura acotada → null.
    /// </summary>
    public async Task<BusinessRuleException?> RechazoAsync(int anio, int numero, ContextoRegistroPoliza? contexto, Guid? empresaId, CancellationToken ct)
    {
        if (await VigenteAsync(anio, numero, empresaId, ct) is not { } r) return null;
        return r.Vencida(clock.UtcNow)
            ? new("CONTAB_PERIODO_REAPERTURA_VENCIDA",
                $"La reapertura del periodo {PeriodoContable.Etiqueta(anio, numero)} venció: el periodo vuelve a estar cerrado y no admite pólizas.")
            : r.Rechazo(contexto);
    }

    /// <summary>Vigencia efectiva de un periodo «Abierto» en BD: true si su reapertura ya venció (hora del servidor).</summary>
    public async Task<bool> VencidaAsync(int anio, int numero, Guid? empresaId, CancellationToken ct) =>
        await VigenteAsync(anio, numero, empresaId, ct) is { } r && r.Vencida(clock.UtcNow);

    /// <summary>Resuelve los códigos de cuenta de las partidas para el contexto (las cuentas son de la empresa de la póliza).</summary>
    public async Task<ContextoRegistroPoliza> ContextoAsync(string? tipoClave, Guid? usuarioId, string? modulo, IEnumerable<Guid> cuentaIds, CancellationToken ct)
    {
        var ids = cuentaIds.Distinct().ToList();
        var codigos = ids.Count == 0 ? [] : await db.Cuentas.IgnoreQueryFilters().AsNoTracking()
            .Where(c => ids.Contains(c.Id)).Select(c => c.Codigo).ToListAsync(ct);
        return new(tipoClave, usuarioId, modulo, codigos);
    }
}
