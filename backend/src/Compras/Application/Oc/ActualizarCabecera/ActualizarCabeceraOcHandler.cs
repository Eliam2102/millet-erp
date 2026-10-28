using MediatR;
using Millet.Compras.Domain.Ports.Catalogos;
using Millet.Compras.Application.Proveedores;
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
    private readonly ICatalogoSatReadPort _catalogos;
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;

    public ActualizarCabeceraOcHandler(ComprasDbContext db, ICatalogoSatReadPort catalogos, CompartidoDbContext compartido)
    {
        _db = db;
        _catalogos = catalogos;
        _compartido = compartido;
    }

    public async Task Handle(ActualizarCabeceraOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        await DatosFacturaOcGuard.ValidarAsync(_catalogos, command.UsoCfdiClave, command.FormaPagoClave, cancellationToken);

        // D1: con cotización elegida, proveedor y moneda quedan fijos a los de la cotización.
        var cambiaProveedor = command.ProveedorId is Guid pid && pid != oc.ProveedorId;
        var cambiaMoneda = command.Moneda is string mon
            && !string.Equals(mon.Trim(), oc.Moneda, StringComparison.OrdinalIgnoreCase);
        if ((cambiaProveedor || cambiaMoneda)
            && await _db.Cotizaciones.AnyAsync(c => c.OrdenCompraId == oc.Id, cancellationToken))
        {
            throw new BusinessRuleException("OC_COTIZACION_FIJA",
                "La orden de compra viene de una cotización elegida; no se puede cambiar el proveedor ni la moneda.");
        }

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
                throw ProveedorNoUtilizable.Error(proveedor.Clave, proveedor.Estatus, $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede usarse en OCs.");
            }
        }

        if (command.ProveedorId is Guid pFijo)
        {
            var rqsApp = await _db.Requisiciones
                .Where(r => r.Origen == Millet.Compras.Domain.OrigenRequisicion.AlmacenesApp
                    && _db.OrdenesCompra.Where(o => o.Id == oc.Id).SelectMany(o => o.Lineas)
                        .Any(l => l.RequisicionId == r.Id))
                .ToListAsync(cancellationToken);
            foreach (var rq in rqsApp) rq.ExigirProveedorFijo(pFijo);
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

        oc.ActualizarDatosFactura(command.UsoCfdiClave, command.MetodoPago, command.FormaPagoClave);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
