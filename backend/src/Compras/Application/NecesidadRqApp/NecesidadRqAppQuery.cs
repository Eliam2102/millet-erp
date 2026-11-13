using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Disponibilidad;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.NecesidadRqApp;

/// <summary>
/// M-332 (Fernando, 10-nov; plano 04 §7.2 paso 7): vista de consulta de la necesidad de una RQ de la app de Almacenes. La RQ ya viene
/// descontada por el jefe de almacén y se compra completa; esta vista solo muestra, por línea, la necesidad, lo disponible hoy, lo ya
/// convertido a OC y lo pendiente. Si el disponible cubre la necesidad se avisa, pero Compras decide. Solo lectura: no reserva ni cambia la RQ.
/// </summary>
public sealed record NecesidadRqAppQuery(Guid RequisicionId) : IRequest<NecesidadRqAppResponse>;

public sealed class NecesidadRqAppQueryValidator : AbstractValidator<NecesidadRqAppQuery>
{
    public NecesidadRqAppQueryValidator() => RuleFor(x => x.RequisicionId).NotEmpty();
}

/// <summary><c>Aplica = false</c> (sin líneas) cuando la RQ no viene de la app de Almacenes.</summary>
public sealed record NecesidadRqAppResponse(Guid RequisicionId, bool Aplica, string? FolioExterno, IReadOnlyList<NecesidadRqAppLinea> Lineas);

public sealed record NecesidadRqAppLinea(
    Guid LineaId,
    short Posicion,
    Guid ArticuloId,
    string? ArticuloClave,
    string? ArticuloNombre,
    string UnidadMedida,
    int? Piezas,
    decimal Necesidad,
    decimal Disponible,
    decimal ConvertidoACompra,
    decimal Reducido,
    decimal Pendiente,
    bool DisponibleCubre);

public sealed class NecesidadRqAppHandler(
    ComprasDbContext db, IConsultarStockPort stock, IArticuloReadPort articulos, IConversionUnidadPort conversion)
    : IRequestHandler<NecesidadRqAppQuery, NecesidadRqAppResponse>
{
    public async Task<NecesidadRqAppResponse> Handle(NecesidadRqAppQuery request, CancellationToken cancellationToken)
    {
        var rq = await db.Requisiciones.AsNoTracking().Include(r => r.Lineas).FirstOrDefaultAsync(r => r.Id == request.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA", $"No se encontró requisición con id '{request.RequisicionId}' en la empresa actual.");
        if (rq.Origen != OrigenRequisicion.AlmacenesApp)
            return new NecesidadRqAppResponse(rq.Id, false, rq.FolioExterno, []);

        var lineaIds = rq.Lineas.Select(l => l.Id).ToList();
        // Cantidad en líneas de OC ligadas a la línea de la RQ; una OC cancelada o rechazada ya no cuenta como convertida.
        var convertidoPorLinea = (await (
            from l in db.LineasOrdenCompra.AsNoTracking()
            join oc in db.OrdenesCompra.AsNoTracking() on l.OrdenCompraId equals oc.Id
            where l.LineaRequisicionId != null && lineaIds.Contains(l.LineaRequisicionId.Value)
               && oc.Estado != EstadoOrdenCompra.Cancelada && oc.Estado != EstadoOrdenCompra.Rechazada
            select new { LineaId = l.LineaRequisicionId!.Value, l.Cantidad }).ToListAsync(cancellationToken))
            .GroupBy(x => x.LineaId).ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

        var arts = await articulos.ObtenerPorIdsAsync(rq.Lineas.Select(l => l.ArticuloId).Distinct().ToList(), cancellationToken);
        var lineas = new List<NecesidadRqAppLinea>();
        foreach (var linea in rq.Lineas.OrderBy(l => l.Posicion))
        {
            // Una línea por llamada: cada una muestra el disponible completo, sin descontar lo que "tomaría" otra línea del mismo artículo.
            var reparto = (await RepartoDisponibilidadService.RepartirAsync(
                stock, conversion, arts, rq.Clasificacion, rq.SucursalId, [linea], cancellationToken))[0];
            lineas.Add(Construir(linea, arts.GetValueOrDefault(linea.ArticuloId), reparto.Disponible, convertidoPorLinea.GetValueOrDefault(linea.Id)));
        }
        return new NecesidadRqAppResponse(rq.Id, true, rq.FolioExterno, lineas);
    }

    /// <summary>Pendiente = necesidad − convertido − reducido (lo que la app avisó que ya no se compra), con piso en 0.</summary>
    internal static NecesidadRqAppLinea Construir(LineaRequisicion linea, ArticuloLectura? articulo, decimal disponible, decimal convertido) =>
        new(linea.Id, linea.Posicion, linea.ArticuloId, articulo?.Clave, articulo?.Nombre, linea.UnidadMedida, linea.Piezas,
            linea.Cantidad, disponible, convertido, linea.CantidadSinCompra,
            Math.Max(0m, linea.Cantidad - convertido - linea.CantidadSinCompra), disponible >= linea.Cantidad);
}
