namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura de Almacén hacia el módulo Identidad. Resuelve, en
/// batch, <c>usuarioId → nombre</c> para enriquecer los DTOs de lectura de
/// salidas con el nombre de la persona destinataria sin que el cliente
/// necesite leer el padrón completo de usuarios (ADR-0042).
///
/// <para>
/// Espejo del <c>IUsuarioReadPort</c> de Compras (mismo ADR): el puerto se
/// declara en el dominio del <b>consumidor</b>. El adaptador, en cambio,
/// <b>no</b> vive en Almacén ni en Compartido (ambos cerrarían ciclo con
/// Identidad, que ya los referencia para sembrar permisos canónicos): lo
/// hospeda <c>Identidad.Infrastructure</c> —el owner del dato— y se cablea
/// en <c>Program.cs</c>.
/// </para>
///
/// <para>
/// Batch (no por-id) para evitar N+1. Las claves no encontradas no aparecen
/// en el diccionario (el handler hace fallback al id).
/// </para>
/// </summary>
public interface IUsuarioReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken);
}
