using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// PATCH parcial sobre un artículo (B.5). Convención: nullable null
/// = no tocar, flag <c>limpiarX</c> = setear nullable a null.
/// Inmutables: id, clave, claveLegacy. El cambio individual de
/// <see cref="Naturaleza"/> es complementario al endpoint bulk de
/// F9-PR1 (reclasificar-naturaleza).
/// </summary>
public sealed record ActualizarArticuloCommand(
    Guid ArticuloId,
    string? Nombre,
    string? DescripcionLarga,
    Guid? UnidadMedidaId,
    Naturaleza? Naturaleza,
    Guid? CategoriaId,
    decimal? PrecioReferenciaMonto,
    string? PrecioReferenciaMoneda,
    bool LimpiarDescripcionLarga,
    bool LimpiarCategoria,
    bool LimpiarPrecioReferencia) : IRequest;

public sealed class ActualizarArticuloValidator : AbstractValidator<ActualizarArticuloCommand>
{
    public ActualizarArticuloValidator()
    {
        RuleFor(c => c.ArticuloId).NotEqual(Guid.Empty);
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254).When(c => c.Nombre is not null);
        RuleFor(c => c.UnidadMedidaId!.Value).NotEqual(Guid.Empty)
            .When(c => c.UnidadMedidaId.HasValue);
        RuleFor(c => c.Naturaleza).IsInEnum().When(c => c.Naturaleza.HasValue);
        RuleFor(c => c.CategoriaId!.Value).NotEqual(Guid.Empty).When(c => c.CategoriaId.HasValue);
        RuleFor(c => c.PrecioReferenciaMonto).GreaterThanOrEqualTo(0)
            .When(c => c.PrecioReferenciaMonto.HasValue);
        RuleFor(c => c.PrecioReferenciaMoneda!).Length(3).Matches("^[A-Z]{3}$")
            .When(c => c.PrecioReferenciaMoneda is not null);
    }
}

public sealed class ActualizarArticuloHandler : IRequestHandler<ActualizarArticuloCommand>
{
    private readonly CompartidoDbContext _db;

    public ActualizarArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ActualizarArticuloCommand request, CancellationToken cancellationToken)
    {
        var articulo = await _db.Articulos
            .FirstOrDefaultAsync(a => a.Id == request.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{request.ArticuloId}'.");

        // Cross-table: si llega moneda, validar contra catálogo.
        if (request.PrecioReferenciaMoneda is { } moneda)
        {
            var existeMoneda = await _db.Monedas.AsNoTracking()
                .AnyAsync(m => m.Codigo == moneda, cancellationToken);
            if (!existeMoneda)
            {
                throw new BusinessRuleException(
                    "ARTICULO_MONEDA_NO_REGISTRADA",
                    $"La moneda '{moneda}' no existe en el catálogo compartido.monedas.");
            }
        }

        // ADR-0046 Etapa 1b: si llega unidadMedidaId, valida que exista/activa,
        // setea el FK y sincroniza unidad_medida_default = código. Si no llega,
        // el FK y el string legacy quedan como estaban (artículo legacy intacto).
        if (request.UnidadMedidaId is { } unidadId)
        {
            var unidad = await _db.UnidadesMedida.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == unidadId, cancellationToken);
            if (unidad is null || unidad.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "ARTICULO_UNIDAD_MEDIDA_NO_REGISTRADA",
                    $"La unidad de medida '{unidadId}' no existe o no está activa.");
            }
            articulo.AsignarUnidadMedida(unidad.Id, unidad.Codigo);
        }

        // Patrón ADR-0046 (PR2): si llega categoriaId, valida + AsignarCategoria
        // (setea FK + string legacy = nombre). Si LimpiarCategoria, ambos a null.
        // Si no llega ninguno, la categoría queda intacta — un artículo no
        // reconciliado (PR3) conserva su string legacy + categoria_id NULL.
        if (request.CategoriaId is { } categoriaId)
        {
            var categoria = await _db.CategoriasArticulo.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoriaId, cancellationToken);
            if (categoria is null || categoria.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "ARTICULO_CATEGORIA_NO_REGISTRADA",
                    $"La categoría '{categoriaId}' no existe o no está activa.");
            }
            articulo.AsignarCategoria(categoria.Id, categoria.Nombre);
        }
        else if (request.LimpiarCategoria)
        {
            articulo.LimpiarCategoria();
        }

        articulo.ActualizarDatos(
            nombre: request.Nombre,
            descripcionLarga: request.DescripcionLarga,
            naturaleza: request.Naturaleza,
            precioReferenciaMonto: request.PrecioReferenciaMonto,
            precioReferenciaMoneda: request.PrecioReferenciaMoneda,
            limpiarDescripcionLarga: request.LimpiarDescripcionLarga,
            limpiarPrecioReferencia: request.LimpiarPrecioReferencia);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
