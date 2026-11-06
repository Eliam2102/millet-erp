namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>Un control de la conciliación de auxiliares (CON-09) para el cierre: si cuadra y por cuánto difiere.</summary>
public sealed record ControlConciliacionResumen(string Control, string Estado, bool Cuadra, decimal? Diferencia);

/// <summary>Resumen de la conciliación de auxiliares al corte, para el checklist del cierre (CON-12).</summary>
public sealed record ResumenConciliacionAuxiliares(DateOnly Corte, bool TodoCuadra, IReadOnlyList<ControlConciliacionResumen> Controles);

/// <summary>
/// F1-CON-09 (86e3jvvm7): ¿cuadran clientes, proveedores y bancos contra sus cuentas de control al corte? Lo consumirá el cierre
/// (CON-12) para no cerrar con diferencias. Solo lectura; se calcula a demanda.
/// </summary>
public interface IConciliacionAuxiliaresResumenReadPort
{
    Task<ResumenConciliacionAuxiliares> ObtenerAsync(DateOnly corte, CancellationToken ct);
}
