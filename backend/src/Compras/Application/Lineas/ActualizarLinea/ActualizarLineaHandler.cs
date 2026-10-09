using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private readonly Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort _dim3ElegibilidadPort;

    public ActualizarLineaHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        IDecimalesUnidadGuard decimalesGuard,
        Millet.CentrosCosto.Application.PublicPorts.IDim3ElegibilidadPort dim3ElegibilidadPort)
    {
        _db = db;
        _compartido = compartido;
        _decimalesGuard = decimalesGuard;
        _dim3ElegibilidadPort = dim3ElegibilidadPort;
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

        // ADR-0046 Etapa 2: la cantidad no puede exceder los decimales de la
        // unidad del artículo (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(command.ArticuloId, command.Cantidad, command.UnidadMedida) },
            cancellationToken);

        // G1.11 / ADR-0050 §1: línea de RQ es captura del dueño del gasto.
        // Valida existe + activo + en alcance del usuario actual.
        if (command.CentroCostoId is Guid centroCostoId)
        {
            await CentroCostoLineaGuard.ValidarAsync(
                _dim3ElegibilidadPort,
                centroCostoId,
                aplicarAlcance: true,
                cancellationToken);
        }

        requisicion.ActualizarLineaEstructural(
            lineaId: command.LineaId,
            articuloId: command.ArticuloId,
            cantidad: command.Cantidad,
            unidadMedida: command.UnidadMedida,
            precioEstimado: Money.Of(command.PrecioEstimadoMonto, command.PrecioEstimadoMoneda),
            cuentaContableId: command.CuentaContableId,
            centroCostoId: command.CentroCostoId,
            proyecto: command.Proyecto,
            fechaRequerida: command.FechaRequerida);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
