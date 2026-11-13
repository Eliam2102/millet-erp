using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
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
