using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.Incidencias;

/// <summary>
/// Rechazo de un paso que revierte la operación (422) y además debe dejar su incidencia (U-4 · 86e3yzemw).
/// El handler la lanza con la <see cref="Solicitud"/> armada; el pipeline <c>IncidenciaRechazoBehavior</c> abre la
/// incidencia en la transacción de Compartido (la del módulo ya se revirtió) y la respuesta lleva
/// <c>incidenciaId</c> e <c>incidenciaNumero</c> en el Problem Details.
/// Con <see cref="Guid.Empty"/> como responsable, la incidencia queda a cargo de quien recibió el rechazo.
/// </summary>
public sealed class IncidenciaRechazoException : BusinessRuleException
{
    public IncidenciaRechazoException(string code, string message, AbrirIncidenciaSolicitud solicitud)
        : base(code, message)
    {
        Solicitud = solicitud with { CodigoMotivo = solicitud.CodigoMotivo ?? code };
    }

    public AbrirIncidenciaSolicitud Solicitud { get; }

    /// <summary>Incidencia abierta (o la pendiente igual que ya existía); nulo si no se pudo abrir.</summary>
    public IncidenciaAbierta? Incidencia { get; set; }
}
