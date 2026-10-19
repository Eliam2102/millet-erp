using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

public sealed record ActualizarToleranciaProveedorCommand(Guid ProveedorId, decimal? MontoMxn, bool EsPorcentaje = false) : IRequest;

public sealed class ActualizarToleranciaProveedorValidator : AbstractValidator<ActualizarToleranciaProveedorCommand>
{
    public ActualizarToleranciaProveedorValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty();
        RuleFor(x => x.MontoMxn).PrecisionScale(18, 4, true)
            .WithMessage("La tolerancia admite hasta 14 enteros y 4 decimales.");
        RuleFor(x => x.MontoMxn).GreaterThanOrEqualTo(0)
            .WithMessage("La tolerancia factura contra OC no puede ser negativa.");
        RuleFor(x => x.MontoMxn).LessThanOrEqualTo(100).When(x => x.EsPorcentaje)
            .WithMessage("La tolerancia en porcentaje no puede pasar de 100 %.");
    }
}

public sealed class ActualizarToleranciaProveedorHandler(
    CompartidoDbContext db,
    ICurrentUserPermissions permisos,
    ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa,
    IClock clock,
    IAuditLogWriter audit) : IRequestHandler<ActualizarToleranciaProveedorCommand>
{
    public async Task Handle(ActualizarToleranciaProveedorCommand request, CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync("datos_maestros.proveedores.tolerancia-editar", cancellationToken))
            throw new ForbiddenException("PROVEEDOR_TOLERANCIA_SIN_PERMISO",
                "Se requiere el permiso de CxP para editar la tolerancia del proveedor.");

        var proveedor = await db.Proveedores.FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No existe el proveedor solicitado.");
        var anterior = proveedor.ToleranciaFacturaContraOcMxn;
        var anteriorEsPorcentaje = proveedor.ToleranciaFacturaContraOcEsPorcentaje;
        var esPorcentaje = request.MontoMxn.HasValue && request.EsPorcentaje;
        if (anterior == request.MontoMxn && anteriorEsPorcentaje == esPorcentaje) return;

        proveedor.ActualizarToleranciaFacturaContraOc(request.MontoMxn, esPorcentaje);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.tolerancia-cambiada",
            modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: "Tolerancia factura contra OC actualizada por CxP.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new
            {
                toleranciaFacturaContraOcMxn = new { antes = anterior, despues = request.MontoMxn },
                toleranciaFacturaContraOcEsPorcentaje = new { antes = anteriorEsPorcentaje, despues = esPorcentaje },
            }),
            metadatos: JsonSerializer.Serialize(new { ocurridoEn = clock.UtcNow }),
            cancellationToken: cancellationToken);
    }
}
