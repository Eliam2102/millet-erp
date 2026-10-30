using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

// 86e3tactf: bloqueo total del proveedor (no se paga ninguna factura, no se levantan OC nuevas).
// Es una marca aparte del estatus de alta. Compras, CxP y Tesorería leen la marca; no hay evento.

public sealed record BloquearProveedorCommand(Guid ProveedorId, string Motivo) : IRequest<BloqueoProveedorResponse>;

public sealed record DesbloquearProveedorCommand(Guid ProveedorId, string Motivo) : IRequest<BloqueoProveedorResponse>;

public sealed record BloqueoProveedorResponse(Guid Id, string Clave, bool Bloqueado, string? MotivoBloqueo, DateTimeOffset? BloqueadoEn);

public sealed class BloquearProveedorValidator : AbstractValidator<BloquearProveedorCommand>
{
    public BloquearProveedorValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MinimumLength(10).MaximumLength(500)
            .WithMessage("Indica el motivo (de 10 a 500 caracteres).");
    }
}

public sealed class DesbloquearProveedorValidator : AbstractValidator<DesbloquearProveedorCommand>
{
    public DesbloquearProveedorValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MinimumLength(10).MaximumLength(500)
            .WithMessage("Indica el motivo (de 10 a 500 caracteres).");
    }
}

public sealed class BloqueoProveedorHandlers(
    CompartidoDbContext db,
    ICurrentUserContext currentUser,
    ICurrentUserPermissions permisos,
    ICurrentEmpresaContext empresa,
    IClock clock,
    IAuditLogWriter audit)
    : IRequestHandler<BloquearProveedorCommand, BloqueoProveedorResponse>,
      IRequestHandler<DesbloquearProveedorCommand, BloqueoProveedorResponse>
{
    // Espejo de Identidad.Domain.PermisosCanonicos.CuentasPorPagarBloqueosLiberarEnLugar (Compartido no referencia Identidad).
    internal const string PermisoLiberarEnLugar = "cuentas_por_pagar.bloqueos.liberar-en-lugar";

    public async Task<BloqueoProveedorResponse> Handle(BloquearProveedorCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = currentUser.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var proveedor = await ObtenerAsync(request.ProveedorId, cancellationToken);
        proveedor.Bloquear(usuarioId, request.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await RegistrarAsync("proveedor.bloqueado", "bloqueado", proveedor, proveedor.MotivoBloqueo!, usuarioId, cancellationToken);
        return new(proveedor.Id, proveedor.Clave, true, proveedor.MotivoBloqueo, proveedor.BloqueadoEn);
    }

    public async Task<BloqueoProveedorResponse> Handle(DesbloquearProveedorCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = currentUser.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var proveedor = await ObtenerAsync(request.ProveedorId, cancellationToken);
        var solicitante = proveedor.BloqueadoPorId;
        var puedeReemplazar = await permisos.TieneAsync(PermisoLiberarEnLugar, cancellationToken);
        proveedor.Desbloquear(usuarioId, request.Motivo, puedeReemplazar);
        await db.SaveChangesAsync(cancellationToken);
        await RegistrarAsync("proveedor.desbloqueado", "desbloqueado", proveedor, request.Motivo.Trim(), usuarioId, cancellationToken, solicitante);
        return new(proveedor.Id, proveedor.Clave, false, null, null);
    }

    private async Task<DatosMaestros.Domain.Proveedor> ObtenerAsync(Guid id, CancellationToken ct) =>
        await db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO",
            "No se encontró el proveedor. Actualiza la lista y vuelve a seleccionarlo.");

    // Bitácora: quién registró, quién lo había pedido (al desbloquear), cuándo y el motivo.
    private Task RegistrarAsync(string operacion, string verbo, DatosMaestros.Domain.Proveedor p, string motivo,
        Guid usuarioId, CancellationToken ct, Guid? solicitante = null) =>
        audit.RegistrarAsync(
            operacion: operacion,
            modulo: "DatosMaestros",
            entidad: "Proveedor",
            entidadId: p.Id,
            aggregateRootId: p.Id,
            actorNombre: currentUser.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: currentUser.Email,
            entidadEtiqueta: $"{p.Clave} · {p.RazonSocial}",
            resumen: $"Proveedor {p.Clave} {verbo}. Motivo: {motivo}",
            usuarioId: usuarioId,
            empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { bloqueado = p.Bloqueado, motivo, solicitanteId = solicitante ?? p.BloqueadoPorId }),
            metadatos: JsonSerializer.Serialize(new { p.Clave, p.Rfc }),
            cancellationToken: ct);
}
