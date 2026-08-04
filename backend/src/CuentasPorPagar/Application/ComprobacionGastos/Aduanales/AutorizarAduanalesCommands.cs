using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Transiciones;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Aduanales;

// ============================================================================
// F7-PR2: doble firma para comprobaciones de gastos aduanales (§7.2).
// Nivel 1 = Comercio Exterior; Nivel 2 = Dirección de Finanzas. Nivel 2
// también transiciona las facturas ligadas a EstadoPasivo.Autorizada
// para que Tesorería pueda pagar.
// ============================================================================

// ------------------------------------------------------ AutorizarNivel1

public sealed record AutorizarNivel1AduanalesCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionComprobacionResponse>;

public sealed class AutorizarNivel1AduanalesValidator : AbstractValidator<AutorizarNivel1AduanalesCommand>
{
    public AutorizarNivel1AduanalesValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AutorizarNivel1AduanalesHandler
    : IRequestHandler<AutorizarNivel1AduanalesCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AutorizarNivel1AduanalesHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        AutorizarNivel1AduanalesCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para firmar Nivel 1.");
        c.AutorizarNivel1Aduanales(usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }
}

// ------------------------------------------------------ AutorizarNivel2

public sealed record AutorizarNivel2AduanalesCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionComprobacionResponse>;

public sealed class AutorizarNivel2AduanalesValidator : AbstractValidator<AutorizarNivel2AduanalesCommand>
{
    public AutorizarNivel2AduanalesValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Firma Nivel 2 (Dirección de Finanzas). En la misma transacción
/// autoriza las facturas ligadas (EstadoPasivo.Capturada → Autorizada)
/// para que aparezcan en la bandeja de Tesorería. Publica el
/// <see cref="FacturaProveedorAutorizadaDomainEvent"/> por cada factura
/// autorizada — los mappers de Integration lo convierten en
/// <c>pasivo.autorizado-para-pago.v1</c> (Tesorería) y
/// <c>factura.autorizada.v1</c> (Compras/Contabilidad); sin él, el
/// pasivo quedaba autorizado en BD pero invisible para Tesorería
/// (hallazgo P7-H2).
/// </summary>
public sealed class AutorizarNivel2AduanalesHandler
    : IRequestHandler<AutorizarNivel2AduanalesCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public AutorizarNivel2AduanalesHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IMediator mediator, IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        AutorizarNivel2AduanalesCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para firmar Nivel 2.");
        var ahora = _clock.UtcNow;

        c.AutorizarNivel2Aduanales(usuarioId, ahora);

        // Autoriza las facturas ligadas para que Tesorería las vea.
        var facturaIds = c.Lineas.Select(l => l.FacturaProveedorId).ToList();
        var facturas = await _db.FacturasProveedor
            .Where(f => facturaIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        foreach (var f in facturas)
        {
            // Idempotencia: si ya está Autorizada por re-entrega, skip.
            if (f.Estado == Domain.FacturaProveedor.EstadoPasivo.Capturada)
            {
                f.Autorizar(usuarioId, ahora);

                // ANTES de SaveChanges para que el outbox interceptor
                // drene los integration events en la misma TX (ADR-0009).
                await _mediator.Publish(new FacturaProveedorAutorizadaDomainEvent(
                    EmpresaId: f.EmpresaId,
                    FacturaProveedorId: f.Id,
                    OrdenCompraId: f.OrdenCompraId,
                    FechaAutorizacion: ahora), cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }
}
