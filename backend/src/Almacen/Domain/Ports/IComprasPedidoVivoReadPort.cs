namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Compras para el <b>motor de reorden</b> (ADR-0047 PR5.B):
/// devuelve la cantidad "viva" ya pedida de <b>origen sistema</b> (borradores del
/// motor + sus OC), agrupada por <c>(articulo, almacén)</c>, para un conjunto de pares.
///
/// <para>
/// "Vivo de sistema" = RQ de <c>Origen == Sistema</c> en estado no terminal + OC
/// <c>Autorizada</c> con recepción pendiente cuyas líneas provienen de una RQ de
/// sistema. <b>Dedup por línea</b> (FK <c>LineaOrdenCompra.LineaRequisicionId</c>): la
/// porción de una línea de RQ ya volcada a una OC viva la cuenta la OC; el resto lo
/// cuenta la RQ. NO toca el universo manual (filtro <c>Origen == Sistema</c>).
/// </para>
/// <para>
/// Grano <b>por almacén</b> (el natural de los datos de Compras: línea de OC y
/// cabecera de RQ llevan <c>AlmacenDestinoId</c>). Para config Nivel 1, el handler de
/// Almacén expande sucursal→almacenes (dato local suyo) y suma; este puerto no conoce
/// la jerarquía de Almacén.
/// </para>
/// <para>
/// Interfaz propiedad de Almacén; adapter real en
/// <c>Millet.Compras.Infrastructure.PublicAdapters.ComprasPedidoVivoReadAdapter</c>,
/// cableado en <c>Program.cs</c> (regla de oro: cero acceso directo a tablas de Compras).
/// </para>
/// </summary>
public interface IComprasPedidoVivoReadPort
{
    /// <summary>
    /// Cantidad viva de origen sistema por <c>(articulo, almacén)</c> para los
    /// <paramref name="pares"/> solicitados. Un par sin nada vivo no aparece en el
    /// diccionario (el consumidor asume 0).
    /// </summary>
    Task<IReadOnlyDictionary<PedidoVivoClave, decimal>> ObtenerVivoDeSistemaAsync(
        IReadOnlyCollection<PedidoVivoClave> pares,
        CancellationToken cancellationToken);
}

/// <summary>Clave de agregación del "vivo": artículo + almacén (Nivel 2).</summary>
public readonly record struct PedidoVivoClave(Guid ArticuloId, Guid AlmacenId);
