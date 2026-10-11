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

public sealed record PonerProveedorEnRevisionCommand(
    Guid ProveedorId,
    string Motivo) : IRequest<PonerProveedorEnRevisionResponse>;

public sealed record PonerProveedorEnRevisionResponse(
    Guid Id,
    string Clave,
    EstatusCatalogo Estatus,
    string MotivoRevision,
    DateTimeOffset EnviadoEn);

public sealed class PonerProveedorEnRevisionValidator : AbstractValidator<PonerProveedorEnRevisionCommand>
{
    public PonerProveedorEnRevisionValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("El motivo de revisión es obligatorio.")
            .MinimumLength(5).WithMessage("El motivo de revisión debe contener al menos 5 caracteres.")
            .MaximumLength(500).WithMessage("El motivo de revisión no puede exceder 500 caracteres.");
    }
}

public sealed class PonerProveedorEnRevisionHandler : IRequestHandler<PonerProveedorEnRevisionCommand, PonerProveedorEnRevisionResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;

    public PonerProveedorEnRevisionHandler(
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

    public async Task<PonerProveedorEnRevisionResponse> Handle(
        PonerProveedorEnRevisionCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken);

        if (proveedor is null)
        {
            throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                "No se encontró el proveedor. Actualiza la lista y vuelve a seleccionarlo.");
        }

        var ahora = _clock.UtcNow;
        var validadorId = _currentUser.UserId ?? Guid.Empty;
        var motivo = request.Motivo.Trim();

        proveedor.PonerEnRevision(motivo);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RegistrarAsync(
            operacion: "proveedor.en-revision",
            modulo: "DatosMaestros",
            entidad: "Proveedor",
            entidadId: proveedor.Id,
            aggregateRootId: proveedor.Id,
            actorNombre: _currentUser.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: _currentUser.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Proveedor {proveedor.Clave} puesto en revisión por Cuentas por pagar. Motivo: {motivo}",
            usuarioId: validadorId,
            empresaId: _empresa.Current,
            cambios: JsonSerializer.Serialize(new { estatusAnterior = "Activo", nuevoEstatus = "EnRevision", motivo }),
            metadatos: JsonSerializer.Serialize(new { proveedor.Clave, proveedor.Rfc }),
            cancellationToken: cancellationToken);

        return new PonerProveedorEnRevisionResponse(
            proveedor.Id,
            proveedor.Clave,
            proveedor.Estatus,
            motivo,
            ahora);
    }
}
