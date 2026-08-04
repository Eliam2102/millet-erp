using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;

/// <summary>
/// Ingesta una orden de Planta Pintura exigiendo master preexistente (sin
/// auto-provisión, a diferencia de A+W): resuelve cliente y artículos por sus
/// referencias y, si falta alguno, registra una excepción en la bandeja en lugar
/// de crear el pedido. Idempotente por <c>(PlantaPintura, numero_pedido)</c> vía
/// <see cref="IngestaControl"/>.
/// </summary>
public sealed class ImportarPedidoPlantaPinturaHandler
    : IRequestHandler<ImportarPedidoPlantaPinturaCommand, ImportarPedidoPlantaPinturaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClientesReadPort _clientes;
    private readonly IProductosReadPort _productos;
    private readonly IClock _clock;

    public ImportarPedidoPlantaPinturaHandler(
        FacturacionDbContext db, IClientesReadPort clientes, IProductosReadPort productos, IClock clock)
    {
        _db = db;
        _clientes = clientes;
        _productos = productos;
        _clock = clock;
    }

    public async Task<ImportarPedidoPlantaPinturaResponse> Handle(
        ImportarPedidoPlantaPinturaCommand command, CancellationToken cancellationToken)
    {
        var ahora = _clock.UtcNow;
        var pedido = command.Pedido;

        // Idempotencia: ¿ya se ingestó esta clave con una versión >= ?
        var control = await _db.IngestaControles
            .FirstOrDefaultAsync(c => c.Origen == OrigenPedido.PlantaPintura && c.ClaveNatural == pedido.NumeroPedido, cancellationToken);
        if (control is not null && control.UltimaVersionAplicada >= pedido.VersionOrigen)
            return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Pospuesta, control.PedidoFacturableId, null);

        // Master preexistente obligatorio (sin auto-provisión).
        var cliente = await _clientes.ResolverPorReferenciaAsync(pedido.ClienteRef, cancellationToken);
        if (cliente is null)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ClienteNoExiste,
                $"Cliente '{pedido.ClienteRef}' no existe en el ERP (Planta Pintura exige preexistencia).", cancellationToken);

        var productos = new Dictionary<string, ProductoFiscalLectura>();
        foreach (var l in pedido.Lineas)
        {
            if (productos.ContainsKey(l.ProductoRef)) continue;
            var prod = await _productos.ResolverPorReferenciaAsync(l.ProductoRef, cancellationToken);
            if (prod is null)
                return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ArticuloNoExiste,
                    $"Artículo '{l.ProductoRef}' no existe en el ERP (Planta Pintura exige preexistencia).", cancellationToken);
            productos[l.ProductoRef] = prod;
        }

        // Crear el pedido facturable.
        var nuevo = PedidoFacturable.ImportarDesdePlantaPintura(
            command.EmpresaId, pedido.NumeroPedido, pedido.SucursalId, cliente.ClienteId, cliente.RazonSocial,
            pedido.CanalVenta, pedido.ComportamientoFiscal, pedido.Moneda, pedido.ObraId, pedido.ObraNombre,
            pedido.Comentarios, pedido.VersionOrigen, pedido.EstadoOrigen);

        foreach (var l in pedido.Lineas)
        {
            var prod = productos[l.ProductoRef];
            nuevo.AgregarLinea(prod.ProductoId, l.Descripcion, l.ClaveProdServSat ?? prod.ClaveProdServSat,
                l.ClaveUnidadSat ?? prod.ClaveUnidadSat, l.Cantidad, l.Precio, l.Descuento, l.RequierePedimento);
        }
        nuevo.RecalcularTotal();

        var hash = pedido.PayloadCrudo.GetHashCode().ToString("x8");
        if (control is null)
            _db.IngestaControles.Add(IngestaControl.Crear(command.EmpresaId, OrigenPedido.PlantaPintura, pedido.NumeroPedido,
                hash, pedido.VersionOrigen, EstadoIngesta.Importado, nuevo.Id, ahora));
        else
            control.Aplicar(hash, pedido.VersionOrigen, EstadoIngesta.Importado, nuevo.Id, ahora);

        _db.PedidosFacturables.Add(nuevo);
        _db.PedidosFacturablesSnapshot.Add(PedidoFacturableSnapshot.Crear(nuevo.Id, pedido.PayloadCrudo, ahora));
        await _db.SaveChangesAsync(cancellationToken);

        return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Aplicada, nuevo.Id, null);
    }

    private async Task<ImportarPedidoPlantaPinturaResponse> RegistrarExcepcionAsync(
        Guid empresaId, string numeroPedido, MotivoExcepcion motivo, string detalle, CancellationToken cancellationToken)
    {
        _db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(empresaId, OrigenPedido.PlantaPintura, numeroPedido, motivo, detalle));
        await _db.SaveChangesAsync(cancellationToken);
        return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Rechazada, null, motivo);
    }
}
