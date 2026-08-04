using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pagos;

// ============================================================================
// TES-PR4: pago a proveedor end-to-end (§3.1). El pasivo llega YA DECIDIDO
// desde CxP (RN-1: solo se paga contra pasivo.autorizado-para-pago
// proyectado en la bandeja); el operador ejecuta la transferencia EN LA
// BANCA (fuera del sistema en MVP) y aquí registra el hecho bancario.
// Un pago cubre N pasivos, pero `aplicado.v1` se emite POR FACTURA (RN-4,
// contrato congelado). La reversa genera contramovimiento (RN-10).
// ============================================================================

public sealed record AplicacionPagoItem(Guid FacturaProveedorId, decimal Importe);

public sealed record AplicacionPagoResponse(
    Guid PagoId,
    Guid FacturaProveedorId,
    decimal Importe,
    bool Revertida);

public sealed record PagoProveedorResponse(
    Guid MovimientoId,
    Guid CuentaBancariaId,
    Guid ProveedorId,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    IReadOnlyList<AplicacionPagoResponse> Aplicaciones);

// --------------------------------------------------- Registrar pago

public sealed record RegistrarPagoProveedorCommand(
    Guid CuentaBancariaId,
    DateOnly FechaValor,
    IReadOnlyList<AplicacionPagoItem> Aplicaciones,
    string? ReferenciaBancaria = null,
    Guid? ConceptoId = null) : IRequest<PagoProveedorResponse>;

public sealed class RegistrarPagoProveedorValidator : AbstractValidator<RegistrarPagoProveedorCommand>
{
    public RegistrarPagoProveedorValidator()
    {
        RuleFor(c => c.CuentaBancariaId).NotEmpty();
        RuleFor(c => c.FechaValor).NotEmpty();
        RuleFor(c => c.ReferenciaBancaria).MaximumLength(120);
        RuleFor(c => c.Aplicaciones).NotEmpty()
            .WithMessage("El pago debe aplicar al menos a un pasivo.");
        RuleForEach(c => c.Aplicaciones).ChildRules(a =>
        {
            a.RuleFor(x => x.FacturaProveedorId).NotEmpty();
            a.RuleFor(x => x.Importe).GreaterThan(0);
        });
        RuleFor(c => c.Aplicaciones)
            .Must(apls => apls.Select(a => a.FacturaProveedorId).Distinct().Count() == apls.Count)
            .WithMessage("No se puede aplicar dos veces a la misma factura en el mismo pago.");
    }
}

