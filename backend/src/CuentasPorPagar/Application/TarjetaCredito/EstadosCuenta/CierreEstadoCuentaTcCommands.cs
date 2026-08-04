using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Domain.TarjetaCredito.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;

// ============================================================================
// F7-PR6: cierre del ciclo de TC empresarial.
// 1. ConfirmarMatchLineaBanco: Auxiliar confirma una sugerencia 60-89.
// 2. CapturarMovimientoDesdeLinea: D13 — captura retroactiva.
// 3. RegistrarRefund: Flujo E §5.5.
// 4. RegistrarMovimientoEspecial: Interés, Anualidad, Comisión.
// 5. MarcarEstadoCuentaConciliado: EnConciliacion → Conciliado.
// 6. CerrarEstadoCuentaTc: Conciliado → Cerrado + genera FacturaProveedor.
// 7. RegistrarPagoBancoEstadoCuentaTc: Cerrado → PagadoBanco.
// 8. DisputarMovimientoTc / ResolverDisputaMovimientoTc (§8.5).
// ============================================================================

// ---------------------------------------- 1. Confirmar match

public sealed record ConfirmarMatchLineaBancoCommand(
    Guid EstadoCuentaTcId,
    int VersionEsperadaEc,
    Guid LineaBancoId,
    Guid MovimientoTcId,
    decimal? DiferenciaCambiariaMxn) : IRequest<EstadoCuentaTcResponse>;

public sealed class ConfirmarMatchLineaBancoValidator : AbstractValidator<ConfirmarMatchLineaBancoCommand>
{
    public ConfirmarMatchLineaBancoValidator()
    {
        RuleFor(c => c.EstadoCuentaTcId).NotEmpty();
        RuleFor(c => c.LineaBancoId).NotEmpty();
        RuleFor(c => c.MovimientoTcId).NotEmpty();
    }
}

public sealed class ConfirmarMatchLineaBancoHandler
    : IRequestHandler<ConfirmarMatchLineaBancoCommand, EstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ConfirmarMatchLineaBancoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<EstadoCuentaTcResponse> Handle(
        ConfirmarMatchLineaBancoCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.EstadoCuentaTcId, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.EstadoCuentaTcId}'.");
        if (ec.Version != command.VersionEsperadaEc)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        var linea = ec.Lineas.FirstOrDefault(l => l.Id == command.LineaBancoId)
            ?? throw new EntityNotFoundException("LIN_NO_ENCONTRADA",
                $"No se encontró la línea '{command.LineaBancoId}' en el estado de cuenta.");

        if (linea.EstadoMatch != EstadoMatchLineaBanco.Pendiente)
        {
            throw new BusinessRuleException(
                "LIN_NO_CONFIRMABLE",
                $"Solo líneas en estado Pendiente se confirman manualmente (actual: {linea.EstadoMatch}).");
        }

        var mov = await _db.MovimientosTarjetaCredito
            .FirstOrDefaultAsync(m => m.Id == command.MovimientoTcId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento '{command.MovimientoTcId}'.");

        // Confirmación manual: score 100 (humano decidió).
        linea.MarcarMatched(mov.Id, score: 100m);
        mov.MarcarConciliadoConEstadoCuenta(ec.Id, linea.Id, linea.FechaAplicacion);

        if (command.DiferenciaCambiariaMxn is decimal dc && dc != 0m)
        {
            ec.RegistrarDiferenciaCambiaria(dc);
        }

        // Recalcular total conciliado.
        var totalConciliado = ec.Lineas
            .Where(l => l.EstadoMatch == EstadoMatchLineaBanco.Matched
                        || l.EstadoMatch == EstadoMatchLineaBanco.CapturaRetroactiva)
            .Sum(l => l.MontoMxn);
        ec.ActualizarTotalConciliado(totalConciliado);

        await _db.SaveChangesAsync(cancellationToken);
        return CrearEstadoCuentaTcHandler.ToResponse(ec);
    }
}

// ---------------------------------------- 2. Captura retroactiva desde línea

public sealed record CapturarMovimientoDesdeLineaCommand(
    Guid EstadoCuentaTcId,
    int VersionEsperadaEc,
    Guid LineaBancoId,
    Guid UsuarioQueUsoId,
    string ConceptoContable) : IRequest<EstadoCuentaTcResponse>;

public sealed class CapturarMovimientoDesdeLineaValidator : AbstractValidator<CapturarMovimientoDesdeLineaCommand>
{
    public CapturarMovimientoDesdeLineaValidator()
    {
        RuleFor(c => c.EstadoCuentaTcId).NotEmpty();
        RuleFor(c => c.LineaBancoId).NotEmpty();
        RuleFor(c => c.UsuarioQueUsoId).NotEmpty();
        RuleFor(c => c.ConceptoContable).NotEmpty().MaximumLength(120);
    }
}

