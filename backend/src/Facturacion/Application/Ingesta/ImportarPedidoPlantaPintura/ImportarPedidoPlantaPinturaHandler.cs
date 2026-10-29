using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

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

        // Idempotencia: la orden ya creó su pedido (cualquier versión) o esta versión ya se aplicó.
        var control = await _db.IngestaControles
            .FirstOrDefaultAsync(c => c.Origen == OrigenPedido.PlantaPintura && c.ClaveNatural == pedido.NumeroPedido, cancellationToken);
        if (control is not null && (control.PedidoFacturableId is not null || control.UltimaVersionAplicada >= pedido.VersionOrigen))
            return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Pospuesta, control.PedidoFacturableId, null);

        // Master preexistente obligatorio (sin auto-provisión) y validación antes de crear.
        var cliente = await _clientes.ResolverPorReferenciaAsync(pedido.ClienteRef, cancellationToken);
        if (cliente is null)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ClienteNoExiste,
                $"Cliente '{pedido.ClienteRef}' no existe en el ERP (Planta Pintura exige preexistencia).", cancellationToken);
        if (!cliente.Activo)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro,
                $"Cliente '{pedido.ClienteRef}' está inactivo en el ERP.", cancellationToken);

        if (pedido.Lineas.Count != 1)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro,
                $"La orden debe traer exactamente una línea (trae {pedido.Lineas.Count}).", cancellationToken);

        var l = pedido.Lineas[0];
        if (l.Cantidad <= 0)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro,
                "La cantidad debe ser mayor a 0.", cancellationToken);
        if (l.Precio < 0)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro,
                "El precio no puede ser negativo.", cancellationToken);

        var prod = await _productos.ResolverPorReferenciaAsync(l.ProductoRef, cancellationToken);
        if (prod is null)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ArticuloNoExiste,
                $"Artículo '{l.ProductoRef}' no existe en el ERP (Planta Pintura exige preexistencia).", cancellationToken);
        if (!prod.Activo)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro,
                $"Artículo '{l.ProductoRef}' está inactivo en el ERP.", cancellationToken);
        if (!prod.EsServicioRecubrimiento)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ServicioNoMarcado,
                $"Artículo '{l.ProductoRef}' no está marcado como servicio de recubrimiento.", cancellationToken);
        if (string.IsNullOrWhiteSpace(l.ClaveProdServSat ?? prod.ClaveProdServSat)
            || string.IsNullOrWhiteSpace(l.ClaveUnidadSat ?? prod.ClaveUnidadSat)
            || string.IsNullOrWhiteSpace(prod.ObjetoImp) || prod.TasaIvaTraslado is null)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.ProductoSinClaveSat,
                $"Artículo '{l.ProductoRef}' tiene datos fiscales incompletos (clave SAT, unidad, objeto de impuesto o tasa de IVA).", cancellationToken);
        if (l.TasaIva is decimal tasa && tasa != prod.TasaIvaTraslado)
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.IvaNoCuadra,
                $"IVA de la orden ({tasa}) no coincide con la tasa del artículo ({prod.TasaIvaTraslado}).", cancellationToken);

        // Crear el pedido facturable.
        PedidoFacturable nuevo;
        try
        {
            nuevo = PedidoFacturable.ImportarDesdePlantaPintura(
                command.EmpresaId, pedido.NumeroPedido, pedido.SucursalId, cliente.ClienteId, cliente.RazonSocial,
                pedido.CanalVenta, pedido.ComportamientoFiscal, pedido.Moneda, pedido.ObraId, pedido.ObraNombre,
                pedido.Comentarios, pedido.VersionOrigen, pedido.EstadoOrigen);
            nuevo.AgregarLinea(prod.ProductoId, l.Descripcion, l.ClaveProdServSat ?? prod.ClaveProdServSat,
                l.ClaveUnidadSat ?? prod.ClaveUnidadSat, l.Cantidad, l.Precio, l.Descuento, l.RequierePedimento);
            nuevo.RecalcularTotal();
        }
        catch (BusinessRuleException ex)
        {
            return await RegistrarExcepcionAsync(command.EmpresaId, pedido.NumeroPedido, MotivoExcepcion.Otro, ex.Message, cancellationToken);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pedido.PayloadCrudo)));
        if (control is null)
            _db.IngestaControles.Add(IngestaControl.Crear(command.EmpresaId, OrigenPedido.PlantaPintura, pedido.NumeroPedido,
                hash, pedido.VersionOrigen, EstadoIngesta.Importado, nuevo.Id, ahora));
        else
            control.Aplicar(hash, pedido.VersionOrigen, EstadoIngesta.Importado, nuevo.Id, ahora);

        foreach (var abierta in await _db.ExcepcionesImportacion
                     .Where(e => e.Origen == OrigenPedido.PlantaPintura && e.PedidoRef == pedido.NumeroPedido && !e.Resuelto)
                     .ToListAsync(cancellationToken))
            abierta.Resolver(usuarioId: null, ahora);

        _db.PedidosFacturables.Add(nuevo);
        _db.PedidosFacturablesSnapshot.Add(PedidoFacturableSnapshot.Crear(nuevo.Id, pedido.PayloadCrudo, ahora));
        await _db.SaveChangesAsync(cancellationToken);

        return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Aplicada, nuevo.Id, null);
    }

    private async Task<ImportarPedidoPlantaPinturaResponse> RegistrarExcepcionAsync(
        Guid empresaId, string numeroPedido, MotivoExcepcion motivo, string detalle, CancellationToken cancellationToken)
    {
        // Una sola excepción abierta por folio (se refresca si se re-observa).
        var abierta = await _db.ExcepcionesImportacion.FirstOrDefaultAsync(
            e => e.Origen == OrigenPedido.PlantaPintura && e.PedidoRef == numeroPedido && !e.Resuelto, cancellationToken);
        if (abierta is null)
            _db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(empresaId, OrigenPedido.PlantaPintura, numeroPedido, motivo, detalle));
        else
            abierta.Actualizar(motivo, detalle);
        await _db.SaveChangesAsync(cancellationToken);
        return new ImportarPedidoPlantaPinturaResponse(ResultadoSolicitudAw.Rechazada, null, motivo, detalle);
    }
}
