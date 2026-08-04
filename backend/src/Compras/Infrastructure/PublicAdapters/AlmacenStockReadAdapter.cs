using Millet.Almacen.Domain.Ports.Externos;
using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IConsultarStockPort"/> declarado en
/// <c>Compras.Domain.Ports.Almacen</c>. Reemplaza el
/// <c>InMemoryConsultarStockPort</c> stub que devolvía stock ficticio.
///
/// <para><b>Bug histórico que cierra este adapter</b>: con el stub activo, al
/// autorizar una RQ de un artículo YA recibido en almacén, el handler recibía
/// <c>Disponible = 0</c> → todo el monto iba a <c>CantidadDeCompra</c> (pedía
/// comprar lo que ya había físicamente).</para>
///
/// <para><b>ADR-0047 PR2</b>: este adapter ya NO lee
/// <c>almacen.saldos_inventario</c> directo (se corrige la violación de "cero
/// acceso directo a tablas de otro módulo"). Delega en el Open Host Service
/// <see cref="IAlmacenSaldoQueryPort"/>.</para>
///
/// <para><b>Workstream almacén-por-línea PR1</b>: el rollup pasó de nivel almacén
/// (<c>AlmacenDestinoId</c> de la RQ) a nivel <b>sucursal</b>. Delega en
/// <see cref="IAlmacenSaldoQueryPort.ConsultarDisponibilidadPorSucursalAsync"/>,
/// que suma las ubicaciones de todos los almacenes de la sucursal. Hoy el
/// resultado no cambia (1 almacén activo por sucursal); queda correcto para
/// sucursales con 2+ almacenes.</para>
/// </summary>
public sealed class AlmacenStockReadAdapter : IConsultarStockPort
{
    private readonly IAlmacenSaldoQueryPort _saldos;

    public AlmacenStockReadAdapter(IAlmacenSaldoQueryPort saldos) => _saldos = saldos;

    public async Task<DisponibilidadStock> ConsultarPorSucursalAsync(
        Guid sucursalId,
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        var d = await _saldos.ConsultarDisponibilidadPorSucursalAsync(
            sucursalId, articuloId, cancellationToken);

        return new DisponibilidadStock(
            OnHand: d.Cantidad,
            Disponible: d.CantidadDisponible);
    }
}
