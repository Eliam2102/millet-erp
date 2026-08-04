using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// PATCH parcial de producto A+W: datos operativos (descripción, unidad,
/// categoría) + atributos fiscales SAT (la vía para completar clave
/// prod/serv y clave unidad de los auto-provisionados antes de timbrar).
/// Inmutables: `Id`, `ReferenciaExterna`, `Origen`.
/// </summary>
public sealed record ActualizarProductoAwCommand(
    Guid ProductoAwId,
    string? Descripcion = null,
    string? UnidadMedida = null,
    Guid? UnidadMedidaId = null,
    Guid? CategoriaId = null,
    string? ClaveProdServSat = null,
    string? ClaveUnidadSat = null,
    string? ObjetoImp = null,
    decimal? TasaIvaTraslado = null,
    decimal? TasaRetencionIva = null,
    decimal? TasaRetencionIsr = null,
    string? FraccionArancelaria = null,
    string? UnidadAduana = null,
    decimal? PesoUnitarioKg = null,
    bool LimpiarCategoria = false,
    bool LimpiarTasaIvaTraslado = false,
    bool LimpiarTasaRetencionIva = false,
    bool LimpiarTasaRetencionIsr = false,
    bool LimpiarFraccionArancelaria = false,
    bool LimpiarUnidadAduana = false,
    bool LimpiarPesoUnitarioKg = false) : IRequest;

public sealed class ActualizarProductoAwValidator : AbstractValidator<ActualizarProductoAwCommand>
{
    public ActualizarProductoAwValidator()
    {
        RuleFor(c => c.ProductoAwId).NotEmpty();
        RuleFor(c => c.Descripcion!).NotEmpty().MaximumLength(254)
            .When(c => c.Descripcion is not null);
        RuleFor(c => c.UnidadMedida!).NotEmpty().MaximumLength(20)
            .When(c => c.UnidadMedida is not null);
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
            .WithMessage("La fracción arancelaria debe tener de 8 a 10 dígitos numéricos.")
            .When(c => c.FraccionArancelaria is not null);
        RuleFor(c => c.UnidadAduana!).NotEmpty().MaximumLength(3)
            .When(c => c.UnidadAduana is not null);
        RuleFor(c => c.PesoUnitarioKg).GreaterThanOrEqualTo(0m)
            .When(c => c.PesoUnitarioKg.HasValue);
    }
}

public sealed class ActualizarProductoAwHandler : IRequestHandler<ActualizarProductoAwCommand>
{
    private readonly CompartidoDbContext _db;

    public ActualizarProductoAwHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ActualizarProductoAwCommand request, CancellationToken cancellationToken)
    {
        var producto = await _db.ProductosAw
            .FirstOrDefaultAsync(p => p.Id == request.ProductoAwId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PRODUCTO_AW_NO_ENCONTRADO",
                $"No existe producto A+W con id '{request.ProductoAwId}'.");

        // FK opcional a UnidadMedida: asignación explícita con sincronía del
        // snapshot (mismo patrón AsignarUnidadMedida de Articulo, ADR-0046).
        if (request.UnidadMedidaId is Guid umId)
        {
            var um = await _db.UnidadesMedida.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == umId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "UNIDAD_MEDIDA_NO_ENCONTRADA",
                    $"No existe unidad de medida con id '{umId}'.");
            producto.AsignarUnidadMedida(um.Id, um.Codigo);
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

        producto.ActualizarDatos(
            descripcion: request.Descripcion,
            unidadMedida: request.UnidadMedidaId is null ? request.UnidadMedida : null,
            categoriaId: request.CategoriaId,
            limpiarCategoria: request.LimpiarCategoria);

        producto.AsignarDatosFiscales(
            claveProdServSat: request.ClaveProdServSat,
            claveUnidadSat: request.ClaveUnidadSat,
            objetoImp: request.ObjetoImp,
            tasaIvaTraslado: request.TasaIvaTraslado,
            tasaRetencionIva: request.TasaRetencionIva,
            tasaRetencionIsr: request.TasaRetencionIsr,
            limpiarTasaIvaTraslado: request.LimpiarTasaIvaTraslado,
            limpiarTasaRetencionIva: request.LimpiarTasaRetencionIva,
            limpiarTasaRetencionIsr: request.LimpiarTasaRetencionIsr);

        producto.AsignarDatosAduana(
            fraccionArancelaria: request.FraccionArancelaria,
            unidadAduana: request.UnidadAduana,
            pesoUnitarioKg: request.PesoUnitarioKg,
            limpiarFraccionArancelaria: request.LimpiarFraccionArancelaria,
            limpiarUnidadAduana: request.LimpiarUnidadAduana,
            limpiarPesoUnitarioKg: request.LimpiarPesoUnitarioKg);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
