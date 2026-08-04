namespace Millet.CentrosCosto.Application.PublicPorts;

/// <summary>
/// Read-port PÚBLICO de Centros de Costo hacia los módulos consumidores
/// (Compras, Almacén). Resuelve, en batch, <c>dim3Id → clave/nombre/activa</c>
/// para que un documento (OC, entrada, salida) muestre su CC-Máquina como
/// "clave — nombre" sin leer el catálogo completo ni acoplarse al esquema
/// <c>centros_costo</c> (ADR-0042).
///
/// <para>
/// A diferencia de <c>IUsuarioReadPort</c> (ADR-0042), aquí el puerto lo
/// DECLARA el owner: CentrosCosto es el <b>servidor</b>, no el cliente. No hay
/// ciclo que romper — CentrosCosto es dependency-free (#620) y los consumidores
/// dependen de este contrato publicado —, así que el owner publica el puerto y
/// hospeda el adaptador (<c>Infrastructure.PublicAdapters.Dim3ReadAdapter</c>).
/// </para>
///
/// <para>
/// <b>SIN filtro de alcance</b> e <b>INCLUYE inactivas</b> (ADR-0050,
/// "ver ≠ elegir"): mostrar el nombre de una máquina que el documento ya trae
/// no es elegirla, y una Dim3 dada de baja (ADR-0049) debe seguir
/// resolviéndose en un documento histórico. El selector CON alcance es otro
/// camino (<c>BuscarDim3Query</c>). Batch (no por-id) para evitar N+1; los ids
/// no encontrados <b>no aparecen</b> en el diccionario (el consumidor hace
/// fallback, p.ej. "No catalogado").
/// </para>
/// </summary>
public interface IDim3ReadPort
{
    Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> dim3Ids,
        CancellationToken cancellationToken);
}

/// <summary>
/// Proyección de lectura de una Dim3 (máquina) para display en documentos.
/// <c>Activa</c> distingue una máquina viva de una dada de baja — que igual se
/// resuelve (ADR-0049), pero el consumidor puede señalarla.
/// </summary>
public sealed record Dim3Lectura(Guid Id, string Clave, string Nombre, bool Activa);
