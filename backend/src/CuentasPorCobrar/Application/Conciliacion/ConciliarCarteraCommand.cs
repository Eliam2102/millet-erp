using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Conciliacion;

/// <summary>Día contable de Millet: mismo criterio que Facturación (<c>EventosContablesFacturacion.FechaContable</c>).</summary>
public static class DiaContableCxc
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");

    public static DateOnly De(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, Zona).DateTime);

    public static DateTimeOffset Ahora(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zona);
}

/// <summary>
/// C7.7: concilia un día contable de Facturación contra la cartera y deja una
/// corrida registrada por empresa, con o sin diferencias. A demanda concilia la
/// empresa del usuario; el worker corre en bypass y concilia todas.
/// </summary>
public sealed record ConciliarCarteraCommand(DateOnly Fecha, OrigenConciliacion Origen)
    : IRequest<IReadOnlyList<ConciliacionResumenResponse>>;

public sealed class ConciliarCarteraValidator : AbstractValidator<ConciliarCarteraCommand>
{
    public ConciliarCarteraValidator(IClock clock)
    {
        RuleFor(x => x.Fecha)
            .Must(f => f <= DiaContableCxc.De(clock.UtcNow))
            .WithMessage("No se puede conciliar un día que todavía no llega.");
        RuleFor(x => x.Origen).IsInEnum();
    }
}

public sealed class ConciliarCarteraHandler(
    CuentasPorCobrarDbContext db,
    IConciliacionFacturacionReadPort facturacion,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext usuario,
    IClock clock) : IRequestHandler<ConciliarCarteraCommand, IReadOnlyList<ConciliacionResumenResponse>>
{
    public async Task<IReadOnlyList<ConciliacionResumenResponse>> Handle(
        ConciliarCarteraCommand request, CancellationToken cancellationToken)
    {
        if (request.Origen == OrigenConciliacion.Demanda && empresa.Current is null)
            throw new BusinessRuleException("CC_SIN_EMPRESA", "Selecciona la empresa a conciliar.");

        var dia = await facturacion.ConsultarDiaAsync(request.Fecha, cancellationToken);

        var facturaIds = dia.Facturas.Select(f => f.FacturaVentaId)
            .Concat(dia.Aplicaciones.Select(a => a.FacturaVentaId))
            .Distinct().ToList();
        var facturasCartera = await db.FacturasCartera.AsNoTracking()
            .Where(f => facturaIds.Contains(f.FacturaVentaId))
            .Select(f => new FacturaEnCartera(f.Id, f.FacturaVentaId, f.Total, f.Estado))
            .ToListAsync(cancellationToken);
        var origenIds = dia.Aplicaciones.Select(a => a.OrigenId).Distinct().ToList();
        var movimientos = await db.MovimientosCartera.AsNoTracking()
            .Where(m => origenIds.Contains(m.OrigenComprobanteId))
            .Select(m => new MovimientoEnCartera(m.FacturaCarteraId, m.Tipo, m.OrigenComprobanteId, m.Importe, m.Revertido))
            .ToListAsync(cancellationToken);

        // Una corrida por empresa: la del usuario, o en el worker cada empresa con
        // cartera o con comprobantes ese día (un día limpio también se registra).
        IReadOnlyList<Guid> empresas = empresa.Current is Guid actual
            ? [actual]
            : (await db.FacturasCartera.AsNoTracking().Select(f => f.EmpresaId).Distinct().ToListAsync(cancellationToken))
                .Concat(dia.Facturas.Select(f => f.EmpresaId))
                .Concat(dia.Aplicaciones.Select(a => a.EmpresaId))
                .Distinct().ToList();

        var ahora = clock.UtcNow;
        var corridas = new List<ConciliacionCartera>();
        foreach (var empresaId in empresas)
        {
            var delDia = new ComprobantesDelDia(
                dia.Facturas.Where(f => f.EmpresaId == empresaId).ToList(),
                dia.Aplicaciones.Where(a => a.EmpresaId == empresaId).ToList());
            var diferencias = ComparadorConciliacion.Comparar(delDia, facturasCartera, movimientos);
            corridas.Add(ConciliacionCartera.Registrar(empresaId, request.Fecha, request.Origen,
                request.Origen == OrigenConciliacion.Demanda ? usuario.UserId : null,
                ahora, delDia.Total, diferencias));
        }

        db.ConciliacionesCartera.AddRange(corridas);
        await db.SaveChangesAsync(cancellationToken);
        return corridas.Select(ConciliacionResumenResponse.De).ToList();
    }
}
