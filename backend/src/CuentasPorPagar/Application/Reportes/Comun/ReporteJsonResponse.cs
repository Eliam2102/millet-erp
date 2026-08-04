namespace Millet.CuentasPorPagar.Application.Reportes.Comun;

/// <summary>
/// Shape JSON estandarizado de los reportes operativos del ERP
/// (ADR-0036). El frontend lo recibe y lo renderiza con
/// <c>&lt;ReporteShell&gt;</c> + tabla virtualizada + exportación
/// client-side a PDF/Excel.
///
/// <para>
/// Convención (no obligatoria por contrato — sólo reforzada por
/// lint/PR): todos los endpoints <c>GET /api/v1/cuentas-por-pagar/reportes/...</c>
/// devuelven esta forma. Los filtros aplicados se devuelven dentro
/// del payload (no como query params reflejados) para que la captura
/// de pantalla del reporte muestre exactamente qué se filtró sin
/// depender del header HTTP.
/// </para>
/// </summary>
public sealed record ReporteJsonResponse(
    string Titulo,
    DateTimeOffset GeneradoEn,
    IReadOnlyList<FiltroAplicado> FiltrosAplicados,
    IReadOnlyList<ColumnaReporte> Columnas,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Filas,
    IReadOnlyDictionary<string, object?>? Totales);

public sealed record FiltroAplicado(string Label, string Valor);

public sealed record ColumnaReporte(
    string Key,
    string Label,
    TipoColumnaReporte Tipo,
    AlineacionColumna Alineacion);

public enum TipoColumnaReporte
{
    Texto       = 1,
    Entero      = 2,    // Numero entero
    Numerico    = 3,    // Decimal genérico
    Moneda      = 4,    // Decimal con prefijo moneda
    Fecha       = 5,
    FechaHora   = 6,
    Booleano    = 7,
    Enum        = 8,
}

public enum AlineacionColumna
{
    Izquierda   = 1,
    Centro      = 2,
    Derecha     = 3,
}
