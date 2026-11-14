using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pagos;

// M-326 parte 2 (b): redondeo de hasta $0.99 (±) al pagar una factura de proveedor. Se solicita antes de registrar el pago, lo autoriza
// el Gerente de Área (distinto de quien solicita) y el pago lo consume una sola vez (RegistrarPagoProveedorHandler).

public sealed record RedondeoPagoResponse(
    Guid Id, Guid FacturaProveedorId, decimal Redondeo, string Estado, string Motivo, Guid SolicitadoPor, Guid? ResueltoPor, string? MotivoRechazo)
{
    public static RedondeoPagoResponse De(AutorizacionRedondeoPago a) =>
        new(a.Id, a.FacturaProveedorId, a.Redondeo, a.Estado.ToString(), a.Motivo, a.SolicitadoPor, a.ResueltoPor, a.MotivoRechazo);
}

public sealed record SolicitarRedondeoPagoCommand(Guid FacturaProveedorId, decimal Redondeo, string Motivo) : IRequest<RedondeoPagoResponse>;

public sealed class SolicitarRedondeoPagoValidator : AbstractValidator<SolicitarRedondeoPagoCommand>
{
    public SolicitarRedondeoPagoValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Redondeo).Must(r => r != 0 && Math.Abs(r) <= AutorizacionRedondeoPago.Tolerancia && decimal.Round(r, 2) == r)
            .WithMessage("El redondeo es distinto de cero, de máximo $0.99 (±) y de dos decimales.");
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class SolicitarRedondeoPagoHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<SolicitarRedondeoPagoCommand, RedondeoPagoResponse>
{
    public async Task<RedondeoPagoResponse> Handle(SolicitarRedondeoPagoCommand request, CancellationToken cancellationToken)
    {
        var c = request; var ct = cancellationToken;
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (usuario.UserId is not Guid usuarioId) throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario solicitante.");
        var pasivo = await db.PasivosPendientesPago.FirstOrDefaultAsync(p => p.FacturaProveedorId == c.FacturaProveedorId, ct)
            ?? throw new EntityNotFoundException("PASIVO_NO_ENCONTRADO", $"El pasivo '{c.FacturaProveedorId}' no está en la bandeja.");
        if (pasivo.EsInterno) throw new BusinessRuleException("RPP_PASIVO_INTERNO", "El redondeo aplica solo a facturas de proveedor.");
        if (pasivo.Moneda != "MXN")
            throw new BusinessRuleException("RPP_SOLO_MXN", "El redondeo de hasta $0.99 aplica a pasivos en MXN; en otra moneda se paga el saldo exacto.");
        // La diferencia solo puede cerrar el saldo: nunca dejar saldo negativo por redondeo.
        if (pasivo.SaldoPendiente - c.Redondeo <= 0)
            throw new BusinessRuleException("RPP_REDONDEO_EXCEDE_SALDO", "El efectivo del pago resultante debe ser mayor a cero.");
        if (await db.AutorizacionesRedondeoPago.AnyAsync(a => a.EmpresaId == empresaId && a.FacturaProveedorId == c.FacturaProveedorId
                && (a.Estado == EstadoAutorizacionRedondeoPago.Solicitada || a.Estado == EstadoAutorizacionRedondeoPago.Autorizada), ct))
            throw new BusinessRuleException("RPP_YA_VIGENTE", "La factura ya tiene una solicitud de redondeo vigente.");
        var a = new AutorizacionRedondeoPago(empresaId, c.FacturaProveedorId, c.Redondeo, c.Motivo, usuarioId, clock.UtcNow);
        db.AutorizacionesRedondeoPago.Add(a);
        await db.SaveChangesAsync(ct);
        return RedondeoPagoResponse.De(a);
    }
}

public sealed record AutorizarRedondeoPagoCommand(Guid Id) : IRequest<RedondeoPagoResponse>;

public sealed class AutorizarRedondeoPagoHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<AutorizarRedondeoPagoCommand, RedondeoPagoResponse>
{
    public async Task<RedondeoPagoResponse> Handle(AutorizarRedondeoPagoCommand request, CancellationToken cancellationToken)
    {
        var c = request; var ct = cancellationToken;
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (usuario.UserId is not Guid usuarioId) throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var a = await db.AutorizacionesRedondeoPago.FirstOrDefaultAsync(x => x.Id == c.Id && x.EmpresaId == empresaId, ct)
            ?? throw new EntityNotFoundException("RPP_NO_ENCONTRADO", $"No se encontró la solicitud de redondeo '{c.Id}'.");
        a.Autorizar(usuarioId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return RedondeoPagoResponse.De(a);
    }
}

public sealed record RechazarRedondeoPagoCommand(Guid Id, string Motivo) : IRequest<RedondeoPagoResponse>;

public sealed class RechazarRedondeoPagoValidator : AbstractValidator<RechazarRedondeoPagoCommand>
{
    public RechazarRedondeoPagoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarRedondeoPagoHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RechazarRedondeoPagoCommand, RedondeoPagoResponse>
{
    public async Task<RedondeoPagoResponse> Handle(RechazarRedondeoPagoCommand request, CancellationToken cancellationToken)
    {
        var c = request; var ct = cancellationToken;
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (usuario.UserId is not Guid usuarioId) throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var a = await db.AutorizacionesRedondeoPago.FirstOrDefaultAsync(x => x.Id == c.Id && x.EmpresaId == empresaId, ct)
            ?? throw new EntityNotFoundException("RPP_NO_ENCONTRADO", $"No se encontró la solicitud de redondeo '{c.Id}'.");
        a.Rechazar(usuarioId, c.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return RedondeoPagoResponse.De(a);
    }
}

// M-326 (b), lectura: lista de solicitudes de autorización de redondeo de pagos, con el alcance por sucursal de los pasivos (ADR-0051).

/// <summary>
/// Una solicitud de redondeo. <c>Importe</c> con signo (positivo: el saldo se liquida con menos efectivo; negativo: se pagó de más).
/// Compatibles con <see cref="RedondeoPagoResponse"/>: <c>Redondeo</c> repite <c>Importe</c> y <c>ResueltoPor</c> repite <c>AutorizadoPor</c>
/// (quien autorizó o rechazó). El proveedor viene del pasivo (<c>ProveedorRazonSocial</c> null si el catálogo no lo resuelve).
/// <c>Fecha</c> = cuando se solicitó; <c>ResueltoEn</c> = cuando se autorizó o rechazó. <c>Usable</c>: autorizada y aún sin consumir por un pago.
/// </summary>
public sealed record RedondeoPagoListadoItem(
    Guid Id, Guid FacturaProveedorId, string? FolioProveedor, Guid? ProveedorId, string? ProveedorClave, string? ProveedorRazonSocial,
    decimal Importe, decimal Redondeo, string Motivo, string Estado, Guid SolicitadoPor, Guid? AutorizadoPor, Guid? ResueltoPor,
    DateTimeOffset Fecha, DateTimeOffset? ResueltoEn, string? MotivoRechazo, Guid? PagoId, bool Usable);

public sealed record ListarRedondeosPagoQuery(string? Estado = null, Guid? FacturaId = null, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<RedondeoPagoListadoItem>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "pasivo";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ListarRedondeosPagoHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext empresa, Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort proveedores)
    : IRequestHandler<ListarRedondeosPagoQuery, PagedResponse<RedondeoPagoListadoItem>>
{
    public async Task<PagedResponse<RedondeoPagoListadoItem>> Handle(ListarRedondeosPagoQuery query, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId) throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        EstadoAutorizacionRedondeoPago? estado = null;
        if (!string.IsNullOrWhiteSpace(query.Estado))
        {
            if (!Enum.TryParse<EstadoAutorizacionRedondeoPago>(query.Estado.Trim(), true, out var e) || !Enum.IsDefined(e))
                throw new BusinessRuleException("RPP_ESTADO_INVALIDO", "El estado debe ser Solicitada, Autorizada, Rechazada o Consumida.");
            estado = e;
        }
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = db.AutorizacionesRedondeoPago.AsNoTracking().Where(a => a.EmpresaId == empresaId);
        if (query.DocumentosPermitidos is { } permitidos)
        {
            // Alcance por sucursal: solo las facturas cuyo pasivo (documento «pasivo») está en las sucursales del usuario.
            var facturas = db.PasivosPendientesPago.Where(p => permitidos.Contains(p.Id)).Select(p => p.FacturaProveedorId);
            q = q.Where(a => facturas.Contains(a.FacturaProveedorId));
        }
        if (estado is { } est) q = q.Where(a => a.Estado == est);
        if (query.FacturaId is Guid factura) q = q.Where(a => a.FacturaProveedorId == factura);

        var total = await q.CountAsync(cancellationToken);
        var filas = await q.OrderByDescending(a => a.CreadoEn).ThenBy(a => a.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var ids = filas.Select(a => a.FacturaProveedorId).Distinct().ToList();
        var pasivos = (await db.PasivosPendientesPago.AsNoTracking().Where(p => ids.Contains(p.FacturaProveedorId))
            .Select(p => new { p.FacturaProveedorId, p.FolioProveedor, p.ProveedorId }).ToListAsync(cancellationToken))
            .GroupBy(f => f.FacturaProveedorId).ToDictionary(g => g.Key, g => g.First());
        var catalogo = await proveedores.ObtenerVariosAsync(pasivos.Values.Select(p => p.ProveedorId).Distinct().ToList(), cancellationToken);

        var items = filas.Select(a =>
        {
            pasivos.TryGetValue(a.FacturaProveedorId, out var pasivo);
            Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto? prov = null;
            if (pasivo is not null) catalogo.TryGetValue(pasivo.ProveedorId, out prov);
            var resolvio = a.Estado is EstadoAutorizacionRedondeoPago.Autorizada or EstadoAutorizacionRedondeoPago.Consumida ? a.ResueltoPor : null;
            return new RedondeoPagoListadoItem(
                a.Id, a.FacturaProveedorId, pasivo?.FolioProveedor, pasivo?.ProveedorId, prov?.Clave, prov?.RazonSocial,
                a.Redondeo, a.Redondeo, a.Motivo, a.Estado.ToString(), a.SolicitadoPor, resolvio, a.ResueltoPor,
                a.CreadoEn, a.ResueltoEn, a.MotivoRechazo, a.PagoId, a.Estado == EstadoAutorizacionRedondeoPago.Autorizada);
        }).ToList();
        return new PagedResponse<RedondeoPagoListadoItem>(items, offset, limit, total);
    }
}
