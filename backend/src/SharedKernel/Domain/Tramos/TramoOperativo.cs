using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Tramos;

/// <summary>
/// M-303 (TR-CONTINUIDAD): interruptor de un tramo de la operación (lectura de A+W, retorno, Pinturas, captura de un módulo) que se
/// detiene en pantalla sin apagar la aplicación. Detenido, su worker no lee y la captura muestra el aviso. Al detenerlo queda la
/// incidencia con el último evento correcto; reanudar lo autoriza otra persona distinta de quien lo detuvo. Una fila por tramo,
/// global (no por empresa): los workers la leen sin contexto de empresa. Tabla <c>compartido.tramos_operativos</c>.
/// </summary>
public sealed class TramoOperativo : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = "";
    public bool Detenido { get; private set; }
    public string? Motivo { get; private set; }
    public Guid? DetenidoPorId { get; private set; }
    public DateTimeOffset? DetenidoAt { get; private set; }
    public Guid? IncidenciaId { get; private set; }
    /// <summary>Último ciclo que terminó bien (lo escribe el worker o la captura).</summary>
    public DateTimeOffset? UltimoEventoCorrectoAt { get; private set; }
    public string? UltimoEventoCorrecto { get; private set; }
    public Guid? ReanudadoPorId { get; private set; }
    public DateTimeOffset? ReanudadoAt { get; private set; }
    public string? MotivoReanudacion { get; private set; }

    private TramoOperativo() { }

    public TramoOperativo(string clave) : base(Guid.CreateVersion7()) => Clave = clave;

    public void Detener(string motivo, Guid actor, DateTimeOffset ahora)
    {
        if (Detenido) throw new BusinessRuleException("TRAMO_YA_DETENIDO", "El tramo ya está detenido.");
        (Detenido, Motivo, DetenidoPorId, DetenidoAt, IncidenciaId) = (true, MotivoValido(motivo), actor, ahora, null);
    }

    public void LigarIncidencia(Guid incidenciaId) => IncidenciaId = incidenciaId;

    public void Reanudar(string motivo, Guid actor, DateTimeOffset ahora)
    {
        if (!Detenido) throw new BusinessRuleException("TRAMO_NO_DETENIDO", "El tramo no está detenido.");
        if (actor == DetenidoPorId) throw new BusinessRuleException("TRAMO_SEPARACION_FUNCIONES", "Reanudar lo autoriza otra persona distinta de quien detuvo el tramo.");
        (Detenido, ReanudadoPorId, ReanudadoAt, MotivoReanudacion) = (false, actor, ahora, MotivoValido(motivo));
    }

    public void RegistrarEventoCorrecto(string detalle, DateTimeOffset ahora) =>
        (UltimoEventoCorrectoAt, UltimoEventoCorrecto) = (ahora, detalle.Length > 300 ? detalle[..300] : detalle);

    private static string MotivoValido(string? motivo)
    {
        var m = motivo?.Trim();
        if (m is null || m.Length is < 5 or > 500) throw new BusinessRuleException("TRAMO_MOTIVO_INVALIDO", "El motivo debe tener entre 5 y 500 caracteres.");
        return m;
    }
}
