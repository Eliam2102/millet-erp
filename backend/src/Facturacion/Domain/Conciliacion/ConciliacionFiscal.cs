using System.Text.RegularExpressions;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Conciliacion;

/// <summary>
/// M-314: constancia de que una incidencia fiscal (el SAT dice otra cosa que el estado interno del comprobante) se resolvió. Solo nace
/// cuando una consulta nueva del estatus del UUID ya no muestra diferencia, con el soporte que respalda la corrección.
/// </summary>
public sealed class ResolucionIncidenciaFiscal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const int SoporteMinimo = 5, SoporteMaximo = 500;

    public Guid EmpresaId { get; set; }
    public Guid ComprobanteId { get; private set; }
    public string Uuid { get; private set; } = string.Empty;
    public Guid SucursalId { get; private set; }
    public EstadoTimbrado EstadoInterno { get; private set; }
    public string? EstatusSatAntes { get; private set; }
    public string EstatusSatDespues { get; private set; } = string.Empty;
    /// <summary>Referencia del soporte (oficio, acuse, captura del portal del SAT); los archivos van por el expediente.</summary>
    public string Soporte { get; private set; } = string.Empty;
    public string? Nota { get; private set; }
    public string ResueltaPor { get; private set; } = string.Empty;
    public DateTimeOffset ResueltaEn { get; private set; }

    private ResolucionIncidenciaFiscal() { }

    public ResolucionIncidenciaFiscal(Guid id, Guid empresaId, Guid comprobanteId, string uuid, Guid sucursalId, EstadoTimbrado estadoInterno,
        string? estatusSatAntes, string estatusSatDespues, string? soporte, string? nota, string resueltaPor, DateTimeOffset ahora) : base(id)
    {
        if (soporte is null || soporte.Trim().Length is < SoporteMinimo or > SoporteMaximo)
            throw new BusinessRuleException("FAC_INCIDENCIA_SOPORTE_REQUERIDO",
                $"Indique el soporte que respalda la resolución (entre {SoporteMinimo} y {SoporteMaximo} caracteres).");
        EmpresaId = empresaId;
        ComprobanteId = comprobanteId;
        Uuid = uuid;
        SucursalId = sucursalId;
        EstadoInterno = estadoInterno;
        EstatusSatAntes = estatusSatAntes;
        EstatusSatDespues = estatusSatDespues;
        Soporte = soporte.Trim();
        Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        ResueltaPor = resueltaPor.Length > 256 ? resueltaPor[..256] : resueltaPor;
        ResueltaEn = ahora;
    }
}

/// <summary>
/// M-314: cierre de folios de una serie en un mes. Guarda el reporte final (folios usados, cancelados y descartados). Vigente = cerrado;
/// no se reabre sin el permiso de reapertura, y la reapertura queda con motivo, quién y cuándo.
/// </summary>
public sealed class CierreFolios : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const int MotivoMinimo = 5, MotivoMaximo = 500;

    public Guid EmpresaId { get; set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public TipoComprobante Tipo { get; private set; }
    /// <summary>Prefijo de la serie (parte del folio antes del consecutivo; vacío si el folio es solo numérico).</summary>
    public string Serie { get; private set; } = string.Empty;
    public long FolioInicial { get; private set; }
    public long FolioFinal { get; private set; }
    public int Usados { get; private set; }
    public int Cancelados { get; private set; }
    public int Descartados { get; private set; }
    /// <summary>Reporte final serializado: listas de números de folio por estado.</summary>
    public string ReporteJson { get; private set; } = "{}";
    public string CerradoPor { get; private set; } = string.Empty;
    public DateTimeOffset CerradoEn { get; private set; }
    public bool Vigente { get; private set; }
    public string? ReabiertoPor { get; private set; }
    public DateTimeOffset? ReabiertoEn { get; private set; }
    public string? MotivoReapertura { get; private set; }

    private CierreFolios() { }

    public CierreFolios(Guid id, Guid empresaId, int anio, int mes, TipoComprobante tipo, string serie, AnalisisFolios analisis,
        string reporteJson, string cerradoPor, DateTimeOffset ahora) : base(id)
    {
        if (!analisis.PuedeCerrar)
            throw new BusinessRuleException("FAC_CIERRE_FOLIOS_NO_CUMPLE", analisis.Motivo(serie));
        EmpresaId = empresaId;
        Anio = anio;
        Mes = mes;
        Tipo = tipo;
        Serie = serie;
        FolioInicial = analisis.FolioInicial;
        FolioFinal = analisis.FolioFinal;
        Usados = analisis.Usados.Count;
        Cancelados = analisis.Cancelados.Count;
        Descartados = analisis.Descartados.Count;
        ReporteJson = reporteJson;
        CerradoPor = cerradoPor.Length > 256 ? cerradoPor[..256] : cerradoPor;
        CerradoEn = ahora;
        Vigente = true;
    }

    public void Reabrir(string? motivo, string usuario, DateTimeOffset ahora)
    {
        if (!Vigente)
            throw new ConflictException("FAC_CIERRE_FOLIOS_YA_REABIERTO", "El cierre de folios ya fue reabierto.");
        if (motivo is null || motivo.Trim().Length is < MotivoMinimo or > MotivoMaximo)
            throw new BusinessRuleException("FAC_CIERRE_FOLIOS_MOTIVO_REQUERIDO",
                $"Indique el motivo de la reapertura (entre {MotivoMinimo} y {MotivoMaximo} caracteres).");
        Vigente = false;
        ReabiertoPor = usuario.Length > 256 ? usuario[..256] : usuario;
        ReabiertoEn = ahora;
        MotivoReapertura = motivo.Trim();
    }
}

