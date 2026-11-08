using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.ConciliacionSat;

/// <summary>De dónde salió la lista de CFDI emitidos que tiene el SAT.</summary>
public enum FuenteConciliacionSat : short
{
    /// <summary>Archivo de metadatos que se baja del portal del SAT o de la descarga masiva (TXT o ZIP).</summary>
    ArchivoMetadata = 1,
    /// <summary>Descarga masiva programada con la FIEL (entrega posterior de FAC-10).</summary>
    DescargaMasiva = 2,
}

/// <summary>Por qué un CFDI no cuadra entre el SAT y el ERP (FAC-10).</summary>
public enum TipoDiferenciaSat : short
{
    /// <summary>El SAT tiene el CFDI y el ERP no (FAC-10-c).</summary>
    FaltanteEnErp = 1,
    /// <summary>El ERP lo timbró en el periodo y el SAT no lo reporta.</summary>
    FaltanteEnSat = 2,
    /// <summary>Vigente en uno y cancelado en el otro (FAC-10-a).</summary>
    EstatusDistinto = 3,
    TotalDistinto = 4,
    /// <summary>Efecto del comprobante distinto (I, E, T, P).</summary>
    TipoDistinto = 5,
    ReceptorDistinto = 6,
}

/// <summary>
/// Corrida de la conciliación de CFDI emitidos contra el SAT (FAC-10, R23 del
/// Módulo 06). Guarda qué se comparó (periodo, archivo y su huella), los conteos
/// y cada diferencia encontrada. Una corrida sin diferencias también queda
/// registrada (FAC-10-b).
/// </summary>
public sealed class ConciliacionSat : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public DateOnly Desde { get; private set; }
    public DateOnly Hasta { get; private set; }
    public FuenteConciliacionSat Fuente { get; private set; }
    public string NombreArchivo { get; private set; } = string.Empty;
    /// <summary>SHA-256 del archivo, para saber si se volvió a conciliar el mismo.</summary>
    public string Huella { get; private set; } = string.Empty;
    public Guid? EjecutadaPor { get; private set; }
    public DateTimeOffset EjecutadaEn { get; private set; }
    /// <summary>CFDI distintos que trae el archivo del SAT.</summary>
    public int CfdiSat { get; private set; }
    /// <summary>CFDI timbrados en el ERP dentro del periodo.</summary>
    public int CfdiErp { get; private set; }
    /// <summary>UUID que cuadran en todo.</summary>
    public int Coincidentes { get; private set; }
    /// <summary>UUID con al menos una diferencia.</summary>
    public int ConDiferencias { get; private set; }

    private readonly List<DiferenciaConciliacionSat> _diferencias = [];
    public IReadOnlyCollection<DiferenciaConciliacionSat> Diferencias => _diferencias.AsReadOnly();

    public bool SinDiferencias => ConDiferencias == 0;

    private ConciliacionSat() { }

    public static ConciliacionSat Registrar(
        Guid empresaId, DateOnly desde, DateOnly hasta, FuenteConciliacionSat fuente, string nombreArchivo, string huella,
        Guid? ejecutadaPor, DateTimeOffset ahora, int cfdiSat, int cfdiErp, IReadOnlyCollection<DiferenciaDetectada> diferencias)
    {
        if (hasta < desde)
            throw new BusinessRuleException("CONCILIACION_SAT_PERIODO_INVALIDO", "La fecha final del periodo no puede ser anterior a la inicial.");
        if (hasta.DayNumber - desde.DayNumber > MaximoDiasPeriodo)
            throw new BusinessRuleException("CONCILIACION_SAT_PERIODO_LARGO",
                $"El periodo de la conciliación no puede pasar de {MaximoDiasPeriodo + 1} días.");

        var c = new ConciliacionSat
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Desde = desde,
            Hasta = hasta,
            Fuente = fuente,
            NombreArchivo = nombreArchivo.Trim(),
            Huella = huella,
            EjecutadaPor = ejecutadaPor,
            EjecutadaEn = ahora,
            CfdiSat = cfdiSat,
            CfdiErp = cfdiErp,
        };
        foreach (var d in diferencias)
            c._diferencias.Add(DiferenciaConciliacionSat.Crear(c, d));
        var uuidsConDiferencia = diferencias.Select(d => d.Uuid).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        c.ConDiferencias = uuidsConDiferencia;
        var universo = cfdiSat + diferencias.Count(d => d.Tipo == TipoDiferenciaSat.FaltanteEnSat);
        c.Coincidentes = Math.Max(0, universo - uuidsConDiferencia);
        return c;
    }

    /// <summary>Hasta un trimestre por corrida: la conciliación es semanal (R23) y el archivo del portal es por rango.</summary>
    public const int MaximoDiasPeriodo = 92;
}

/// <summary>
/// Un CFDI que no cuadra en una corrida. Quien concilia la marca como revisada
/// (con nota opcional) cuando ya la atendió.
/// </summary>
public sealed class DiferenciaConciliacionSat : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ConciliacionId { get; private set; }
    public string Uuid { get; private set; } = string.Empty;
    public TipoDiferenciaSat Tipo { get; private set; }
    public Guid? ComprobanteId { get; private set; }
    public string? Folio { get; private set; }
    public string? RfcReceptor { get; private set; }
    public string? ValorSat { get; private set; }
    public string? ValorErp { get; private set; }
    public bool Revisada { get; private set; }
    public Guid? RevisadaPor { get; private set; }
    public DateTimeOffset? RevisadaEn { get; private set; }
    public string? Nota { get; private set; }

    private DiferenciaConciliacionSat() { }

    internal static DiferenciaConciliacionSat Crear(ConciliacionSat c, DiferenciaDetectada d) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = c.EmpresaId,
        ConciliacionId = c.Id,
        Uuid = d.Uuid,
        Tipo = d.Tipo,
        ComprobanteId = d.ComprobanteId,
        Folio = d.Folio,
        RfcReceptor = d.RfcReceptor,
        ValorSat = d.ValorSat,
        ValorErp = d.ValorErp,
    };

    public void MarcarRevisada(Guid? usuarioId, DateTimeOffset ahora, string? nota)
    {
        if (Revisada)
            throw new BusinessRuleException("CONCILIACION_SAT_YA_REVISADA", "Esta diferencia ya está marcada como revisada.");
        var limpia = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        if (limpia is { Length: > MaximoNota })
            throw new BusinessRuleException("CONCILIACION_SAT_NOTA_LARGA", $"La nota no debe pasar de {MaximoNota} caracteres.");
        Revisada = true;
        RevisadaPor = usuarioId;
        RevisadaEn = ahora;
        Nota = limpia;
    }

    public const int MaximoNota = 500;
}