public sealed class CapturarMovimientoDesdeLineaHandler
    : IRequestHandler<CapturarMovimientoDesdeLineaCommand, EstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public CapturarMovimientoDesdeLineaHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<EstadoCuentaTcResponse> Handle(
        CapturarMovimientoDesdeLineaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.EstadoCuentaTcId, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.EstadoCuentaTcId}'.");
        if (ec.Version != command.VersionEsperadaEc)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        var linea = ec.Lineas.FirstOrDefault(l => l.Id == command.LineaBancoId)
            ?? throw new EntityNotFoundException("LIN_NO_ENCONTRADA",
                $"No se encontró la línea '{command.LineaBancoId}'.");
        if (linea.EstadoMatch != EstadoMatchLineaBanco.NoConciliado)
        {
            throw new BusinessRuleException(
                "LIN_NO_CAPTURABLE",
                $"Solo líneas NoConciliado pueden capturarse retroactivamente (actual: {linea.EstadoMatch}).");
        }

        var tarjeta = await _db.TarjetasCredito
            .Include(t => t.UsuariosAutorizados)
            .FirstOrDefaultAsync(t => t.Id == ec.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{ec.TarjetaId}'.");

        // Captura retroactiva con tipo CompraSinCfdi (el operador
        // confirma el cargo aunque no haya factura). Después puede
        // reclasificarse si encuentra el CFDI.
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: empresaId,
            tarjeta: tarjeta,
            usuarioQueUsoId: command.UsuarioQueUsoId,
            fechaMovimiento: linea.FechaAplicacion,
            montoOriginal: Math.Abs(linea.Monto),
            monedaOriginal: linea.Moneda,
            tipoCambioCaptura: null,
            merchantRaw: linea.MerchantRaw,
            descripcionLibre: $"Captura retroactiva desde línea {linea.PosicionArchivo}",
            conceptoContable: command.ConceptoContable,
            ticketBlobRef: null);
        mov.MarcarCapturaRetroactiva();
        mov.MarcarConciliadoConEstadoCuenta(ec.Id, linea.Id, linea.FechaAplicacion);
        _db.MovimientosTarjetaCredito.Add(mov);

        linea.MarcarMatched(mov.Id, score: 100m);

        var totalConciliado = ec.Lineas
            .Where(l => l.EstadoMatch == EstadoMatchLineaBanco.Matched)
            .Sum(l => l.MontoMxn);
        ec.ActualizarTotalConciliado(totalConciliado);

        await _db.SaveChangesAsync(cancellationToken);
        return CrearEstadoCuentaTcHandler.ToResponse(ec);
    }
}

// ---------------------------------------- 3. Registrar Refund

public sealed record RegistrarRefundTcCommand(
    Guid TarjetaId,
    Guid UsuarioQueUsoId,
    DateOnly FechaMovimiento,
    decimal MontoOriginal,
    string MonedaOriginal,
    decimal? TipoCambioCaptura,
    string MerchantRaw,
    string ConceptoContable,
    Guid MovimientoOriginalId) : IRequest<MovimientoRefundResponse>;

public sealed record MovimientoRefundResponse(Guid Id, Guid MovimientoOriginalId, decimal MontoMxn, int Version);

public sealed class RegistrarRefundTcValidator : AbstractValidator<RegistrarRefundTcCommand>
{
    public RegistrarRefundTcValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.MovimientoOriginalId).NotEmpty();
        RuleFor(c => c.MontoOriginal).GreaterThan(0);
        RuleFor(c => c.MonedaOriginal).NotEmpty().Length(3);
        RuleFor(c => c.MerchantRaw).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ConceptoContable).NotEmpty().MaximumLength(120);
    }
}

public sealed class RegistrarRefundTcHandler : IRequestHandler<RegistrarRefundTcCommand, MovimientoRefundResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public RegistrarRefundTcHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<MovimientoRefundResponse> Handle(
        RegistrarRefundTcCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var tarjeta = await _db.TarjetasCredito
            .Include(t => t.UsuariosAutorizados)
            .FirstOrDefaultAsync(t => t.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");

        var movOriginal = await _db.MovimientosTarjetaCredito
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MovimientoOriginalId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_MOV_ORIGINAL_NO_ENCONTRADO",
                $"No se encontró el movimiento original '{command.MovimientoOriginalId}'.");

        if (movOriginal.TarjetaId != command.TarjetaId)
        {
            throw new BusinessRuleException(
                "TC_REFUND_TARJETA_MISMATCH",
                "El movimiento original no pertenece a la misma tarjeta del refund.");
        }

        if (command.MontoOriginal > Math.Abs(movOriginal.MontoOriginal))
        {
            throw new BusinessRuleException(
                "TC_REFUND_EXCEDE_ORIGINAL",
                $"El monto del refund ({command.MontoOriginal}) excede el monto original ({movOriginal.MontoOriginal}).");
        }

        var refund = MovimientoTarjetaCredito.CapturarRefund(
            empresaId: empresaId,
            tarjeta: tarjeta,
            usuarioQueUsoId: command.UsuarioQueUsoId,
            fechaMovimiento: command.FechaMovimiento,
            montoOriginal: command.MontoOriginal,
            monedaOriginal: command.MonedaOriginal,
            tipoCambioCaptura: command.TipoCambioCaptura,
            merchantRaw: command.MerchantRaw,
            conceptoContable: command.ConceptoContable,
            movimientoOriginalId: command.MovimientoOriginalId);

        _db.MovimientosTarjetaCredito.Add(refund);
        await _db.SaveChangesAsync(cancellationToken);

        return new MovimientoRefundResponse(refund.Id, command.MovimientoOriginalId, refund.MontoMxn, refund.Version);
    }
}

