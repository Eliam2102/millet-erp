using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.TarjetaCredito.Movimientos;

// ============================================================================
// F7-PR4: captura de movimientos de TC empresarial.
// Flujo A — Compra con CFDI (§5.1): genera FacturaProveedor en Pagada.
// Flujo B — Compra sin CFDI (§5.2): solo movimiento; sin factura.
// ============================================================================

public sealed record MovimientoTcResponse(
    Guid Id,
    Guid TarjetaId,
    Guid UsuarioQueUsoId,
    DateOnly FechaMovimiento,
    TipoMovimientoTc Tipo,
    EstadoMovimientoTc Estado,
    decimal MontoOriginal,
    string MonedaOriginal,
    decimal? TipoCambioCaptura,
    decimal MontoMxn,
    string MerchantNormalizado,
    Guid? FacturaProveedorId,
    string ConceptoContable,
    int Version);

internal static class MovimientoTcMapper
{
    public static MovimientoTcResponse ToResponse(MovimientoTarjetaCredito m) =>
        new(m.Id, m.TarjetaId, m.UsuarioQueUsoId, m.FechaMovimiento,
            m.Tipo, m.Estado, m.MontoOriginal, m.MonedaOriginal,
            m.TipoCambioCaptura, m.MontoMxn, m.MerchantNormalizado,
            m.FacturaProveedorId, m.ConceptoContable, m.Version);
}

// --------------------------------------------------- Flujo A: con CFDI

public sealed record RegistrarMovimientoTcConCfdiCommand(
    Guid TarjetaId,
    Guid UsuarioQueUsoId,
    DateOnly FechaMovimiento,
    decimal MontoOriginal,
    string MonedaOriginal,
    decimal? TipoCambioCaptura,
    string MerchantRaw,
    string? DescripcionLibre,
    string ConceptoContable,
    // CFDI ya capturado en CxP por canal estándar
    Guid CfdiRecibidoId,
    Guid ProveedorId,
    string? UuidCfdi,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    decimal Subtotal,
    decimal Descuentos,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal TotalFactura,
    DateOnly FechaVencimiento) : IRequest<MovimientoTcResponse>;

public sealed class RegistrarMovimientoTcConCfdiValidator : AbstractValidator<RegistrarMovimientoTcConCfdiCommand>
{
    public RegistrarMovimientoTcConCfdiValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.UsuarioQueUsoId).NotEmpty();
        RuleFor(c => c.CfdiRecibidoId).NotEmpty();
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.MontoOriginal).GreaterThan(0);
        RuleFor(c => c.MonedaOriginal).NotEmpty().Length(3);
        RuleFor(c => c.MerchantRaw).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ConceptoContable).NotEmpty().MaximumLength(120);
        RuleFor(c => c.TotalFactura).GreaterThan(0);
    }
}

