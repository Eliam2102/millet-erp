using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

public sealed record RegistrarSaldoInicialCommand(Guid CuentaId, decimal Saldo, DateOnly FechaCorte,
    string Motivo, int VersionEsperada) : IRequest;
public sealed class RegistrarSaldoInicialValidator : AbstractValidator<RegistrarSaldoInicialCommand>
{
    public RegistrarSaldoInicialValidator()
    {
        RuleFor(x => x.CuentaId).NotEmpty();
        RuleFor(x => x.FechaCorte).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}
public sealed class RegistrarSaldoInicialHandler(TesoreriaDbContext db, ICurrentUserPermissions permissions)
    : IRequestHandler<RegistrarSaldoInicialCommand>
{
    public async Task Handle(RegistrarSaldoInicialCommand command, CancellationToken cancellationToken)
    {
        if (!await permissions.TieneAsync(PermisosCanonicos.TesoreriaCuentasAdministrar, cancellationToken))
            throw new ForbiddenException("CTA_SALDO_SIN_PERMISO", "Necesitas permiso de administración de cuentas para registrar el saldo inicial.");
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == command.CuentaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");
        if (cuenta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(cuenta), cuenta.Id);
        if (await db.MovimientosBancarios.AnyAsync(m => m.CuentaBancariaId == cuenta.Id && m.FechaValor <= command.FechaCorte, cancellationToken))
            throw new BusinessRuleException("CTA_CORTE_CON_MOVIMIENTOS", "El corte debe ser anterior al primer movimiento registrado para evitar contar importes dos veces.");
        cuenta.RegistrarSaldoInicial(command.Saldo, command.FechaCorte, command.Motivo);
        await db.SaveChangesAsync(cancellationToken);
    }
}
