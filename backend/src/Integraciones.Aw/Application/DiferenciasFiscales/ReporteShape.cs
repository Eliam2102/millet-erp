namespace Millet.Integraciones.Aw.Application.DiferenciasFiscales;

/// <summary>
/// Shape canónico de reporte (ADR-0036), mismo contrato que los demás módulos para que
/// <c>&lt;ReporteShell&gt;</c> lo pinte y exporte sin código específico. Extensiones de este reporte:
/// <c>Avisos</c> (qué no se pudo comparar y por qué) y <c>Resumen</c> (conteos).
/// </summary>
public sealed record ReporteResponse<TFila>(
    string Titulo,
    DateTimeOffset GeneradoEn,
    IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaDescriptor> Columnas,
    IReadOnlyList<TFila> Filas,
    IReadOnlyDictionary<string, decimal>? Totales = null,
    IReadOnlyList<string>? Avisos = null,
    ResumenDiferenciasFiscales? Resumen = null);

/// <summary>Descripción de una columna para que el frontend la pinte sin acoplarse al DTO.</summary>
public sealed record ColumnaDescriptor(
    string Clave,
    string Etiqueta,
    string Tipo, // "texto" | "numero" | "moneda" | "fecha"
    string? Alineacion = null,
    int? AnchoPx = null);

/// <summary>Conteos del cálculo: entidades comparadas y diferencias vigentes (no revisadas) y revisadas.</summary>
public sealed record ResumenDiferenciasFiscales(int ClientesComparados, int ProductosComparados, int Pendientes, int Revisadas);
