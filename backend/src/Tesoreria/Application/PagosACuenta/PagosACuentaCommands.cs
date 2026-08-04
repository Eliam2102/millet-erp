using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PagosACuenta;

// ============================================================================
// TES-PR6: pago a cuenta (§3.4 / TES-2) — el pago urgente sin factura SÍ se
// captura (el limbo se registra, no se oculta; riesgo #1 del área). Gate
// RN-2: máximo UN pago no aplicado abierto por proveedor — validación aquí
// + índice parcial único ux_pago_cuenta_abierto de respaldo ante dobles
// submit. La liga tardía aplica el movimiento preexistente al pasivo SIN
// re-desembolsar y emite aplicado.v1 (mismo contrato congelado que PR-4).
//
// Autorización: en MVP el gate humano es el permiso
// tesoreria.pagos-cuenta.registrar (Jefe de Tesorería); la matriz de
// autorización llega con PR-5 [T-G4].
// ============================================================================

public sealed record PagoACuentaResponse(
    Guid MovimientoId,
    Guid CuentaBancariaId,
    Guid? ProveedorId,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    string Motivo,
    EstadoAplicacionMovimiento EstadoAplicacion);

// --------------------------------------------------- Registrar (RN-2)

public sealed record RegistrarPagoACuentaCommand(
    Guid CuentaBancariaId,
    decimal Monto,
    DateOnly FechaValor,
    string Motivo,
    Guid? ProveedorId = null,
    string? ReferenciaBancaria = null,
    Guid? ConceptoId = null) : IRequest<PagoACuentaResponse>;

public sealed class RegistrarPagoACuentaValidator : AbstractValidator<RegistrarPagoACuentaCommand>
{
    public RegistrarPagoACuentaValidator()
    {
        RuleFor(c => c.CuentaBancariaId).NotEmpty();
        RuleFor(c => c.Monto).GreaterThan(0);
        RuleFor(c => c.FechaValor).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
        RuleFor(c => c.ReferenciaBancaria).MaximumLength(120);
    }
}

public sealed class RegistrarPagoACuentaHandler
    : IRequestHandler<RegistrarPagoACuentaCommand, PagoACuentaResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IClock _clock;

    public RegistrarPagoACuentaHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _clock = clock;
    }

    public async Task<PagoACuentaResponse> Handle(
        RegistrarPagoACuentaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el pago a cuenta.");

        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaBancariaId}'.");

        var abierto = await _periodoContable.EstaAbiertoAsync(
            command.FechaValor.Year, command.FechaValor.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        // RN-2: máximo un pago no aplicado abierto por proveedor. El índice
        // parcial único es el backstop ante dobles submit; aquí se valida
        // para devolver un 422 legible.
        if (command.ProveedorId is Guid proveedorId)
        {
            var yaHayAbierto = await _db.MovimientosBancarios.AnyAsync(m =>
                    m.BeneficiarioRef == proveedorId
                    && m.BeneficiarioTipo == BeneficiarioTipo.Proveedor
                    && m.Sentido == SentidoMovimiento.Egreso
                    && m.EstadoAplicacion == EstadoAplicacionMovimiento.NoAplicado
                    && m.ContramovimientoDe == null,
                cancellationToken);
            if (yaHayAbierto)
                throw new BusinessRuleException("PAGO_CUENTA_ABIERTO_EXISTENTE",
                    "El proveedor ya tiene un pago a cuenta abierto; liga el anterior antes de registrar otro (RN-2).");
        }

        var movimiento = MovimientoBancario.RegistrarPagoACuenta(
            empresaId: empresaId,
            cuenta: cuenta,
            proveedorId: command.ProveedorId,
            monto: command.Monto,
            fechaValor: command.FechaValor,
            referenciaBancaria: command.ReferenciaBancaria,
            conceptoId: command.ConceptoId,
            motivo: command.Motivo,
            creadoPor: usuarioId,
            ahora: _clock.UtcNow);

        _db.MovimientosBancarios.Add(movimiento);
        await _db.SaveChangesAsync(cancellationToken);

        return new PagoACuentaResponse(
            movimiento.Id, movimiento.CuentaBancariaId, command.ProveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria, movimiento.MotivoNoAplicado!,
            movimiento.EstadoAplicacion);
    }
}

// --------------------------------------------------- Liga tardía (sin re-desembolso)

