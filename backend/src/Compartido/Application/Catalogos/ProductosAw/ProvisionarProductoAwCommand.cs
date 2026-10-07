using FluentValidation;
using MediatR;
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
    private readonly AplicarProductoAwService _aplicar;

    public ProvisionarProductoAwHandler(AplicarProductoAwService aplicar) => _aplicar = aplicar;

    public async Task<ProvisionarProductoAwResponse> Handle(
        ProvisionarProductoAwCommand request, CancellationToken cancellationToken)
    {
        // Mismo punto de escritura que la sincronización; SoloCrear = no toca un producto existente.
        var r = await _aplicar.AplicarAsync(new AplicarProductoAwSnapshot(
            request.ReferenciaExterna, request.Descripcion, request.UnidadMedida, Baja: false, [],
            DateTime.UtcNow, VersionContrato: "pedido", VersionMapeo: "pedido",
            ClaveUnidadSatSugerida: request.ClaveUnidadSatSugerida,
            FraccionArancelaria: request.FraccionArancelaria,
            PesoUnitarioKg: request.PesoUnitarioKg,
            SoloCrear: true), cancellationToken);

        return Respuesta(r.Producto!, creado: r.Accion == AplicarProductoAwAccion.Creado);
    }

    private static ProvisionarProductoAwResponse Respuesta(ProductoAw p, bool creado) => new(
        p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
        p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
        p.Origen.ToString(), creado);
}
