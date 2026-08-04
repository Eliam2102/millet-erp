using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;

/// <summary>
/// Handler de <see cref="AgregarLineaManualOcCommand"/>. Lookup de la OC
/// con sus líneas (Include), invoca <c>AgregarLineaManual</c> del agregado
/// y persiste. El agregado valida estado + bandera <c>SinRequisicionPrevia</c>
/// y devuelve la línea recién creada.
/// </summary>
public sealed class AgregarLineaManualOcHandler
    : IRequestHandler<AgregarLineaManualOcCommand, AgregarLineaManualOcResponse>
{
    private readonly ComprasDbContext _db;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IArticuloReadPort _articulos;

    public AgregarLineaManualOcHandler(
        ComprasDbContext db,
        IDecimalesUnidadGuard decimalesGuard,
        IArticuloReadPort articulos)
    {
        _db = db;
        _decimalesGuard = decimalesGuard;
        _articulos = articulos;
    }

    public async Task<AgregarLineaManualOcResponse> Handle(
        AgregarLineaManualOcCommand command,
        CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        DescuentoLinea? descuento = (command.DescuentoTipo, command.DescuentoValor) switch
        {
            (DescuentoTipo t, decimal v) => new DescuentoLinea(t, v),
            _ => null,
        };

        IndicadorImpuestos? indicador = command.IndicadorImpuestos is not null
            ? new IndicadorImpuestos(command.IndicadorImpuestos)
            : null;

        // ADR-0046 Etapa 2: la cantidad no puede exceder los decimales de la
        // unidad del artículo (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(command.ArticuloId, command.Cantidad, command.UnidadMedida) },
            cancellationToken);

        // GAP-9: snapshot de naturaleza — las líneas de servicio se excluyen
        // del sub-estado de Recepción. Artículo no encontrado → false.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            [command.ArticuloId], cancellationToken);
        var esServicio = articulos.TryGetValue(command.ArticuloId, out var articulo)
            && articulo.EsServicio;

        var linea = oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: command.ArticuloId,
            cantidad: command.Cantidad,
            unidadMedida: command.UnidadMedida,
            precioUnitario: command.PrecioUnitario,
            departamentoSolicitanteId: command.DepartamentoSolicitanteId,
            descuento: descuento,
            indicadorImpuestos: indicador,
            descripcionExtendida: command.DescripcionExtendida,
            fechaEntregaLinea: command.FechaEntregaLinea,
            textoAdicional: command.TextoAdicional,
            esServicio: esServicio,
            // Fase E PR3: CC-Máquina elegido por el comprador (proxy, abierto).
            centroCostoId: command.CentroCostoId);

        await _db.SaveChangesAsync(cancellationToken);

        return new AgregarLineaManualOcResponse(
            LineaId: linea.Id,
            Posicion: linea.Posicion,
            SubtotalLinea: linea.SubtotalLinea,
            IvaImporte: linea.IvaImporte);
    }
}