public sealed class RegistrarPagoProveedorHandler
    : IRequestHandler<RegistrarPagoProveedorCommand, PagoProveedorResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public RegistrarPagoProveedorHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _publisher = publisher; _clock = clock;
    }

    public async Task<PagoProveedorResponse> Handle(
        RegistrarPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el pago.");

        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaBancariaId}'.");

        // RN-8: candado de período (stub hoy).
        var abierto = await _periodoContable.EstaAbiertoAsync(
            command.FechaValor.Year, command.FechaValor.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        // RN-1: todos los pasivos deben estar en la bandeja (proyección del
        // evento autorizado de CxP).
        var facturaIds = command.Aplicaciones.Select(a => a.FacturaProveedorId).ToList();
        var pasivos = await _db.PasivosPendientesPago
            .Where(p => facturaIds.Contains(p.FacturaProveedorId))
            .ToDictionaryAsync(p => p.FacturaProveedorId, cancellationToken);

        var faltantes = facturaIds.Where(id => !pasivos.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
            throw new BusinessRuleException("PAGO_PASIVO_NO_AUTORIZADO",
                $"Pasivo(s) no presentes en la bandeja de autorizados: {string.Join(", ", faltantes)} (RN-1).");

        // GI-PR3 (doc 12): los pasivos internos SÍ se pagan por este flujo,
        // pero emiten su propio evento (préstamo de viáticos → vuelta a
        // CxP) o ninguno (reposición de caja) — nunca
        // pago-factura-proveedor.aplicado.v1, que apuntaría a una factura
        // inexistente. Un pago no mezcla internos con facturas: son
        // beneficiarios de naturaleza distinta.
        var hayInternos = pasivos.Values.Any(p => p.EsInterno);
        if (hayInternos && pasivos.Values.Any(p => !p.EsInterno))
            throw new BusinessRuleException("PAGO_MEZCLA_INTERNOS",
                "Un pago no puede mezclar pasivos internos (reposición/viáticos) con facturas de proveedor.");

        // Una transferencia = un beneficiario: todos los pasivos del mismo
        // proveedor.
        var proveedores = pasivos.Values.Select(p => p.ProveedorId).Distinct().ToList();
        if (proveedores.Count > 1)
            throw new BusinessRuleException("PAGO_MULTIPROVEEDOR",
                "Un pago cubre pasivos de un solo proveedor; registra un pago por proveedor.");
        var proveedorId = proveedores[0];

        // RN-3: la cuenta de egreso coincide en moneda con los pasivos
        // (cross-moneda bloqueado en MVP; T-G6).
        var monedasDistintas = pasivos.Values
            .Select(p => p.Moneda).Distinct()
            .Where(m => m != cuenta.Moneda)
            .ToList();
        if (monedasDistintas.Count > 0)
            throw new BusinessRuleException("PAGO_CROSS_MONEDA",
                $"La cuenta es {cuenta.Moneda} y hay pasivos en {string.Join(", ", monedasDistintas)} (RN-3; cross-moneda fuera del MVP).");

        var ahora = _clock.UtcNow;
        var monto = command.Aplicaciones.Sum(a => a.Importe);

        var movimiento = MovimientoBancario.RegistrarPagoProveedor(
            empresaId: empresaId,
            cuenta: cuenta,
            proveedorId: proveedorId,
            monto: monto,
            fechaValor: command.FechaValor,
            referenciaBancaria: command.ReferenciaBancaria,
            conceptoId: command.ConceptoId,
            creadoPor: usuarioId,
            ahora: ahora);
        _db.MovimientosBancarios.Add(movimiento);

        var aplicaciones = new List<AplicacionPagoProveedor>(command.Aplicaciones.Count);
        foreach (var item in command.Aplicaciones)
        {
            var pasivo = pasivos[item.FacturaProveedorId];
            // Validación local; la autoridad final es CxP (PAGO_EXCEDE_SALDO).
            pasivo.AplicarPago(item.Importe);

            var aplicacion = new AplicacionPagoProveedor(
                movimientoId: movimiento.Id,
                facturaProveedorId: item.FacturaProveedorId,
                proveedorId: proveedorId,
                importeAplicado: item.Importe,
                creadoEn: ahora);
            aplicaciones.Add(aplicacion);
            _db.AplicacionesPagoProveedor.Add(aplicacion);

            if (pasivo.EsInterno)
            {
                // GI-PR3: préstamo de viáticos → vuelta hacia CxP para
                // transicionar la solicitud a Anticipada. La reposición de
                // caja chica se liquida local (CxP no consume su pago hoy).
                if (pasivo.OrigenTipo == "PrestamoViaticos")
                {
                    await _publisher.PublishAsync(new PagoPrestamoViaticosAplicadoIntegrationEvent(
                        EmpresaId: empresaId,
                        OcurridoEn: ahora,
                        SolicitudViaticosId: pasivo.OrigenId,
                        PagoId: aplicacion.Id,
                        MontoPagado: item.Importe,
                        Moneda: movimiento.Moneda,
                        FechaPago: command.FechaValor,
                        ReferenciaBancaria: movimiento.ReferenciaBancaria), cancellationToken);
                }
            }
            else
            {
                // RN-4: aplicado.v1 POR FACTURA, al outbox en la misma TX
                // (ADR-0009 — el interceptor drena el buffer en SaveChanges).
                await _publisher.PublishAsync(new PagoFacturaProveedorAplicadoIntegrationEvent(
                    EmpresaId: empresaId,
                    OcurridoEn: ahora,
                    FacturaProveedorId: item.FacturaProveedorId,
                    PagoId: aplicacion.Id,
                    Monto: item.Importe,
                    Moneda: movimiento.Moneda,
                    FechaPago: command.FechaValor,
                    MetodoPago: pasivo.MetodoPago,
                    ReferenciaBancaria: movimiento.ReferenciaBancaria), cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new PagoProveedorResponse(
            movimiento.Id, movimiento.CuentaBancariaId, proveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria,
            aplicaciones.Select(a => new AplicacionPagoResponse(
                a.Id, a.FacturaProveedorId, a.ImporteAplicado, a.Revertida)).ToList());
    }
}

// --------------------------------------------------- Revertir pago

public sealed record RevertirPagoProveedorCommand(Guid PagoId, string Motivo)
    : IRequest<PagoProveedorResponse>;

public sealed class RevertirPagoProveedorValidator : AbstractValidator<RevertirPagoProveedorCommand>
{
    public RevertirPagoProveedorValidator()
    {
        RuleFor(c => c.PagoId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RevertirPagoProveedorHandler
    : IRequestHandler<RevertirPagoProveedorCommand, PagoProveedorResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public RevertirPagoProveedorHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _publisher = publisher; _clock = clock;
    }

    public async Task<PagoProveedorResponse> Handle(
        RevertirPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que revierte el pago.");

        var aplicacion = await _db.AplicacionesPagoProveedor
            .FirstOrDefaultAsync(a => a.Id == command.PagoId, cancellationToken)
            ?? throw new EntityNotFoundException("PAGO_NO_ENCONTRADO",
                $"No se encontró el pago '{command.PagoId}'.");

        var movimiento = await _db.MovimientosBancarios
            .FirstOrDefaultAsync(m => m.Id == aplicacion.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento del pago '{command.PagoId}'.");

        var ahora = _clock.UtcNow;
        var fechaReversa = DateOnly.FromDateTime(ahora.UtcDateTime);

        // RN-8 sobre la fecha del contramovimiento.
        var abierto = await _periodoContable.EstaAbiertoAsync(
            fechaReversa.Year, fechaReversa.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {fechaReversa.Year}/{fechaReversa.Month:00} está cerrado.");

        // RN-10: marca + contramovimiento; nada se borra.
        aplicacion.Revertir();
        var contramovimiento = movimiento.CrearContramovimiento(
            importe: aplicacion.ImporteAplicado,
            fechaValor: fechaReversa,
            creadoPor: usuarioId,
            ahora: ahora);
        _db.MovimientosBancarios.Add(contramovimiento);

        // El pasivo regresa a la bandeja con el saldo restaurado (si CxP
        // re-emite el autorizado, la re-proyección lo corrige).
        var pasivo = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == aplicacion.FacturaProveedorId, cancellationToken);

        // GI-PR3: la reversa de un pago interno no tiene contraparte en
        // CxP (el préstamo ya transicionó la solicitud a Anticipada y no
        // existe la transición inversa). Se bloquea hasta modelarla.
        if (pasivo?.EsInterno == true)
            throw new BusinessRuleException("REVERSA_PASIVO_INTERNO",
                "La reversa de pagos de pasivos internos (reposición/viáticos) no está soportada; corrígelo en el módulo de origen.");

        pasivo?.RevertirPago(aplicacion.ImporteAplicado);

        await _publisher.PublishAsync(new PagoFacturaProveedorRevertidoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: aplicacion.FacturaProveedorId,
            PagoOriginalId: aplicacion.Id,
            MontoRevertido: aplicacion.ImporteAplicado,
            Moneda: movimiento.Moneda,
            FechaReversa: fechaReversa,
            Motivo: command.Motivo.Trim()), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new PagoProveedorResponse(
            movimiento.Id, movimiento.CuentaBancariaId, aplicacion.ProveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria,
            [new AplicacionPagoResponse(aplicacion.Id, aplicacion.FacturaProveedorId, aplicacion.ImporteAplicado, aplicacion.Revertida)]);
    }
}

// --------------------------------------------------- Solicitar cancelación de pasivo

public sealed record SolicitarCancelacionPasivoCommand(Guid FacturaProveedorId, string Motivo)
    : IRequest;

public sealed class SolicitarCancelacionPasivoValidator : AbstractValidator<SolicitarCancelacionPasivoCommand>
{
    public SolicitarCancelacionPasivoValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class SolicitarCancelacionPasivoHandler : IRequestHandler<SolicitarCancelacionPasivoCommand>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public SolicitarCancelacionPasivoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _publisher = publisher; _clock = clock;
    }

    public async Task Handle(SolicitarCancelacionPasivoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario solicitante.");

        var pasivo = await _db.PasivosPendientesPago
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == command.FacturaProveedorId, cancellationToken);
        if (pasivo is null)
            throw new EntityNotFoundException("PASIVO_NO_ENCONTRADO",
                $"El pasivo '{command.FacturaProveedorId}' no está en la bandeja.");

        // GI-PR2: la cancelación de un pasivo interno no viaja a CxP como
        // solicitud de factura — se resuelve en su módulo de origen.
        if (pasivo.EsInterno)
            throw new BusinessRuleException("PASIVO_INTERNO_NO_CANCELABLE",
                "Los pasivos internos no se cancelan desde Tesorería; se resuelven en CxP (comprobación/solicitud de origen).");

        // Sin mutación local: CxP decide (EnviarARevision); su resolución
        // regresa por el flujo normal de eventos.
        await _publisher.PublishAsync(new CancelacionPasivoSolicitadaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: _clock.UtcNow,
            FacturaProveedorId: command.FacturaProveedorId,
            UsuarioSolicitanteId: usuarioId,
            Motivo: command.Motivo.Trim()), cancellationToken);

        // SaveChanges drena el buffer del publisher a la outbox (ADR-0009)
        // aunque no haya cambios de entidades.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
