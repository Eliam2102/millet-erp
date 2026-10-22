using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Lineas.ActualizarLinea;

public sealed class ActualizarLineaHandler : IRequestHandler<ActualizarLineaCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly Millet.CentrosCosto.Application.PublicPorts.ICentroCostoCapturaPort _capturaCentroCosto;
    private readonly Millet.Compras.Domain.Ports.IClaseActivoCompraReadPort? _clasesActivos;

    public ActualizarLineaHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        IDecimalesUnidadGuard decimalesGuard,
        Millet.CentrosCosto.Application.PublicPorts.ICentroCostoCapturaPort capturaCentroCosto,
        Millet.Compras.Domain.Ports.IClaseActivoCompraReadPort? clasesActivos = null)
    {
        _clasesActivos = clasesActivos;
        _db = db;
        _compartido = compartido;
        _decimalesGuard = decimalesGuard;
        _capturaCentroCosto = capturaCentroCosto;
    }

    public async Task<Unit> Handle(ActualizarLineaCommand command, CancellationToken cancellationToken)
    {
        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        // F7-PR1: validar articulo cross-table (existe + activo).
        var articulo = await _compartido.Articulos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == command.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{command.ArticuloId}'.");

        if (articulo.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "ARTICULO_INACTIVO",
                $"El artículo '{articulo.Clave}' está {articulo.Estatus} y no puede usarse en líneas.");
        }

        ArticulosPorClasificacion.Validar(requisicion.Clasificacion, articulo);
        // Solo se valida la clase si el cliente envía la clasificación; null = conservar la actual.
        var linea = requisicion.Lineas.FirstOrDefault(l => l.Id == command.LineaId);
        var claseEfectiva = command.ClaseActivoId ?? linea?.ClaseActivoId;
        if ((command.EsActivoFijo ?? linea?.EsActivoFijo) == true && (command.EsActivoFijo is not null || command.ClaseActivoId is not null)
            && (claseEfectiva is not Guid claseId || _clasesActivos is null || !await _clasesActivos.EsActivaAsync(claseId, cancellationToken)))
            throw new BusinessRuleException("COMPRA_CLASE_ACTIVO_INVALIDA", "Seleccione una clase de activo activa de la empresa.");

        // ADR-0046 Etapa 2: la cantidad no puede exceder los decimales de la
        // unidad del artículo (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(command.ArticuloId, command.Cantidad, command.UnidadMedida) },
            cancellationToken);

        var centroCostoId = await CentroCostoRqResolver.ResolverAsync(
            requisicion, _compartido, _capturaCentroCosto, command.CentroCostoId, cancellationToken);

        requisicion.ActualizarLineaEstructural(
            lineaId: command.LineaId,
            articuloId: command.ArticuloId,
            cantidad: command.Cantidad,
            unidadMedida: command.UnidadMedida,
            precioEstimado: Money.Of(command.PrecioEstimadoMonto ?? 0m, command.PrecioEstimadoMoneda ?? "MXN"),
            cuentaContableId: command.CuentaContableId,
            centroCostoId: centroCostoId,
            proyecto: command.Proyecto,
            fechaRequerida: command.FechaRequerida,
            esActivoFijo: command.EsActivoFijo,
            claseActivoId: command.ClaseActivoId,
            articuloEsServicio: articulo.Naturaleza == Naturaleza.Servicio);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