/// <summary>
/// Flujo A (§5.1). El handler:
/// 1. Carga la tarjeta y valida estado, vigencia, autorización del usuario.
/// 2. Si <c>UuidCfdi</c> viene, valida que no esté ya capturado como factura.
/// 3. Genera <c>FacturaProveedor</c> sin OC y la transiciona a Pagada
///    (porque la TC ya pagó).
/// 4. Crea <c>MovimientoTarjetaCredito</c> con tipo=CompraConCfdi vinculando
///    el CFDI + factura + proveedor.
/// </summary>
public sealed class RegistrarMovimientoTcConCfdiHandler
    : IRequestHandler<RegistrarMovimientoTcConCfdiCommand, MovimientoTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RegistrarMovimientoTcConCfdiHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clock = clock;
    }

    public async Task<MovimientoTcResponse> Handle(
        RegistrarMovimientoTcConCfdiCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var tarjeta = await _db.TarjetasCredito
            .Include(t => t.UsuariosAutorizados)
            .FirstOrDefaultAsync(t => t.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");

        // Resolución y consumo del CFDI vinculado — mismo patrón #622 que
        // caja chica/viáticos (TC quedó fuera y lo detectó la verificación
        // e2e P7: el mismo CFDI se registraba dos veces y quedaba
        // PorProcesar para siempre). Existencia + PorProcesar + coherencia
        // de UUID; el UUID efectivo sale del CFDI cuando no viene explícito.
        var cfdi = await _db.CfdisRecibidos
            .FirstOrDefaultAsync(c => c.Id == command.CfdiRecibidoId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_CFDI_NO_ENCONTRADO",
                $"No se encontró el CFDI recibido '{command.CfdiRecibidoId}'.");
        if (cfdi.Estado != Domain.Cfdi.EstadoCfdiRecibido.PorProcesar)
        {
            throw new BusinessRuleException("TC_CFDI_YA_PROCESADO",
                $"El CFDI {cfdi.UuidCfdi.Valor} ya no está PorProcesar (estado actual: {cfdi.Estado}).");
        }
        var uuidNorm = (command.UuidCfdi ?? cfdi.UuidCfdi.Valor).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(command.UuidCfdi)
            && !string.Equals(uuidNorm, cfdi.UuidCfdi.Valor, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("TC_CFDI_UUID_NO_COINCIDE",
                $"El UUID del movimiento ({command.UuidCfdi}) no coincide con el del CFDI vinculado ({cfdi.UuidCfdi.Valor}).");
        }

        // Duplicidad por UUID efectivo (las Canceladas no cuentan, P7-H1).
        var yaCapturado = await _db.FacturasProveedor.AsNoTracking()
            .AnyAsync(f => f.UuidCfdi == uuidNorm
                && f.Estado != Domain.FacturaProveedor.EstadoPasivo.Cancelada, cancellationToken);
        if (yaCapturado)
            throw new BusinessRuleException("TC_MOV_FACTURA_DUPLICADA",
                $"El CFDI {uuidNorm} ya está capturado como factura.");

        var ahora = _clock.UtcNow;

        var factura = global::Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            empresaId: empresaId,
            cfdiRecibidoId: command.CfdiRecibidoId,
            uuidCfdi: uuidNorm,
            proveedorId: command.ProveedorId,
            sucursalId: Guid.Empty, // TC no tiene sucursal específica
            folioProveedor: command.FolioProveedor,
            serieProveedor: command.SerieProveedor,
            fechaDocumento: command.FechaCfdi,
            fechaContabilizacion: command.FechaCfdi,
            fechaVencimiento: command.FechaVencimiento,
            moneda: command.MonedaOriginal,
            tipoCambio: command.TipoCambioCaptura,
            subtotal: command.Subtotal,
            descuentos: command.Descuentos,
            impuestosTrasladados: command.ImpuestosTrasladados,
            retenciones: command.Retenciones,
            total: command.TotalFactura,
            motivoCaptura: $"TC — cargo con CFDI en tarjeta {tarjeta.NombreAlias}",
            ahora: ahora);

        // El CFDI queda consumido apuntando a la factura (#622 para TC) y
        // su MetodoPago se copia antes de la transición.
        cfdi.MarcarConvertidoEnPasivo(factura.Id);
        factura.AsignarMetodoPago(cfdi.MetodoPago);

        // La deuda con el proveedor se considera saldada: la TC ya pagó.
        // La autorización + el ImportePagado se manejan vía la transición.
        factura.Autorizar(_currentUser.UserId, ahora);
        factura.RegistrarPago(command.TotalFactura, ahora, $"Pago TC tarjeta {tarjeta.NombreAlias}");

        _db.FacturasProveedor.Add(factura);

        var mov = MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: empresaId,
            tarjeta: tarjeta,
            usuarioQueUsoId: command.UsuarioQueUsoId,
            fechaMovimiento: command.FechaMovimiento,
            montoOriginal: command.MontoOriginal,
            monedaOriginal: command.MonedaOriginal,
            tipoCambioCaptura: command.TipoCambioCaptura,
            merchantRaw: command.MerchantRaw,
            descripcionLibre: command.DescripcionLibre,
            cfdiRecibidoId: command.CfdiRecibidoId,
            facturaProveedorId: factura.Id,
            proveedorId: command.ProveedorId,
            conceptoContable: command.ConceptoContable);

        _db.MovimientosTarjetaCredito.Add(mov);
        await _db.SaveChangesAsync(cancellationToken);

        return MovimientoTcMapper.ToResponse(mov);
    }
}

