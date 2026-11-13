namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Regla de salida 5 (liquidación del chofer): clave y zona horaria de cada sucursal (catálogo de Administración) y los festivos del
/// calendario compartido (<c>system.dias-festivos</c>). Solo lectura.
/// </summary>
public interface IPtCalendarioSucursalesPort
{
    Task<IReadOnlyDictionary<Guid, SucursalCalendarioPt>> SucursalesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Vacío si los festivos aún no se configuran: el plazo se calcula sin ellos.</summary>
    Task<IReadOnlySet<DateOnly>> FestivosAsync(CancellationToken ct);
}

/// <summary><c>Nombre</c>: origen de la Carta Porte de la hoja (la sucursal de donde sale, no siempre Conkal).</summary>
public sealed record SucursalCalendarioPt(string Clave, string ZonaHoraria, string? Nombre = null);
