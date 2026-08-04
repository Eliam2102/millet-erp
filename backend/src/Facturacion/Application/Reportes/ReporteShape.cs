namespace Millet.Facturacion.Application.Reportes;

/// <summary>
/// Shape canónico para reportes operativos del módulo Facturación (ADR-0036). El
/// backend devuelve <c>ReporteResponse</c> con datos estructurados; el frontend
/// renderiza con <c>&lt;ReporteShell&gt;</c> + exporta a PDF/Excel client-side.
///
/// <para>
/// Convención alineada con Almacén / CxP / Compras para que
/// <c>&lt;ReporteShell&gt;</c> reuse la lectura sin renderer-specific code.
/// </para>
/// </summary>
public sealed record ReporteResponse<TFila>(
    string Titulo,
    DateTimeOffset GeneradoEn,
    IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaDescriptor> Columnas,
    IReadOnlyList<TFila> Filas,
    IReadOnlyDictionary<string, decimal>? Totales = null);

/// <summary>
/// Descripción de una columna del reporte para que el frontend la pinte sin
/// acoplarse a la estructura del DTO.
/// </summary>
public sealed record ColumnaDescriptor(
    string Clave,
    string Etiqueta,
    string Tipo, // "texto" | "numero" | "moneda" | "fecha"
    string? Alineacion = null,
    int? AnchoPx = null);
