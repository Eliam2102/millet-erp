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
    private readonly Millet.Almacen.Domain.Ports.IVarianteVidrioReadPort? _variantes; // opcional solo para pruebas unitarias sin medidas
    private readonly Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort _dim3ElegibilidadPort;
    private readonly Millet.Compras.Domain.Ports.IClaseActivoCompraReadPort? _clasesActivos;

    public ActualizarLineaOcHandler(
        ComprasDbContext db,
        IDecimalesUnidadGuard decimalesGuard,
        IArticuloReadPort articulos,
        Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort dim3ElegibilidadPort,
        Millet.Compras.Domain.Ports.IClaseActivoCompraReadPort? clasesActivos = null,
        Millet.Almacen.Domain.Ports.IVarianteVidrioReadPort? variantes = null)
    {
        _variantes = variantes;
        _clasesActivos = clasesActivos;
        _db = db;
        _decimalesGuard = decimalesGuard;
        _articulos = articulos;
        _dim3ElegibilidadPort = dim3ElegibilidadPort;
    }

    public async Task Handle(ActualizarLineaOcCommand command, CancellationToken cancellationToken)
    {
        await using var tx = await SaldoCompraRq.BloquearAsync(_db, cancellationToken);
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        var lineaActual = oc.Lineas.FirstOrDefault(l => l.Id == command.LineaId);

        if (lineaActual?.LineaRequisicionId is Guid lineaRqId && command.Cantidad is decimal nuevaCantidad)
        {
            var saldos = await SaldoCompraRq.ObtenerAsync(_db, [lineaRqId], cancellationToken, lineaActual.Id);
            SaldoCompraRq.Validar(nuevaCantidad, saldos.GetValueOrDefault(lineaRqId));
        }

        // Solo se valida la clase si el cliente envía la clasificación; null = conservar la actual.
        var claseEfectiva = command.ClaseActivoId ?? lineaActual?.ClaseActivoId;
        if ((command.EsActivoFijo ?? lineaActual?.EsActivoFijo) == true && (command.EsActivoFijo is not null || command.ClaseActivoId is not null)
            && (claseEfectiva is not Guid claseId || _clasesActivos is null || !await _clasesActivos.EsActivaAsync(claseId, cancellationToken)))
            throw new BusinessRuleException("COMPRA_CLASE_ACTIVO_INVALIDA", "Seleccione una clase de activo activa de la empresa.");

        // F1-MP-01 (E1, A4.1-b): la medida se valida contra el artículo resultante (el del PATCH o el actual). Si cambia
        // el artículo, la medida anterior no se conserva (el dominio la limpia): vidrio crudo ⇒ hay que elegir otra.
        var articuloId = command.ArticuloId ?? lineaActual?.ArticuloId;
        Millet.Almacen.Domain.Ports.VarianteVidrioLectura? medida = null;
        if (articuloId is Guid aid)
        {
            var lectura = await _articulos.ObtenerPorIdsAsync([aid], cancellationToken);
            var cambiaArticulo = lineaActual is not null && aid != lineaActual.ArticuloId;
            medida = await OcLineaMedida.ResolverAsync(
                lectura.TryGetValue(aid, out var art) && art.TieneProductoAw, _variantes, aid,
                command.ProductoAwVarianteId ?? (cambiaArticulo ? null : lineaActual?.ProductoAwVarianteId), cancellationToken);
        }

        // ADR-0046 Etapa 2: si el PATCH cambia la cantidad, valida sus decimales
        // contra la unidad del artículo. El articuloId puede venir en el comando
        // o conservarse de la línea actual (PATCH parcial). FK NULL → no valida.
        // Con medida los m² salen de hojas enteras (D8): Derivada.
        if (command.Cantidad is decimal cantidad && articuloId is Guid aid2)
        {
            await _decimalesGuard.ValidarAsync(
                new[]
                {
                    new CantidadAValidar(aid2, cantidad, command.UnidadMedida ?? lineaActual?.UnidadMedida, Derivada: medida is not null),
                },
                cancellationToken);
        }

        // G1.11 / ADR-0050 §2: en línea manual de OC (sin RQ previa), el comprador
        // captura por proxy. Valida existe + activo, SIN alcance.
        // Si la línea es heredada (LineaRequisicionId != null), no se evalúa aquí
        // para permitir que el dominio lance LINEA_OC_CC_HEREDADO_INMUTABLE.
        if (command.CentroCostoId is Guid ccId && lineaActual is { LineaRequisicionId: null })
        {
            await CentroCostoLineaGuard.ValidarAsync(
                _dim3ElegibilidadPort,
                ccId,
                aplicarAlcance: false,
                cancellationToken);
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
            centroCostoId: command.CentroCostoId,
            fraccionArancelaria: command.FraccionArancelaria,
            limpiarFraccionArancelaria: command.LimpiarFraccionArancelaria,
            esActivoFijo: command.EsActivoFijo,
            claseActivoId: command.ClaseActivoId,
            // Solo si el PATCH trae medida; sin ella el dominio conserva (o limpia al cambiar de artículo) y revalida hojas enteras.
            productoAwVarianteId: command.ProductoAwVarianteId is null ? null : medida?.VarianteId,
            anchoMm: command.ProductoAwVarianteId is null ? null : medida?.AnchoMm,
            altoMm: command.ProductoAwVarianteId is null ? null : medida?.AltoMm);

        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}
