using MediatR;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;

public sealed class CrearPedidoFacturableManualHandler
    : IRequestHandler<CrearPedidoFacturableManualCommand, PedidoFacturableResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IProductosReadPort _productos;
    private readonly IEmpresaFiscalReadPort _empresasFiscal;

    public CrearPedidoFacturableManualHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IProductosReadPort productos,
        IEmpresaFiscalReadPort empresasFiscal)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _productos = productos;
        _empresasFiscal = empresasFiscal;
    }

    public async Task<PedidoFacturableResponse> Handle(
        CrearPedidoFacturableManualCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var pedido = PedidoFacturable.CrearManual(
            empresaId: empresaId,
            numeroPedido: command.NumeroPedido,
            sucursalId: command.SucursalId,
            clienteId: command.ClienteId,
            clienteNombre: command.ClienteNombre,
            canalVentaId: command.CanalVenta,
            comportamientoFiscal: command.ComportamientoFiscal,
            moneda: command.Moneda,
            obraId: command.ObraId,
            obraNombre: command.ObraNombre,
            comentarios: command.Comentarios,
            capturadoPor: _user.UserId);

        // FAC-DET-PR2: tasa de IVA por línea manual — artículo > empresa.
        var tasaIvaDe = await TasaIvaLineaManual.CrearResolverAsync(
            _productos, _empresasFiscal, empresaId,
            command.Lineas.Select(l => l.ProductoId), cancellationToken);

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

        _db.PedidosFacturables.Add(pedido);
        await _db.SaveChangesAsync(cancellationToken);

        return new PedidoFacturableResponse(pedido.Id, pedido.Estado.ToString(), pedido.Total, pedido.Version);
    }
}