// ---------------------------------------- 4. Registrar movimiento especial

public sealed record RegistrarMovimientoEspecialTcCommand(
    Guid TarjetaId,
    Guid UsuarioQueUsoId,
    DateOnly FechaMovimiento,
    TipoMovimientoTc Tipo,
    decimal MontoOriginal,
    string MonedaOriginal,
    decimal? TipoCambioCaptura,
    string MerchantRaw,
    string ConceptoContable) : IRequest<MovimientoEspecialResponse>;

public sealed record MovimientoEspecialResponse(Guid Id, TipoMovimientoTc Tipo, decimal MontoMxn, int Version);

public sealed class RegistrarMovimientoEspecialTcValidator : AbstractValidator<RegistrarMovimientoEspecialTcCommand>
{
    public RegistrarMovimientoEspecialTcValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.MontoOriginal).GreaterThan(0);
        RuleFor(c => c.MonedaOriginal).NotEmpty().Length(3);
        RuleFor(c => c.MerchantRaw).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ConceptoContable).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Tipo).Must(t =>
            t is TipoMovimientoTc.GastoFinanciero
                or TipoMovimientoTc.Anualidad
                or TipoMovimientoTc.ComisionDivisa)
            .WithMessage("Tipo debe ser GastoFinanciero, Anualidad o ComisionDivisa.");
    }
}

public sealed class RegistrarMovimientoEspecialTcHandler
    : IRequestHandler<RegistrarMovimientoEspecialTcCommand, MovimientoEspecialResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public RegistrarMovimientoEspecialTcHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<MovimientoEspecialResponse> Handle(
        RegistrarMovimientoEspecialTcCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var tarjeta = await _db.TarjetasCredito
            .Include(t => t.UsuariosAutorizados)
            .FirstOrDefaultAsync(t => t.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");

        var mov = MovimientoTarjetaCredito.CapturarMovimientoEspecial(
            empresaId: empresaId,
            tarjeta: tarjeta,
            usuarioQueUsoId: command.UsuarioQueUsoId,
            fechaMovimiento: command.FechaMovimiento,
            tipo: command.Tipo,
            montoOriginal: command.MontoOriginal,
            monedaOriginal: command.MonedaOriginal,
            tipoCambioCaptura: command.TipoCambioCaptura,
            merchantRaw: command.MerchantRaw,
            conceptoContable: command.ConceptoContable);

        _db.MovimientosTarjetaCredito.Add(mov);
        await _db.SaveChangesAsync(cancellationToken);
        return new MovimientoEspecialResponse(mov.Id, mov.Tipo, mov.MontoMxn, mov.Version);
    }
}

// ---------------------------------------- 5. Marcar Conciliado

public sealed record MarcarEstadoCuentaConciliadoCommand(Guid Id, int VersionEsperada)
    : IRequest<EstadoCuentaTcResponse>;

public sealed class MarcarEstadoCuentaConciliadoValidator : AbstractValidator<MarcarEstadoCuentaConciliadoCommand>
{
    public MarcarEstadoCuentaConciliadoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
    }
}

public sealed class MarcarEstadoCuentaConciliadoHandler
    : IRequestHandler<MarcarEstadoCuentaConciliadoCommand, EstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public MarcarEstadoCuentaConciliadoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<EstadoCuentaTcResponse> Handle(
        MarcarEstadoCuentaConciliadoCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.Id}'.");
        if (ec.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        ec.MarcarConciliado();
        await _db.SaveChangesAsync(cancellationToken);
        return CrearEstadoCuentaTcHandler.ToResponse(ec);
    }
}

// ---------------------------------------- 6. Cerrar (genera FacturaProveedor)

public sealed record CerrarEstadoCuentaTcCommand(Guid Id, int VersionEsperada)
    : IRequest<CerrarEstadoCuentaTcResponse>;

