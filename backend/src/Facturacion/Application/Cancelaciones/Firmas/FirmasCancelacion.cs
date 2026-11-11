using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.Firmas;

/// <summary>M-235: importe en MXN y firmas que pide la cancelación de un comprobante.</summary>
public static class FirmasCancelacion
{
    /// <summary>Total con impuestos en MXN (con su tipo de cambio si viene en otra moneda), como M-087.</summary>
    public static decimal ImporteMxn(Comprobante c) =>
        Math.Round(c.Total * (c.TipoCambio ?? 1m), 2, MidpointRounding.AwayFromZero);

    public static async Task<short> RequeridasAsync(FacturacionDbContext db, Comprobante c, CancellationToken ct)
    {
        var reglas = await db.ReglasFirmaCancelacion.AsNoTracking().ToListAsync(ct);
        return ReglaFirmaCancelacion.FirmasPara(reglas, c.GetType().Name, ImporteMxn(c));
    }
}

public sealed record ReglaFirmaCancelacionDto(string? TipoComprobante, decimal UmbralMxn, short FirmasRequeridas);

/// <summary>M-235: reglas de firmas de la cancelación (vacía = una firma para todo).</summary>
public sealed record ListarReglasFirmaCancelacionQuery : IRequest<IReadOnlyList<ReglaFirmaCancelacionDto>>;

/// <summary>M-235: reemplaza las reglas de firmas de la empresa. Aplican a las solicitudes nuevas.</summary>
public sealed record GuardarReglasFirmaCancelacionCommand(IReadOnlyList<ReglaFirmaCancelacionDto> Reglas)
    : IRequest<IReadOnlyList<ReglaFirmaCancelacionDto>>;

public sealed class ReglasFirmaCancelacionHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    : IRequestHandler<ListarReglasFirmaCancelacionQuery, IReadOnlyList<ReglaFirmaCancelacionDto>>,
      IRequestHandler<GuardarReglasFirmaCancelacionCommand, IReadOnlyList<ReglaFirmaCancelacionDto>>
{
    public async Task<IReadOnlyList<ReglaFirmaCancelacionDto>> Handle(ListarReglasFirmaCancelacionQuery query, CancellationToken cancellationToken)
    {
        var reglas = await db.ReglasFirmaCancelacion.AsNoTracking().ToListAsync(cancellationToken);
        return Ordenar(reglas);
    }

    public async Task<IReadOnlyList<ReglaFirmaCancelacionDto>> Handle(GuardarReglasFirmaCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var nuevas = command.Reglas
            .Select(r => ReglaFirmaCancelacion.Crear(empresaId, r.TipoComprobante, r.UmbralMxn, r.FirmasRequeridas))
            .ToList();
        if (nuevas.GroupBy(r => (r.TipoComprobante, r.UmbralMxn)).Any(g => g.Count() > 1))
            throw new BusinessRuleException("CANCELACION_REGLA_DUPLICADA", "Hay dos reglas con el mismo tipo de operación y umbral.");

        db.ReglasFirmaCancelacion.RemoveRange(await db.ReglasFirmaCancelacion.ToListAsync(cancellationToken));
        db.ReglasFirmaCancelacion.AddRange(nuevas);
        await db.SaveChangesAsync(cancellationToken);
        return Ordenar(nuevas);
    }

    private static List<ReglaFirmaCancelacionDto> Ordenar(IEnumerable<ReglaFirmaCancelacion> reglas) =>
        reglas.OrderBy(r => r.TipoComprobante ?? string.Empty).ThenBy(r => r.UmbralMxn)
            .Select(r => new ReglaFirmaCancelacionDto(r.TipoComprobante, r.UmbralMxn, r.FirmasRequeridas))
            .ToList();
}
