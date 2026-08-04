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
/// Alta de artículo en <c>compartido.articulos</c> (B.5). UNIQUE
/// (clave) → 422 PROVEEDOR_CLAVE_DUPLICADA si choca. Si llega
/// <see cref="PrecioReferenciaMoneda"/>, validación cross-table contra
/// <c>compartido.monedas</c>.
/// </summary>
public sealed record CrearArticuloCommand(
    string Clave,
    string Nombre,
    Guid UnidadMedidaId,
    Naturaleza Naturaleza,
    string? DescripcionLarga,
    Guid? CategoriaId,
    decimal? PrecioReferenciaMonto,
    string? PrecioReferenciaMoneda) : IRequest<CrearArticuloResponse>;

public sealed record CrearArticuloResponse(Guid Id, string Clave);

public sealed class CrearArticuloValidator : AbstractValidator<CrearArticuloCommand>
{
    public CrearArticuloValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.UnidadMedidaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Naturaleza).IsInEnum();
        RuleFor(c => c.CategoriaId!.Value).NotEqual(Guid.Empty).When(c => c.CategoriaId.HasValue);
        RuleFor(c => c.PrecioReferenciaMonto).GreaterThanOrEqualTo(0)
            .When(c => c.PrecioReferenciaMonto.HasValue);
        RuleFor(c => c.PrecioReferenciaMoneda!).Length(3).Matches("^[A-Z]{3}$")
            .When(c => c.PrecioReferenciaMoneda is not null);
    }
}

public sealed class CrearArticuloHandler
    : IRequestHandler<CrearArticuloCommand, CrearArticuloResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task<CrearArticuloResponse> Handle(
        CrearArticuloCommand request, CancellationToken cancellationToken)
    {
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

        var claveExiste = await _db.Articulos.AsNoTracking()
            .AnyAsync(a => a.Clave == request.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "ARTICULO_CLAVE_DUPLICADA",
                $"Ya existe un artículo con clave '{request.Clave}'.");
        }

        // ADR-0046 Etapa 1b: el artículo nace con FK a la unidad. Validamos
        // que exista y esté activa, y derivamos unidad_medida_default = código
        // (snapshot que copian las líneas).
        var unidad = await _db.UnidadesMedida.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UnidadMedidaId, cancellationToken);
        if (unidad is null || unidad.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "ARTICULO_UNIDAD_MEDIDA_NO_REGISTRADA",
                $"La unidad de medida '{request.UnidadMedidaId}' no existe o no está activa.");
        }

        // Patrón ADR-0046 (PR2): si llega categoriaId, valida que exista/activa;
        // el string legacy categoria = nombre de la categoría. Sin categoría,
        // ambos quedan null.
        CategoriaArticulo? categoria = null;
        if (request.CategoriaId is { } categoriaId)
        {
            categoria = await _db.CategoriasArticulo.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoriaId, cancellationToken);
            if (categoria is null || categoria.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "ARTICULO_CATEGORIA_NO_REGISTRADA",
                    $"La categoría '{categoriaId}' no existe o no está activa.");
            }
        }

        var articulo = new Articulo(
            id: Guid.CreateVersion7(),
            clave: request.Clave,
            nombre: request.Nombre,
            unidadMedidaDefault: unidad.Codigo,
            naturaleza: request.Naturaleza,
            descripcionLarga: request.DescripcionLarga,
            categoria: categoria?.Nombre,
            precioReferenciaMonto: request.PrecioReferenciaMonto,
            precioReferenciaMoneda: request.PrecioReferenciaMoneda,
            unidadMedidaId: unidad.Id,
            categoriaId: categoria?.Id);

        _db.Articulos.Add(articulo);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearArticuloResponse(articulo.Id, articulo.Clave);
    }
}
