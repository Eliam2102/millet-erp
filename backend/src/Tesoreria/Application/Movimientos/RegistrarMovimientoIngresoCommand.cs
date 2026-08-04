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
// TES-PR2: alta manual de movimiento de INGRESO (§3.3 paso 2, endpoint POST
// movimientos del §11). Los egresos no entran por aquí: solo vía pagos
// (RN-1, PR-4) o pago a cuenta (RN-2, PR-6). Con conciliación activa (PR-9)
// la alta asistida desde extracto reutiliza este flujo.
// ============================================================================

public sealed record RegistrarMovimientoIngresoCommand(
    Guid CuentaBancariaId,
    decimal Monto,
    DateOnly FechaValor,
    string? ReferenciaBancaria = null,
    Guid? ConceptoId = null,
    BeneficiarioTipo? BeneficiarioTipo = null,
    Guid? BeneficiarioRef = null) : IRequest<MovimientoBancarioResponse>;

public sealed class RegistrarMovimientoIngresoValidator : AbstractValidator<RegistrarMovimientoIngresoCommand>
{
    public RegistrarMovimientoIngresoValidator()
    {
        RuleFor(c => c.CuentaBancariaId).NotEmpty();
        RuleFor(c => c.Monto).GreaterThan(0);
        RuleFor(c => c.FechaValor).NotEmpty();
        RuleFor(c => c.ReferenciaBancaria).MaximumLength(120);
        RuleFor(c => c.BeneficiarioTipo).IsInEnum().When(c => c.BeneficiarioTipo is not null);
        RuleFor(c => c.BeneficiarioTipo)
            .NotNull()
            .When(c => c.BeneficiarioRef is not null)
            .WithMessage("Si se indica la referencia del beneficiario, el tipo es obligatorio.");
    }
}

public sealed class RegistrarMovimientoIngresoHandler
    : IRequestHandler<RegistrarMovimientoIngresoCommand, MovimientoBancarioResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IClock _clock;

    public RegistrarMovimientoIngresoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _clock = clock;
    }

    public async Task<MovimientoBancarioResponse> Handle(
        RegistrarMovimientoIngresoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el movimiento.");

        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaBancariaId}'.");

        if (command.ConceptoId is Guid conceptoId)
        {
            var conceptoActivo = await _db.ConceptosMovimiento
                .AnyAsync(c => c.Id == conceptoId && c.Activo, cancellationToken);
            if (!conceptoActivo)
                throw new BusinessRuleException("MOV_CONCEPTO_INVALIDO",
                    $"El concepto '{conceptoId}' no existe o está inactivo.");
        }

        // RN-8: registro bloqueado en período cerrado (stub siempre-abierto
        // hasta Contabilidad — PLATFORM-TODO(<PeriodoContableCerrado>)).
        var abierto = await _periodoContable.EstaAbiertoAsync(
            command.FechaValor.Year, command.FechaValor.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        var movimiento = MovimientoBancario.RegistrarIngreso(
            empresaId: empresaId,
            cuenta: cuenta,
            monto: command.Monto,
            fechaValor: command.FechaValor,
            referenciaBancaria: command.ReferenciaBancaria,
            conceptoId: command.ConceptoId,
            beneficiarioTipo: command.BeneficiarioTipo,
            beneficiarioRef: command.BeneficiarioRef,
            creadoPor: usuarioId,
            ahora: _clock.UtcNow);

        _db.MovimientosBancarios.Add(movimiento);
        await _db.SaveChangesAsync(cancellationToken);

        return MovimientoBancarioMapper.ToResponse(movimiento);
    }
}