public sealed record LigarPagoACuentaCommand(
    Guid MovimientoId,
    Guid FacturaProveedorId,
    decimal Importe) : IRequest<PagoProveedorResponse>;

public sealed class LigarPagoACuentaValidator : AbstractValidator<LigarPagoACuentaCommand>
{
    public LigarPagoACuentaValidator()
    {
        RuleFor(c => c.MovimientoId).NotEmpty();
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
    }
}

public sealed class LigarPagoACuentaHandler
    : IRequestHandler<LigarPagoACuentaCommand, PagoProveedorResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public LigarPagoACuentaHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _publisher = publisher; _clock = clock;
    }

    public async Task<PagoProveedorResponse> Handle(
        LigarPagoACuentaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var movimiento = await _db.MovimientosBancarios
            .FirstOrDefaultAsync(m => m.Id == command.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento '{command.MovimientoId}'.");

        if (movimiento.Sentido != SentidoMovimiento.Egreso || movimiento.MotivoNoAplicado is null)
            throw new BusinessRuleException("LIGA_NO_ES_PAGO_A_CUENTA",
                "Solo se ligan tardíamente movimientos registrados como pago a cuenta.");
        if (movimiento.ContramovimientoDe is not null)
            throw new BusinessRuleException("LIGA_SOBRE_REVERSA",
                "No se liga un contramovimiento.");

        // RN-1: la liga es contra un pasivo autorizado presente en la bandeja.
        var pasivo = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == command.FacturaProveedorId, cancellationToken)
            ?? throw new BusinessRuleException("PAGO_PASIVO_NO_AUTORIZADO",
                $"El pasivo '{command.FacturaProveedorId}' no está en la bandeja de autorizados (RN-1).");

        if (movimiento.BeneficiarioRef is Guid beneficiario && beneficiario != pasivo.ProveedorId)
            throw new BusinessRuleException("LIGA_PROVEEDOR_DISTINTO",
                "El pasivo pertenece a un proveedor distinto al beneficiario del pago a cuenta.");
        if (movimiento.Moneda != pasivo.Moneda)
            throw new BusinessRuleException("PAGO_CROSS_MONEDA",
                $"El movimiento es {movimiento.Moneda} y el pasivo {pasivo.Moneda} (RN-3).");

        var yaLigada = await _db.AplicacionesPagoProveedor.AnyAsync(
            a => a.MovimientoId == movimiento.Id
                 && a.FacturaProveedorId == command.FacturaProveedorId
                 && !a.Revertida,
            cancellationToken);
        if (yaLigada)
            throw new BusinessRuleException("LIGA_DUPLICADA",
                "El movimiento ya está ligado a esa factura.");

        // Sin re-desembolso: el dinero ya salió; solo se aplica.
        var sumaActual = await _db.AplicacionesPagoProveedor
            .Where(a => a.MovimientoId == movimiento.Id && !a.Revertida)
            .SumAsync(a => (decimal?)a.ImporteAplicado, cancellationToken) ?? 0m;

        var ahora = _clock.UtcNow;
        pasivo.AplicarPago(command.Importe);
        movimiento.ActualizarEstadoAplicacion(sumaActual + command.Importe);

        var aplicacion = new AplicacionPagoProveedor(
            movimientoId: movimiento.Id,
            facturaProveedorId: command.FacturaProveedorId,
            proveedorId: pasivo.ProveedorId,
            importeAplicado: command.Importe,
            creadoEn: ahora);
        _db.AplicacionesPagoProveedor.Add(aplicacion);

        // Mismo contrato congelado que el pago directo (RN-4): CxP no
        // distingue una liga tardía de un pago normal.
        await _publisher.PublishAsync(new PagoFacturaProveedorAplicadoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: command.FacturaProveedorId,
            PagoId: aplicacion.Id,
            Monto: command.Importe,
            Moneda: movimiento.Moneda,
            FechaPago: movimiento.FechaValor,
            MetodoPago: pasivo.MetodoPago,
            ReferenciaBancaria: movimiento.ReferenciaBancaria), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new PagoProveedorResponse(
            movimiento.Id, movimiento.CuentaBancariaId, pasivo.ProveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria,
            [new AplicacionPagoResponse(aplicacion.Id, aplicacion.FacturaProveedorId, aplicacion.ImporteAplicado, aplicacion.Revertida)]);
    }
}
