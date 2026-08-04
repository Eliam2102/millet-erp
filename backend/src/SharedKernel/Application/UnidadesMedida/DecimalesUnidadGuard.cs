using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.UnidadesMedida;

/// <inheritdoc cref="IDecimalesUnidadGuard"/>
public sealed class DecimalesUnidadGuard : IDecimalesUnidadGuard
{
    private readonly IUnidadMedidaReadPort _unidades;

    public DecimalesUnidadGuard(IUnidadMedidaReadPort unidades) => _unidades = unidades;

    public async Task ValidarAsync(
        IEnumerable<CantidadAValidar> cantidades,
        CancellationToken cancellationToken)
    {
        var lineas = cantidades as IReadOnlyCollection<CantidadAValidar> ?? cantidades.ToList();
        if (lineas.Count == 0)
        {
            return;
        }

        var articuloIds = lineas.Select(l => l.ArticuloId).Distinct().ToList();
        var decimalesPorArticulo = await _unidades
            .ObtenerDecimalesPorArticulosAsync(articuloIds, cancellationToken);

        foreach (var linea in lineas)
        {
            // FK NULL o artículo no resuelto → no se valida (Etapa 2: la regla
            // solo aplica cuando el artículo resuelve a una unidad del catálogo).
            if (!decimalesPorArticulo.TryGetValue(linea.ArticuloId, out var decimales)
                || decimales is not int permitidos)
            {
                continue;
            }

            if (!DecimalesUnidad.EsValida(linea.Cantidad, permitidos))
            {
                var unidad = string.IsNullOrWhiteSpace(linea.UnidadEtiqueta)
                    ? "del artículo"
                    : $"«{linea.UnidadEtiqueta}»";
                throw new BusinessRuleException(
                    DecimalesUnidad.CodigoError,
                    $"La unidad {unidad} permite máximo {permitidos} "
                    + $"{(permitidos == 1 ? "decimal" : "decimales")}; "
                    + $"«{linea.Cantidad}» tiene más.");
            }
        }
    }
}
