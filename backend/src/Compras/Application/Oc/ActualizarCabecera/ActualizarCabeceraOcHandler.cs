using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Oc.ActualizarCabecera;

/// <summary>
/// Aplica el PATCH parcial sobre la cabecera. Lookup por id (query filter
/// cubre empresa+soft-delete). Si cambia <c>ProveedorId</c>, valida que
/// el nuevo esté activo (C10).
/// </summary>
public sealed class ActualizarCabeceraOcHandler : IRequestHandler<ActualizarCabeceraOcCommand>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;

    public ActualizarCabeceraOcHandler(ComprasDbContext db, CompartidoDbContext compartido)
    {
        _db = db;
        _compartido = compartido;
    }

    public async Task Handle(ActualizarCabeceraOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        // C10 — validar proveedor activo si cambia.
        if (command.ProveedorId is Guid nuevoProveedorId && nuevoProveedorId != oc.ProveedorId)
        {
            var proveedor = await _compartido.Proveedores
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == nuevoProveedorId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "PROVEEDOR_NO_ENCONTRADO",
                    $"No se encontró proveedor con id '{nuevoProveedorId}'.");

            if (proveedor.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "PROVEEDOR_INACTIVO",
                    $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede usarse en OCs.");
            }
        }

        oc.ActualizarCabecera(
            proveedorId: command.ProveedorId,
            condicionesPagoId: command.CondicionesPagoId,
            usoPrincipalId: command.UsoPrincipalId,
            encargadoComprasId: command.EncargadoComprasId,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            esImportacion: command.EsImportacion,
            cotizacionExcepcionada: command.CotizacionExcepcionada,
            observaciones: command.Observaciones,
            fechaEntregaEsperada: command.FechaEntregaEsperada,
            descuentoGlobalTipo: command.DescuentoGlobalTipo,
            descuentoGlobalValor: command.DescuentoGlobalValor,
            gastosAdicionales: command.GastosAdicionales,
            redondeo: command.Redondeo,
            limpiarObservaciones: command.LimpiarObservaciones,
            limpiarFechaEntregaEsperada: command.LimpiarFechaEntregaEsperada,
            limpiarDescuentoGlobal: command.LimpiarDescuentoGlobal,
            limpiarTipoCambio: command.LimpiarTipoCambio);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