/// <summary>
/// Resultado de revisar los folios de una serie en un mes (lógica pura, sin base de datos). Usados = timbrados; cancelados; descartados.
/// Bloquean el cierre: folios pendientes (borrador, en proceso, fallidos, por autorizar, cancelación pendiente) y huecos en la numeración.
/// </summary>
public sealed record AnalisisFolios(
    IReadOnlyList<long> Usados, IReadOnlyList<long> Cancelados, IReadOnlyList<long> Descartados,
    IReadOnlyList<long> Pendientes, IReadOnlyList<long> Huecos, long FolioInicial, long FolioFinal)
{
    public bool PuedeCerrar => Pendientes.Count == 0 && Huecos.Count == 0 && (Usados.Count + Cancelados.Count + Descartados.Count) > 0;

    public string Motivo(string serie)
    {
        var partes = new List<string>();
        if (Pendientes.Count > 0) partes.Add($"folios pendientes: {Lista(Pendientes)}");
        if (Huecos.Count > 0) partes.Add($"huecos en la numeración: {Lista(Huecos)}");
        if (partes.Count == 0) partes.Add("la serie no tiene folios en el periodo");
        return $"La serie «{(serie.Length == 0 ? "(sin prefijo)" : serie)}» no se puede cerrar: {string.Join("; ", partes)}.";
    }

    private static string Lista(IReadOnlyList<long> folios) =>
        string.Join(", ", folios.Take(10)) + (folios.Count > 10 ? $" y {folios.Count - 10} más" : string.Empty);

    private static readonly Regex Consecutivo = new(@"^(?<serie>.*?)(?<n>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Prefijo de serie de un folio formateado («FV-000123» → «FV-»).</summary>
    public static string SerieDe(string folio)
    {
        var m = Consecutivo.Match(folio ?? string.Empty);
        return m.Success ? m.Groups["serie"].Value : folio ?? string.Empty;
    }

    public static AnalisisFolios Analizar(IEnumerable<(long Numero, EstadoTimbrado Estado)> folios)
    {
        var lista = folios.OrderBy(f => f.Numero).ToList();
        var usados = lista.Where(f => f.Estado == EstadoTimbrado.Timbrado).Select(f => f.Numero).ToList();
        var cancelados = lista.Where(f => f.Estado == EstadoTimbrado.Cancelado).Select(f => f.Numero).ToList();
        var descartados = lista.Where(f => f.Estado == EstadoTimbrado.Descartada).Select(f => f.Numero).ToList();
        var pendientes = lista.Where(f => f.Estado is not (EstadoTimbrado.Timbrado or EstadoTimbrado.Cancelado or EstadoTimbrado.Descartada))
            .Select(f => f.Numero).ToList();
        var presentes = lista.Select(f => f.Numero).ToHashSet();
        var huecos = new List<long>();
        if (lista.Count > 0)
            for (var n = lista[0].Numero; n <= lista[^1].Numero; n++)
                if (!presentes.Contains(n)) huecos.Add(n);
        return new(usados, cancelados, descartados, pendientes, huecos, lista.Count == 0 ? 0 : lista[0].Numero, lista.Count == 0 ? 0 : lista[^1].Numero);
    }
}
