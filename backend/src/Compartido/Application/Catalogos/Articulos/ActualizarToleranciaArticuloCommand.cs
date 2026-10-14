using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Articulos;

/// <summary>
/// Configura la tolerancia de recepción por material (A5). Editable solo con
/// el permiso <c>datos_maestros.articulos.tolerancia-editar</c>; <c>null</c>
/// equivale a 0 %.
/// </summary>
public sealed record ActualizarToleranciaArticuloCommand(Guid ArticuloId, decimal? Porcentaje) : IRequest;

public sealed class ActualizarToleranciaArticuloValidator : AbstractValidator<ActualizarToleranciaArticuloCommand>
{
    public ActualizarToleranciaArticuloValidator()
    {
        RuleFor(x => x.ArticuloId).NotEmpty();
        RuleFor(x => x.Porcentaje).InclusiveBetween(0m, 100m)
            .WithMessage("La tolerancia de recepción debe estar entre 0 y 100 % con máximo 2 decimales.");
        RuleFor(x => x.Porcentaje).PrecisionScale(5, 2, true)
            .WithMessage("La tolerancia de recepción debe estar entre 0 y 100 % con máximo 2 decimales.");
    }
}

public sealed class ActualizarToleranciaArticuloHandler(
    CompartidoDbContext db,
    ICurrentUserPermissions permisos,
    ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa,
    IClock clock,
    IAuditLogWriter audit) : IRequestHandler<ActualizarToleranciaArticuloCommand>
{
    public async Task Handle(ActualizarToleranciaArticuloCommand request, CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync("datos_maestros.articulos.tolerancia-editar", cancellationToken))
            throw new ForbiddenException("ARTICULO_TOLERANCIA_SIN_PERMISO",
                "Se requiere el permiso de tolerancia de recepción para editar la tolerancia del artículo.");

        var articulo = await db.Articulos.FirstOrDefaultAsync(a => a.Id == request.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException("ARTICULO_NO_ENCONTRADO", "No existe el artículo solicitado.");
        var anterior = articulo.ToleranciaCantidadPorcentaje;
        if (anterior == request.Porcentaje) return;

        articulo.ActualizarToleranciaRecepcion(request.Porcentaje);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "articulo.tolerancia-recepcion-cambiada",
            modulo: "DatosMaestros", entidad: "Articulo",
            entidadId: articulo.Id, aggregateRootId: articulo.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{articulo.Clave} · {articulo.Nombre}",
            resumen: "Tolerancia de recepción por material actualizada.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { toleranciaCantidadPorcentaje = new { antes = anterior, despues = request.Porcentaje } }),
            metadatos: JsonSerializer.Serialize(new { ocurridoEn = clock.UtcNow }),
            cancellationToken: cancellationToken);
    }
}
