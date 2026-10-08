namespace Millet.CentrosCosto.Application.PublicPorts;

/// <summary>
/// Read-port PÚBLICO de "elegir" un CC-Máquina al guardar un documento
/// (G1.11 / ADR-0050). Complementa a <see cref="IDim3ReadPort"/> ("ver"):
/// aquí SÍ importa que exista y esté activa, y opcionalmente el alcance del
/// usuario actual (asignación usuario→Dim3, bypass con
/// <c>centros_costo.dim3.leer-todos</c>).
/// </summary>
public interface IDim3ElegibilidadPort
{
    /// <param name="aplicarAlcance">
    /// true = captura del dueño del gasto (línea de RQ); false = captura por
    /// proxy (línea manual de OC), solo existe + activa.
    /// </param>
    Task<Dim3Elegibilidad> EvaluarAsync(
        Guid dim3Id, bool aplicarAlcance, CancellationToken cancellationToken);
}

public enum Dim3Elegibilidad
{
    Valida = 0,
    NoExiste = 1,
    Inactiva = 2,
    FueraDeAlcance = 3,
}
