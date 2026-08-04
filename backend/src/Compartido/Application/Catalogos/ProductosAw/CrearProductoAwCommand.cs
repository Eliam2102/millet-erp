using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// Alta manual de producto de venta en <c>compartido.producto_aw</c>
/// (ADR-0048 D5). UNIQUE(referencia_externa) → 422 legible. La
/// auto-provisión desde A+W NO usa este command (usa
/// <c>ProvisionarProductoAwCommand</c>, PR4); este es el alta de operador
/// (p.ej. adelantar el catálogo antes del primer pedido).
/// </summary>
public sealed record CrearProductoAwCommand(
    string ReferenciaExterna,
    string Descripcion,
    string UnidadMedida,
    Guid? UnidadMedidaId,
    Guid? CategoriaId,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string? FraccionArancelaria = null,
    string? UnidadAduana = null,
    decimal? PesoUnitarioKg = null) : IRequest<CrearProductoAwResponse>;

public sealed record CrearProductoAwResponse(Guid Id, string ReferenciaExterna);

public sealed class CrearProductoAwValidator : AbstractValidator<CrearProductoAwCommand>
{
    public CrearProductoAwValidator()
    {
        RuleFor(c => c.ReferenciaExterna).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Descripcion).NotEmpty().MaximumLength(254);
        RuleFor(c => c.UnidadMedida).NotEmpty().MaximumLength(20);
        RuleFor(c => c.ClaveProdServSat!).Length(8).When(c => c.ClaveProdServSat is not null);
        RuleFor(c => c.ClaveUnidadSat!).MaximumLength(5).When(c => c.ClaveUnidadSat is not null);
        RuleFor(c => c.ObjetoImp!).Must(o => o is "01" or "02" or "03")
            .WithMessage("El objeto de impuesto debe ser 01, 02 o 03.")
            .When(c => c.ObjetoImp is not null);
        RuleFor(c => c.TasaIvaTraslado).InclusiveBetween(0m, 1m)
            .When(c => c.TasaIvaTraslado.HasValue);
        RuleFor(c => c.TasaRetencionIva).InclusiveBetween(0m, 1m)
            .When(c => c.TasaRetencionIva.HasValue);
        RuleFor(c => c.TasaRetencionIsr).InclusiveBetween(0m, 1m)
            .When(c => c.TasaRetencionIsr.HasValue);
        RuleFor(c => c.FraccionArancelaria!).Matches(@"^\d{8,10}$")
            .When(c => c.FraccionArancelaria is not null);
        RuleFor(c => c.UnidadAduana!).NotEmpty().MaximumLength(3)
            .When(c => c.UnidadAduana is not null);
        RuleFor(c => c.PesoUnitarioKg).GreaterThanOrEqualTo(0m)
            .When(c => c.PesoUnitarioKg.HasValue);
    }
}

public sealed class CrearProductoAwHandler
    : IRequestHandler<CrearProductoAwCommand, CrearProductoAwResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearProductoAwHandler(CompartidoDbContext db) => _db = db;

    public async Task<CrearProductoAwResponse> Handle(
        CrearProductoAwCommand request, CancellationToken cancellationToken)
    {
        var refExiste = await _db.ProductosAw.AsNoTracking()
            .AnyAsync(p => p.ReferenciaExterna == request.ReferenciaExterna, cancellationToken);
        if (refExiste)
        {
            throw new BusinessRuleException(
                "PRODUCTO_AW_REFERENCIA_DUPLICADA",
                $"Ya existe un producto A+W con referencia '{request.ReferenciaExterna}'.");
        }

        // Cross-table: la unidad del catálogo debe existir si se manda el FK.
        if (request.UnidadMedidaId is Guid umId)
        {
            var existeUm = await _db.UnidadesMedida.AsNoTracking()
                .AnyAsync(u => u.Id == umId, cancellationToken);
            if (!existeUm)
            {
                throw new EntityNotFoundException(
                    "UNIDAD_MEDIDA_NO_ENCONTRADA",
                    $"No existe unidad de medida con id '{umId}'.");
            }
        }
        if (request.CategoriaId is Guid catId)
        {
            var existeCat = await _db.CategoriasArticulo.AsNoTracking()
                .AnyAsync(c => c.Id == catId, cancellationToken);
            if (!existeCat)
            {
                throw new EntityNotFoundException(
                    "CATEGORIA_NO_ENCONTRADA",
                    $"No existe categoría con id '{catId}'.");
            }
        }

        var producto = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: request.ReferenciaExterna,
            descripcion: request.Descripcion,
            unidadMedida: request.UnidadMedida,
            origen: OrigenMaster.Manual,
            unidadMedidaId: request.UnidadMedidaId,
            categoriaId: request.CategoriaId,
            claveProdServSat: request.ClaveProdServSat,
            claveUnidadSat: request.ClaveUnidadSat,
            objetoImp: request.ObjetoImp,
            tasaIvaTraslado: request.TasaIvaTraslado,
            tasaRetencionIva: request.TasaRetencionIva,
            tasaRetencionIsr: request.TasaRetencionIsr,
            fraccionArancelaria: request.FraccionArancelaria,
            unidadAduana: request.UnidadAduana,
            pesoUnitarioKg: request.PesoUnitarioKg);

        _db.ProductosAw.Add(producto);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearProductoAwResponse(producto.Id, producto.ReferenciaExterna);
    }
}
