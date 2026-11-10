using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Movimientos;

// ============================================================================
// M-203 (86e3zr7k1): traspaso entre cuentas propias. Una sola operación que
// crea el egreso en origen y el ingreso en destino (misma transacción, misma
// referencia, mismo TraspasoId) y que el flujo de efectivo no cuenta como
// ingreso ni egreso. Revertir cualquiera de los dos revierte el otro.
// La póliza la genera el motor contable con la plantilla de movimiento bancario.
// ============================================================================

public sealed record TraspasoResponse(Guid TraspasoId, MovimientoBancarioResponse Egreso, MovimientoBancarioResponse Ingreso);

public sealed record RegistrarTraspasoCommand(
    Guid CuentaOrigenId,
    Guid CuentaDestinoId,
    decimal Monto,
    DateOnly FechaValor,
    string Referencia,
    string? Motivo = null,
    Guid? SucursalId = null) : IRequest<TraspasoResponse>;

public sealed class RegistrarTraspasoValidator : AbstractValidator<RegistrarTraspasoCommand>
{
    public RegistrarTraspasoValidator()
    {
        RuleFor(c => c.CuentaOrigenId).NotEmpty();
        RuleFor(c => c.CuentaDestinoId).NotEmpty().NotEqual(c => c.CuentaOrigenId)
            .WithMessage("La cuenta origen y la destino deben ser distintas.");
        RuleFor(c => c.Monto).GreaterThan(0);
        RuleFor(c => c.FechaValor).NotEmpty();
        RuleFor(c => c.Referencia).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Motivo).MaximumLength(400);
    }
}

public sealed class RegistrarTraspasoHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
    IPeriodoContablePort periodoContable, IClock clock) : IRequestHandler<RegistrarTraspasoCommand, TraspasoResponse>
{
    public async Task<TraspasoResponse> Handle(RegistrarTraspasoCommand command, CancellationToken cancellationToken)
    {
        if (currentEmpresa.Current is null)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que registra el traspaso.");

        async Task<Domain.Cuentas.CuentaBancaria> Cuenta(Guid id) =>
            await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", $"No se encontró la cuenta bancaria '{id}'.");
        var origen = await Cuenta(command.CuentaOrigenId);
        var destino = await Cuenta(command.CuentaDestinoId);

        // RN-8: el período contable de la fecha valor debe estar abierto.
        if (!await periodoContable.EstaAbiertoAsync(command.FechaValor.Year, command.FechaValor.Month, cancellationToken))
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        var (egreso, ingreso) = MovimientoBancario.RegistrarTraspaso(
            origen, destino, command.Monto, command.FechaValor, command.Referencia, command.Motivo,
            usuarioId, clock.UtcNow, command.SucursalId);
        db.MovimientosBancarios.AddRange(egreso, ingreso);
        await db.SaveChangesAsync(cancellationToken);

        var concepto = await db.ConceptosMovimiento.AsNoTracking()
            .Where(c => c.Id == Domain.Cuentas.ConceptoMovimiento.TraspasoId).Select(c => c.Nombre).FirstOrDefaultAsync(cancellationToken);
        return new(egreso.TraspasoId!.Value,
            MovimientoBancarioMapper.ToResponse(egreso, concepto, true), MovimientoBancarioMapper.ToResponse(ingreso, concepto, true));
    }
}

/// <summary>Revierte el traspaso completo desde cualquiera de sus dos movimientos (contramovimientos ligados en ambas cuentas).</summary>
public sealed record RevertirTraspasoCommand(Guid MovimientoId, string Motivo) : IRequest<IReadOnlyList<MovimientoBancarioResponse>>;

public sealed class RevertirTraspasoValidator : AbstractValidator<RevertirTraspasoCommand>
{
    public RevertirTraspasoValidator()
    {
        RuleFor(c => c.MovimientoId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RevertirTraspasoHandler(
    TesoreriaDbContext db, ICurrentUserContext currentUser, IPeriodoContablePort periodoContable, IClock clock)
    : IRequestHandler<RevertirTraspasoCommand, IReadOnlyList<MovimientoBancarioResponse>>
{
    public async Task<IReadOnlyList<MovimientoBancarioResponse>> Handle(RevertirTraspasoCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que revierte el traspaso.");
        var traspasoId = await db.MovimientosBancarios.Where(m => m.Id == command.MovimientoId).Select(m => m.TraspasoId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("TRASPASO_NO_ES_TRASPASO", "El movimiento no pertenece a un traspaso entre cuentas propias.");
        var lados = await db.MovimientosBancarios.Where(m => m.TraspasoId == traspasoId).ToListAsync(cancellationToken);
        var ids = lados.Select(l => l.Id).ToList();
        if (await db.MovimientosBancarios.AnyAsync(m => m.ContramovimientoDe != null && ids.Contains(m.ContramovimientoDe.Value), cancellationToken))
            throw new BusinessRuleException("TRASPASO_YA_REVERTIDO", "Este traspaso ya fue revertido.");
        if (lados.Any(l => l.EstadoConciliacion == EstadoConciliacionMovimiento.Conciliado))
            throw new BusinessRuleException("TRASPASO_CONCILIADO", "Un lado del traspaso ya está conciliado; desconcílialo antes de revertir.");

        var hoy = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (!await periodoContable.EstaAbiertoAsync(hoy.Year, hoy.Month, cancellationToken))
            throw new BusinessRuleException("MOV_PERIODO_CERRADO", $"El período {hoy.Year}/{hoy.Month:00} está cerrado.");

        var contras = lados.Select(l => l.CrearContramovimiento(l.Monto, hoy, usuarioId, clock.UtcNow, command.Motivo)).ToList();
        db.MovimientosBancarios.AddRange(contras);
        await db.SaveChangesAsync(cancellationToken);
        return contras.Select(c => MovimientoBancarioMapper.ToResponse(c, null, true)).ToList();
    }
}