public sealed record CerrarEstadoCuentaTcResponse(
    Guid Id,
    EstadoCuentaTcStatus Estado,
    Guid FacturaProveedorId,
    decimal TotalBancoMxn,
    int Version);

public sealed class CerrarEstadoCuentaTcValidator : AbstractValidator<CerrarEstadoCuentaTcCommand>
{
    public CerrarEstadoCuentaTcValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
    }
}

public sealed class CerrarEstadoCuentaTcHandler
    : IRequestHandler<CerrarEstadoCuentaTcCommand, CerrarEstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CerrarEstadoCuentaTcHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser,
        IMediator mediator, IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<CerrarEstadoCuentaTcResponse> Handle(
        CerrarEstadoCuentaTcCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.Id}'.");
        if (ec.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        var tarjeta = await _db.TarjetasCredito
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ec.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{ec.TarjetaId}'.");

        // Filtrar movimientos en disputa del total que se factura al banco.
        var totalSinDisputas = await _db.MovimientosTarjetaCredito
            .Where(m => m.EstadoCuentaTcId == ec.Id && !m.EnDisputa)
            .SumAsync(m => (decimal?)m.MontoMxn, cancellationToken) ?? 0m;

        var totalFactura = totalSinDisputas > 0 ? totalSinDisputas : (ec.TotalBancoMxn ?? 0m);
        if (totalFactura <= 0)
        {
            throw new BusinessRuleException(
                "EC_TOTAL_FACTURA_INVALIDO",
                "El total a facturar al banco debe ser > 0.");
        }

        var ahora = _clock.UtcNow;
        var fechaCorte = new DateTimeOffset(ec.FechaCorte.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Generar FacturaProveedor agregada contra el banco (§5.4).
        var factura = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            empresaId: ec.EmpresaId,
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: tarjeta.BancoProveedorId,
            sucursalId: Guid.Empty, // TC no tiene sucursal específica
            folioProveedor: null,
            serieProveedor: null,
            fechaDocumento: fechaCorte,
            fechaContabilizacion: fechaCorte,
            fechaVencimiento: ec.FechaLimitePago,
            moneda: "MXN",
            tipoCambio: null,
            subtotal: totalFactura,
            descuentos: 0m,
            impuestosTrasladados: 0m,
            retenciones: 0m,
            total: totalFactura,
            motivoCaptura: $"TC — estado de cuenta {ec.Id} cerrado",
            ahora: ahora);
        _db.FacturasProveedor.Add(factura);

        ec.Cerrar(facturaProveedorId: factura.Id);

        await _mediator.Publish(new EstadoCuentaTcCerradoDomainEvent(
            EmpresaId: ec.EmpresaId,
            EstadoCuentaTcId: ec.Id,
            TarjetaId: ec.TarjetaId,
            TotalBancoMxn: totalFactura,
            FacturaProveedorId: factura.Id,
            DiferenciaCambiariaMxn: ec.DiferenciaCambiariaMxn,
            OcurridoEn: ahora), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new CerrarEstadoCuentaTcResponse(
            Id: ec.Id,
            Estado: ec.Estado,
            FacturaProveedorId: factura.Id,
            TotalBancoMxn: totalFactura,
            Version: ec.Version);
    }
}

// ---------------------------------------- 7. Marcar PagadoBanco

public sealed record MarcarEstadoCuentaTcPagadoBancoCommand(Guid Id, int VersionEsperada)
    : IRequest<EstadoCuentaTcResponse>;

public sealed class MarcarPagadoBancoValidator : AbstractValidator<MarcarEstadoCuentaTcPagadoBancoCommand>
{
    public MarcarPagadoBancoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
    }
}

public sealed class MarcarEstadoCuentaTcPagadoBancoHandler
    : IRequestHandler<MarcarEstadoCuentaTcPagadoBancoCommand, EstadoCuentaTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public MarcarEstadoCuentaTcPagadoBancoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<EstadoCuentaTcResponse> Handle(
        MarcarEstadoCuentaTcPagadoBancoCommand command, CancellationToken cancellationToken)
    {
        var ec = await _db.EstadosCuentaTc
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EC_NO_ENCONTRADO",
                $"No se encontró el estado de cuenta '{command.Id}'.");
        if (ec.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(EstadoCuentaTc), ec.Id);

        ec.MarcarPagadoBanco();

        // Batch update de movimientos asociados: pasan a PagadoAlBanco.
        var movimientos = await _db.MovimientosTarjetaCredito
            .Where(m => m.EstadoCuentaTcId == ec.Id)
            .ToListAsync(cancellationToken);
        foreach (var mov in movimientos)
        {
            mov.MarcarPagadoAlBanco();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return CrearEstadoCuentaTcHandler.ToResponse(ec);
    }
}
