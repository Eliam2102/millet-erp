namespace Millet.Tesoreria.Application.Reportes.Comun;

/// <summary>
/// Shape JSON estandarizado de los reportes operativos del ERP
/// (ADR-0036), replicado localmente por módulo (mismo criterio que
/// <c>PagedResponse</c> — no acoplar módulos por una primitiva). El
/// frontend lo renderiza con <c>&lt;ReporteShell&gt;</c> + exportación
/// client-side a PDF/Excel.
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
    Entero      = 2,
    Numerico    = 3,
    Moneda      = 4,
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