// --------------------------------------------------- Flujo B: sin CFDI

public sealed record RegistrarMovimientoTcSinCfdiCommand(
    Guid TarjetaId,
    Guid UsuarioQueUsoId,
    DateOnly FechaMovimiento,
    decimal MontoOriginal,
    string MonedaOriginal,
    decimal? TipoCambioCaptura,
    string MerchantRaw,
    string? DescripcionLibre,
    string ConceptoContable,
    string? TicketBlobRef) : IRequest<MovimientoTcResponse>;

public sealed class RegistrarMovimientoTcSinCfdiValidator : AbstractValidator<RegistrarMovimientoTcSinCfdiCommand>
{
    public RegistrarMovimientoTcSinCfdiValidator()
    {
        RuleFor(c => c.TarjetaId).NotEmpty();
        RuleFor(c => c.UsuarioQueUsoId).NotEmpty();
        RuleFor(c => c.MontoOriginal).GreaterThan(0);
        RuleFor(c => c.MonedaOriginal).NotEmpty().Length(3);
        RuleFor(c => c.MerchantRaw).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ConceptoContable).NotEmpty().MaximumLength(120);
    }
}

public sealed class RegistrarMovimientoTcSinCfdiHandler
    : IRequestHandler<RegistrarMovimientoTcSinCfdiCommand, MovimientoTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public RegistrarMovimientoTcSinCfdiHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<MovimientoTcResponse> Handle(
        RegistrarMovimientoTcSinCfdiCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        var tarjeta = await _db.TarjetasCredito
            .Include(t => t.UsuariosAutorizados)
            .FirstOrDefaultAsync(t => t.Id == command.TarjetaId, cancellationToken)
            ?? throw new EntityNotFoundException("TC_NO_ENCONTRADA",
                $"No se encontró la tarjeta '{command.TarjetaId}'.");

        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: empresaId,
            tarjeta: tarjeta,
            usuarioQueUsoId: command.UsuarioQueUsoId,
            fechaMovimiento: command.FechaMovimiento,
            montoOriginal: command.MontoOriginal,
            monedaOriginal: command.MonedaOriginal,
            tipoCambioCaptura: command.TipoCambioCaptura,
            merchantRaw: command.MerchantRaw,
            descripcionLibre: command.DescripcionLibre,
            conceptoContable: command.ConceptoContable,
            ticketBlobRef: command.TicketBlobRef);

        _db.MovimientosTarjetaCredito.Add(mov);
        await _db.SaveChangesAsync(cancellationToken);
        return MovimientoTcMapper.ToResponse(mov);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarMovimientosTcQuery(
    Guid? TarjetaId = null,
    Guid? UsuarioQueUsoId = null,
    EstadoMovimientoTc? Estado = null,
    TipoMovimientoTc? Tipo = null,
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<MovimientoTcResponse>>;

public sealed class ListarMovimientosTcHandler
    : IRequestHandler<ListarMovimientosTcQuery, PagedResponse<MovimientoTcResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    public ListarMovimientosTcHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<MovimientoTcResponse>> Handle(
        ListarMovimientosTcQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.MovimientosTarjetaCredito.AsNoTracking();
        if (query.TarjetaId is Guid t) q = q.Where(m => m.TarjetaId == t);
        if (query.UsuarioQueUsoId is Guid u) q = q.Where(m => m.UsuarioQueUsoId == u);
        if (query.Estado is EstadoMovimientoTc e) q = q.Where(m => m.Estado == e);
        if (query.Tipo is TipoMovimientoTc ti) q = q.Where(m => m.Tipo == ti);
        if (query.FechaDesde is DateOnly d) q = q.Where(m => m.FechaMovimiento >= d);
        if (query.FechaHasta is DateOnly h) q = q.Where(m => m.FechaMovimiento <= h);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(m => m.FechaMovimiento).ThenBy(m => m.Id)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<MovimientoTcResponse>(
            items.Select(MovimientoTcMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}
