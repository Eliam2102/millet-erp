using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

public sealed record RechazarProveedorCommand(
    Guid ProveedorId,
    string Motivo) : IRequest<RechazarProveedorResponse>;

public sealed record RechazarProveedorResponse(
    Guid Id,
    string Clave,
    EstatusCatalogo Estatus,
    string MotivoRechazo,
    DateTimeOffset RechazadoEn);

public sealed class RechazarProveedorValidator : AbstractValidator<RechazarProveedorCommand>
{
    public RechazarProveedorValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("El motivo de rechazo es obligatorio.")
            .MinimumLength(5).WithMessage("El motivo de rechazo debe contener al menos 5 caracteres.")
            .MaximumLength(500).WithMessage("El motivo de rechazo no puede exceder 500 caracteres.");
    }
}

public sealed class RechazarProveedorHandler : IRequestHandler<RechazarProveedorCommand, RechazarProveedorResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;

    public RechazarProveedorHandler(
        CompartidoDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresa,
        IClock clock,
        IAuditLogWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _empresa = empresa;
        _clock = clock;
        _audit = audit;
    }

    public async Task<RechazarProveedorResponse> Handle(
        RechazarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken);

        if (proveedor is null)
        {
            throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No existe proveedor con id '{request.ProveedorId}'.");
        }

        if (proveedor.Estatus != EstatusCatalogo.EnRevision)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_NO_EN_REVISION",
                $"Solo se pueden rechazar proveedores en estado 'EnRevision'. Estado actual: '{proveedor.Estatus}'.");
        }

        var ahora = _clock.UtcNow;
        var validadorId = _currentUser.UserId ?? Guid.Empty;
        var motivo = request.Motivo.Trim();

        proveedor.Rechazar(validadorId, motivo, ahora);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RegistrarAsync(
            operacion: "proveedor.rechazado",
            modulo: "DatosMaestros",
            entidad: "Proveedor",
            entidadId: proveedor.Id,
            aggregateRootId: proveedor.Id,
            actorNombre: _currentUser.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: _currentUser.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Proveedor {proveedor.Clave} rechazado por CxP. Motivo: {motivo}",
            usuarioId: validadorId,
            empresaId: _empresa.Current,
            cambios: JsonSerializer.Serialize(new { estatusAnterior = "EnRevision", nuevoEstatus = "Inactivo", motivo }),
            metadatos: JsonSerializer.Serialize(new { proveedor.Clave, proveedor.Rfc }),
            cancellationToken: cancellationToken);

        return new RechazarProveedorResponse(
            proveedor.Id,
            proveedor.Clave,
            proveedor.Estatus,
            motivo,
            ahora);
    }
}
