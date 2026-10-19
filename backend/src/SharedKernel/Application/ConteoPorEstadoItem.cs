namespace Millet.SharedKernel.Application;

/// <summary>
/// Conteo de documentos por estado para los tabs de las bandejas (UX-F3-05).
/// <c>Estado</c> es el valor numérico del enum del listado; los estados sin
/// documentos no se devuelven (el frontend los muestra en 0).
/// </summary>
public sealed record ConteoPorEstadoItem(int Estado, int Total);
