namespace Millet.Almacen.Domain.Ports;

/// <remarks>
/// ADM08 / ADR-0062: el nombre histórico Dim3 se conserva por compatibilidad;
/// este contrato admite centros Dim1/Dim2 y máquinas Dim3.
/// </remarks>
/// <summary>
/// Read-port de Almacén hacia CentrosCosto para resolver, en batch,
/// <c>dim3Id → clave/nombre/activa</c> y mostrar el CC-Máquina de una línea de
/// salida como "clave — nombre" (Fase E PR5, ADR-0050 §3 / ADR-0042).
///
/// <para>Almacén define el contrato aquí (no referencia CentrosCosto) porque la
/// arista de proyecto <c>Almacen→CentrosCosto</c> cerraría un ciclo vía
/// <c>Compartido</c> (<c>CentrosCosto→Compartido→Almacen</c>). Mismo patrón que
/// <c>IComprasOcReadPort</c> / <c>IComprasRequisicionReadPort</c>: puerto propio
/// + stub NoOp + adapter real wireado en <c>Program.cs</c> donde ambos módulos
/// están disponibles (el adapter delega en
/// <c>CentrosCosto.Application.PublicPorts.IDim3ReadPort</c>).</para>
///
/// <para><b>SIN filtro de alcance</b> e <b>INCLUYE inactivas</b> (ADR-0050
/// "ver ≠ elegir"; ADR-0049): mostrar el nombre de una máquina que la salida ya
/// trae no es elegirla. Batch (no por-id) para evitar N+1; los ids no
/// encontrados no aparecen en el diccionario (el consumidor cae a "No
/// catalogado").</para>
/// </summary>
public interface ICentroCostoReadPort
{
    Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> dim3Ids,
        CancellationToken cancellationToken);
}

/// <summary>Proyección de lectura de una Dim3 (máquina) para display en salidas.</summary>
public sealed record Dim3Lectura(Guid Id, string Clave, string Nombre, bool Activa);
