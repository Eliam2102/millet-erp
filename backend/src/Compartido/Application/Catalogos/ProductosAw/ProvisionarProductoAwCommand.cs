using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// Auto-provisión de producto de venta desde A+W (ADR-0048 D5; cierra la
/// mitad DatosMaestros de PLATFORM-TODO(&lt;MasterProvisioningAw&gt;)).
/// <b>Upsert idempotente por <see cref="ProductoAw.ReferenciaExterna"/></b>
/// (PROD_ID). Al crear: resuelve el FK al catálogo <c>UnidadMedida</c> por
/// código (ADR-0046) si existe, y acepta la clave unidad SAT sugerida por el
/// mapeo del caller. La clave prod/serv SAT queda pendiente para el operador
/// (bloquea timbrado, no alta). Si ya existe, NO pisa lo completado a mano.
/// </summary>
public sealed record ProvisionarProductoAwCommand(
    string ReferenciaExterna,
    string Descripcion,
    string UnidadMedida,
    string? ClaveUnidadSatSugerida,
    string? FraccionArancelaria = null,
    decimal? PesoUnitarioKg = null) : IRequest<ProvisionarProductoAwResponse>;

public sealed record ProvisionarProductoAwResponse(
    Guid ProductoId,
    string Descripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string Origen,
    bool Creado);

public sealed class ProvisionarProductoAwValidator
    : AbstractValidator<ProvisionarProductoAwCommand>
{
    public ProvisionarProductoAwValidator()
    {
        RuleFor(c => c.ReferenciaExterna).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Descripcion).NotEmpty().MaximumLength(254);
        RuleFor(c => c.UnidadMedida).NotEmpty().MaximumLength(20);
        RuleFor(c => c.ClaveUnidadSatSugerida!).MaximumLength(5)
            .When(c => c.ClaveUnidadSatSugerida is not null);
        RuleFor(c => c.FraccionArancelaria!).Matches(@"^\d{8,10}$")
            .When(c => c.FraccionArancelaria is not null);
        RuleFor(c => c.PesoUnitarioKg).GreaterThanOrEqualTo(0m)
            .When(c => c.PesoUnitarioKg.HasValue);
    }
}

public sealed class ProvisionarProductoAwHandler
    : IRequestHandler<ProvisionarProductoAwCommand, ProvisionarProductoAwResponse>
{
    private readonly CompartidoDbContext _db;

    public ProvisionarProductoAwHandler(CompartidoDbContext db) => _db = db;

    public async Task<ProvisionarProductoAwResponse> Handle(
        ProvisionarProductoAwCommand request, CancellationToken cancellationToken)
    {
        var existente = await _db.ProductosAw
            .FirstOrDefaultAsync(p => p.ReferenciaExterna == request.ReferenciaExterna, cancellationToken);
        if (existente is not null)
            return Respuesta(existente, creado: false);

        // ADR-0046: si la unidad de A+W (M2/PZA/ML/KG) existe en el catálogo,
        // se liga el FK; si no, queda el snapshot string y se reconcilia después.
        var unidad = await _db.UnidadesMedida.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Codigo == request.UnidadMedida, cancellationToken);

        var producto = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: request.ReferenciaExterna,
            descripcion: request.Descripcion,
            unidadMedida: request.UnidadMedida,
            origen: OrigenMaster.Aw,
            unidadMedidaId: unidad?.Id,
            claveUnidadSat: request.ClaveUnidadSatSugerida,
            fraccionArancelaria: request.FraccionArancelaria,
            pesoUnitarioKg: request.PesoUnitarioKg);

        _db.ProductosAw.Add(producto);
        await _db.SaveChangesAsync(cancellationToken);

        return Respuesta(producto, creado: true);
    }

    private static ProvisionarProductoAwResponse Respuesta(ProductoAw p, bool creado) => new(
        p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
        p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
        p.Origen.ToString(), creado);
}
