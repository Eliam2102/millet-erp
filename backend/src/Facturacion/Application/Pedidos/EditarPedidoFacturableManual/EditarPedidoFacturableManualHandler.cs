using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;

public sealed class EditarPedidoFacturableManualHandler
    : IRequestHandler<EditarPedidoFacturableManualCommand, PedidoFacturableResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IProductosReadPort _productos;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;

    public EditarPedidoFacturableManualHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        IProductosReadPort productos,
        IEmpresaFiscalReadPort empresasFiscal)
    {
        _db = db;
        _empresa = empresa;
        _productos = productos;
        _empresasFiscal = empresasFiscal;
    }

    public async Task<PedidoFacturableResponse> Handle(
        EditarPedidoFacturableManualCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var pedido = await _db.PedidosFacturables
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable '{command.Id}'.");

        if (pedido.Origen != OrigenPedido.Manual)
            throw new BusinessRuleException(
                "PEDIDO_NO_MANUAL",
                "Sólo los pedidos de origen Manual se editan por este flujo; los ingestados se refrescan desde su origen.");

        // Concurrencia optimista (ETag/If-Match): la versión esperada del cliente
        // debe coincidir con la actual; si no, alguien editó en medio (409).
        if (pedido.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PedidoFacturable), command.Id);

        pedido.EditarCabecera(
            clienteId: command.ClienteId,
            clienteNombre: command.ClienteNombre,
            canalVentaId: command.CanalVenta,
            comportamientoFiscal: command.ComportamientoFiscal,
            moneda: command.Moneda,
            obraId: command.ObraId,
            obraNombre: command.ObraNombre,
            comentarios: command.Comentarios);

        // FAC-DET-PR2: tasa de IVA por línea manual — artículo > empresa.
        var tasaIvaDe = await TasaIvaLineaManual.CrearResolverAsync(
            _productos, _empresasFiscal, empresaId,
            command.Lineas.Select(l => l.ProductoId), cancellationToken);

        pedido.LimpiarLineas();
        foreach (var l in command.Lineas)
        {
            pedido.AgregarLinea(
                productoId: l.ProductoId,
                productoDescripcion: l.ProductoDescripcion,
                claveProdServSat: l.ClaveProdServSat,
                claveUnidadSat: l.ClaveUnidadSat,
                cantidad: l.Cantidad,
                precio: l.Precio,
                descuento: l.Descuento,
                requierePedimento: l.RequierePedimento,
                tasaIva: tasaIvaDe(l.ProductoId));
        }

        pedido.RecalcularTotal();

        await _db.SaveChangesAsync(cancellationToken);

        return new PedidoFacturableResponse(pedido.Id, pedido.Estado.ToString(), pedido.Total, pedido.Version);
    }
}
