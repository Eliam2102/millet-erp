using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Apartados;

/// <summary>Consulta la existencia apartada por línea de una requisición (86e3tferc).</summary>
public sealed record ObtenerApartadosRequisicionQuery(Guid RequisicionId) : IRequest<IReadOnlyList<ApartadoLineaResponse>>;

/// <summary>Apartado de una línea expresado en la unidad de documento de la línea.</summary>
public sealed record ApartadoLineaResponse(Guid LineaId, decimal Pendiente, decimal Liberada, string UnidadMedida, string? UltimoMotivo);

public sealed class ObtenerApartadosRequisicionHandler(
    ComprasDbContext db,
    TransaccionApartadosRq apartadosTx,
    IConversionUnidadPort conversion)
    : IRequestHandler<ObtenerApartadosRequisicionQuery, IReadOnlyList<ApartadoLineaResponse>>
{
    public async Task<IReadOnlyList<ApartadoLineaResponse>> Handle(ObtenerApartadosRequisicionQuery query, CancellationToken cancellationToken)
    {
        var requisicion = await db.Requisiciones.AsNoTracking()
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == query.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{query.RequisicionId}' en la empresa actual.");

        var apartados = await apartadosTx.Apartados.ConsultarPorRequisicionAsync(requisicion.Id, cancellationToken);
        var resultado = new List<ApartadoLineaResponse>();
        foreach (var apartado in apartados)
        {
            var linea = requisicion.Lineas.FirstOrDefault(l => l.Id == apartado.LineaRequisicionId);
            if (linea is null) continue;
            var c = await conversion.ConvertirAsync(linea.ArticuloId, 1m, null, linea.UnidadMedida, cancellationToken);
            resultado.Add(new ApartadoLineaResponse(
                linea.Id,
                apartado.Pendiente / c.FactorDocumentoABase,
                apartado.Liberada / c.FactorDocumentoABase,
                linea.UnidadMedida,
                apartado.UltimoMotivo));
        }
        return resultado;
    }
}
