namespace Millet.Integraciones.Aw.Application.DiferenciasFiscales;

/// <summary>
/// Opciones del reporte de diferencias fiscales A+W vs ERP (U1.8b). Sección
/// <c>IntegracionesAw:DiferenciasFiscales</c>. La corrida programada está apagada por defecto.
/// </summary>
public sealed class DiferenciasFiscalesAwOptions
{
    public const string SectionName = "IntegracionesAw:DiferenciasFiscales";

    /// <summary>Registra el worker semanal que calcula y guarda una corrida.</summary>
    public bool ProgramacionHabilitada { get; set; }

    /// <summary>Día de la corrida semanal (nombre en inglés de <see cref="System.DayOfWeek"/>, p. ej. Monday).</summary>
    public DayOfWeek DiaSemana { get; set; } = DayOfWeek.Monday;

    /// <summary>Hora local de la corrida, formato HH:mm.</summary>
    public string HoraLocal { get; set; } = "06:00";

    /// <summary>Zona horaria IANA de <see cref="HoraLocal"/>.</summary>
    public string ZonaHoraria { get; set; } = "America/Mexico_City";

    /// <summary>
    /// Con hasta este número de clientes A+W en el ERP se leen por referencia; con más, se recorre A+W por
    /// páginas (un barrido real son ~45 mil filas: leer por referencia sale más barato con pocos clientes).
    /// </summary>
    public int MaxLecturasPorReferencia { get; set; } = 200;
}
