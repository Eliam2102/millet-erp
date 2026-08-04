namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de <b>escritura</b> hacia Compras para crear una Requisición en Borrador de
/// <b>origen sistema</b> (motor de reorden, ADR-0047 PR5.C). Espejo-de-escritura del
/// read-port <c>IComprasPedidoVivoReadPort</c>: la interfaz vive en Almacén (consumidor)
/// y el adapter en <c>Compras.Infrastructure.PublicAdapters</c> (dueño del agregado;
/// Compras referencia Almacén, no al revés), cableado en <c>Program.cs</c>.
///
/// <para>
/// Recibe datos básicos (sucursal, almacén, líneas de artículo+cantidad) y el adapter
/// arma las 4 fuentes internamente (usuario de servicio + empresa, depto de sistema,
/// precio/UM del maestro, código de sucursal), construye la RQ con
/// <c>Origen=Sistema</c>, folio vía el servicio compartido, conserva la validación de
/// almacén-pertenece-a-sucursal y <b>omite</b> opera-en-sucursal. Invocable/testeable
/// por sí solo; PR5.D lo orquestará con el faltante calculado.
/// </para>
/// </summary>
public interface IComprasCrearRqSistemaPort
{
    /// <summary>Crea la RQ de sistema en Borrador y devuelve su Id.</summary>
    Task<Guid> CrearBorradorSistemaAsync(
        CrearRqSistemaSolicitud solicitud,
        CancellationToken cancellationToken);
}

/// <summary>Datos básicos de la RQ automática; el adapter resuelve el resto.</summary>
public sealed record CrearRqSistemaSolicitud(
    Guid SucursalId,
    Guid AlmacenDestinoId,
    IReadOnlyList<LineaRqSistema> Lineas);

/// <summary>Línea propuesta por el motor: artículo + cantidad a reponer.</summary>
public sealed record LineaRqSistema(Guid ArticuloId, decimal Cantidad);
