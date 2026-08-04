using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarLinea;

public sealed class ActualizarLineaOcHandler : IRequestHandler<ActualizarLineaOcCommand>
{
    private readonly ComprasDbContext _db;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IArticuloReadPort _articulos;

    public ActualizarLineaOcHandler(
        ComprasDbContext db,
        IDecimalesUnidadGuard decimalesGuard,
        IArticuloReadPort articulos)
    {
        _db = db;
        _decimalesGuard = decimalesGuard;
        _articulos = articulos;
    }

    public async Task Handle(ActualizarLineaOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        // ADR-0046 Etapa 2: si el PATCH cambia la cantidad, valida sus decimales
        // contra la unidad del artículo. El articuloId puede venir en el comando
        // o conservarse de la línea actual (PATCH parcial). FK NULL → no valida.
        if (command.Cantidad is decimal cantidad)
        {
            var lineaActual = oc.Lineas.FirstOrDefault(l => l.Id == command.LineaId);
            var articuloId = command.ArticuloId ?? lineaActual?.ArticuloId;
            if (articuloId is Guid aid)
            {
                await _decimalesGuard.ValidarAsync(
                    new[]
                    {
                        new CantidadAValidar(aid, cantidad, command.UnidadMedida ?? lineaActual?.UnidadMedida),
                    },
                    cancellationToken);
            }
        }

        DescuentoLinea? descuento = (command.DescuentoTipo, command.DescuentoValor) switch
        {
            (DescuentoTipo t, decimal v) => new DescuentoLinea(t, v),
            _ => null,
        };

        IndicadorImpuestos? indicador = command.IndicadorImpuestos is not null
            ? new IndicadorImpuestos(command.IndicadorImpuestos)
            : null;

        // GAP-9: si el PATCH cambia el artículo, re-resolver la naturaleza
        // para actualizar el snapshot EsServicio de la línea. Artículo no
        // encontrado → false. Sin cambio de artículo → null (no tocar).
        bool? esServicio = null;
        if (command.ArticuloId is Guid articuloNuevo)
        {
            var articulos = await _articulos.ObtenerPorIdsAsync(
                [articuloNuevo], cancellationToken);
            esServicio = articulos.TryGetValue(articuloNuevo, out var articulo)
                && articulo.EsServicio;
        }

        oc.ActualizarLinea(
            lineaId: command.LineaId,
            articuloId: command.ArticuloId,
            cantidad: command.Cantidad,
            unidadMedida: command.UnidadMedida,
            precioUnitario: command.PrecioUnitario,
            descuento: descuento,
            indicadorImpuestos: indicador,
            departamentoSolicitanteId: command.DepartamentoSolicitanteId,
            descripcionExtendida: command.DescripcionExtendida,
            fechaEntregaLinea: command.FechaEntregaLinea,
            limpiarDescripcionExtendida: command.LimpiarDescripcionExtendida,
            limpiarFechaEntregaLinea: command.LimpiarFechaEntregaLinea,
            esServicio: esServicio,
            centroCostoId: command.CentroCostoId);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
