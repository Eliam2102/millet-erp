using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Terminal;

public sealed record TerminalAfiliacionResponse(Guid Id, string Afiliacion, string CodTerminal, Guid SucursalId, string? Descripcion, bool Activa);

/// <summary>Terminal que aparece en los archivos cargados y aún no está en el catálogo (para darla de alta).</summary>
public sealed record TerminalSinCatalogarResponse(string Afiliacion, string CodTerminal, int Ventas);

/// <summary>Da de alta la terminal de una afiliación en una sucursal, o la reasigna/reactiva si ya existe.</summary>
public sealed record GuardarTerminalAfiliacionCommand(string Afiliacion, string CodTerminal, Guid SucursalId, string? Descripcion)
    : IRequest<TerminalAfiliacionResponse>;

public sealed record DesactivarTerminalAfiliacionCommand(Guid Id) : IRequest<TerminalAfiliacionResponse>;

public sealed record TerminalesAfiliacionQuery(bool IncluirInactivas = false) : IRequest<IReadOnlyList<TerminalAfiliacionResponse>>;

public sealed record TerminalesSinCatalogarQuery : IRequest<IReadOnlyList<TerminalSinCatalogarResponse>>;

public sealed class GuardarTerminalAfiliacionValidator : AbstractValidator<GuardarTerminalAfiliacionCommand>
{
    public GuardarTerminalAfiliacionValidator()
    {
        RuleFor(c => c.Afiliacion).NotEmpty().MaximumLength(20);
        RuleFor(c => c.CodTerminal).NotEmpty().MaximumLength(20);
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Descripcion).MaximumLength(200);
    }
}

public sealed class TerminalAfiliacionHandlers(FacturacionDbContext db, ICurrentEmpresaContext empresa, ISucursalesReadPort sucursales) :
    IRequestHandler<GuardarTerminalAfiliacionCommand, TerminalAfiliacionResponse>,
    IRequestHandler<DesactivarTerminalAfiliacionCommand, TerminalAfiliacionResponse>,
    IRequestHandler<TerminalesAfiliacionQuery, IReadOnlyList<TerminalAfiliacionResponse>>,
    IRequestHandler<TerminalesSinCatalogarQuery, IReadOnlyList<TerminalSinCatalogarResponse>>
{
    public async Task<TerminalAfiliacionResponse> Handle(GuardarTerminalAfiliacionCommand request, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        if ((await sucursales.FiltrarNoActivasAsync([request.SucursalId], cancellationToken)).Count > 0)
            throw new BusinessRuleException("TERMINAL_SUCURSAL_INACTIVA", "La sucursal no existe o no está activa.");

        var afiliacion = request.Afiliacion.Trim();
        var codigo = request.CodTerminal.Trim();
        var terminal = await db.TerminalesAfiliacion.FirstOrDefaultAsync(t => t.Afiliacion == afiliacion && t.CodTerminal == codigo, cancellationToken);
        if (terminal is null)
        {
            terminal = TerminalAfiliacion.Crear(empresaId, afiliacion, codigo, request.SucursalId, request.Descripcion);
            db.TerminalesAfiliacion.Add(terminal);
        }
        else terminal.Actualizar(request.SucursalId, request.Descripcion);
        await db.SaveChangesAsync(cancellationToken);
        return De(terminal);
    }

    public async Task<TerminalAfiliacionResponse> Handle(DesactivarTerminalAfiliacionCommand request, CancellationToken cancellationToken)
    {
        var terminal = await db.TerminalesAfiliacion.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TERMINAL_CATALOGO_NO_ENCONTRADA", "La terminal no está en el catálogo.");
        terminal.Desactivar();
        await db.SaveChangesAsync(cancellationToken);
        return De(terminal);
    }

    public async Task<IReadOnlyList<TerminalAfiliacionResponse>> Handle(TerminalesAfiliacionQuery request, CancellationToken cancellationToken)
    {
        var q = db.TerminalesAfiliacion.AsNoTracking();
        if (!request.IncluirInactivas) q = q.Where(t => t.Activa);
        return (await q.OrderBy(t => t.Afiliacion).ThenBy(t => t.CodTerminal).ToListAsync(cancellationToken)).Select(De).ToList();
    }

    public async Task<IReadOnlyList<TerminalSinCatalogarResponse>> Handle(TerminalesSinCatalogarQuery request, CancellationToken cancellationToken)
    {
        var vistas = await db.TransaccionesTerminal.AsNoTracking()
            .Where(t => t.Afiliacion != null && t.CodTerminal != null)
            .GroupBy(t => new { t.Afiliacion, t.CodTerminal })
            .Select(g => new { g.Key.Afiliacion, g.Key.CodTerminal, Ventas = g.Count() })
            .ToListAsync(cancellationToken);
        var catalogadas = (await db.TerminalesAfiliacion.AsNoTracking().Where(t => t.Activa)
            .Select(t => new { t.Afiliacion, t.CodTerminal }).ToListAsync(cancellationToken))
            .Select(t => (t.Afiliacion, t.CodTerminal)).ToHashSet();
        return vistas.Where(v => !catalogadas.Contains((v.Afiliacion!, v.CodTerminal!)))
            .OrderBy(v => v.Afiliacion).ThenBy(v => v.CodTerminal)
            .Select(v => new TerminalSinCatalogarResponse(v.Afiliacion!, v.CodTerminal!, v.Ventas)).ToList();
    }

    private static TerminalAfiliacionResponse De(TerminalAfiliacion t) =>
        new(t.Id, t.Afiliacion, t.CodTerminal, t.SucursalId, t.Descripcion, t.Activa);
}
