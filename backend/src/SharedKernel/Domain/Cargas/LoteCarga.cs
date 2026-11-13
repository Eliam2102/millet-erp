using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Cargas;

/// <summary>Ciclo del lote de carga (M-047): Subido → Autorizado | Rechazado; Autorizado → Aplicado.</summary>
public enum EstadoLoteCarga : short
{
    Subido = 1,
    Autorizado = 2,
    Rechazado = 3,
    Aplicado = 4,
}

/// <summary>
/// Lote común de cargas (M-047, 86e3jvvmf): un archivo subido por una persona, mapeado a las columnas de un
/// tipo de carga (<see cref="Tipo"/>, p. ej. <c>contabilidad.saldos-iniciales</c>), validado por el módulo dueño
/// del tipo, autorizado o rechazado por <b>otra</b> persona y aplicado una sola vez. Guarda la huella SHA-256 del
/// archivo original, las filas ya mapeadas y el resultado de la última validación; no guarda el archivo.
/// Tabla <c>compartido.lotes_carga</c>. El módulo dueño del tipo nunca lee esta tabla: el lote le pasa las filas.
/// </summary>
public sealed class LoteCarga : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MotivoMin = 5;
    public const int MotivoMax = 1000;

    public Guid EmpresaId { get; set; }
    public string Tipo { get; private set; } = string.Empty;
    public string NombreArchivo { get; private set; } = string.Empty;
    public string Huella { get; private set; } = string.Empty;
    public DateOnly? FechaCorte { get; private set; }
    public EstadoLoteCarga Estado { get; private set; } = EstadoLoteCarga.Subido;

    /// <summary>Mapeo encabezado del archivo → columna del tipo (JSON).</summary>
    public string MapeoJson { get; private set; } = "{}";
    /// <summary>Parámetros del tipo capturados al subir (JSON), p. ej. sucursal por omisión.</summary>
    public string ParametrosJson { get; private set; } = "{}";
    /// <summary>Filas ya mapeadas a las columnas del tipo (JSON), con su renglón real del archivo.</summary>
    public string FilasJson { get; private set; } = "[]";
    /// <summary>Resultado de la última validación del módulo dueño (JSON).</summary>
    public string ValidacionJson { get; private set; } = "{}";
    public bool PuedeAplicar { get; private set; }
    public int TotalFilas { get; private set; }
    public int TotalErrores { get; private set; }

    public Guid SubidoPorId { get; private set; }
    public string SubidoPorNombre { get; private set; } = string.Empty;
    public DateTimeOffset SubidoEn { get; private set; }
    public Guid? DecididoPorId { get; private set; }
    public string? DecididoPorNombre { get; private set; }
    public DateTimeOffset? DecididoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public DateTimeOffset? AplicadoEn { get; private set; }
    /// <summary>Lo que devolvió el módulo al aplicar (p. ej. el id de la póliza de apertura) y su folio.</summary>
    public string? Referencia { get; private set; }
    public string? FolioResultado { get; private set; }
    public string? UltimoError { get; private set; }

    /// <summary>Bitácora del lote (JSON): cada paso con quién y cuándo.</summary>
    public string HistorialJson { get; private set; } = "[]";

    private LoteCarga() { }

    public LoteCarga(Guid id, Guid empresaId, string tipo, string nombreArchivo, string huella, DateOnly? fechaCorte,
        string mapeoJson, string filasJson, int totalFilas, Guid subidoPorId, string subidoPorNombre, DateTimeOffset ahora,
        string parametrosJson = "{}")
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(tipo) || string.IsNullOrWhiteSpace(huella) || subidoPorId == Guid.Empty)
            throw new BusinessRuleException("LOTE_CARGA_DATOS_INVALIDOS", "El lote necesita tipo, huella del archivo y quién lo sube.");
        EmpresaId = empresaId;
        Tipo = tipo;
        NombreArchivo = nombreArchivo;
        Huella = huella.ToUpperInvariant();
        FechaCorte = fechaCorte;
        MapeoJson = mapeoJson;
        ParametrosJson = parametrosJson;
        FilasJson = filasJson;
        TotalFilas = totalFilas;
        SubidoPorId = subidoPorId;
        SubidoPorNombre = subidoPorNombre;
        SubidoEn = ahora;
    }

    public void RegistrarValidacion(string validacionJson, bool puedeAplicar, int errores)
    {
        if (Estado is not (EstadoLoteCarga.Subido or EstadoLoteCarga.Autorizado))
            throw new BusinessRuleException("LOTE_CARGA_ESTADO", $"El lote está {Estado}; ya no se valida.");
        ValidacionJson = validacionJson;
        PuedeAplicar = puedeAplicar;
        TotalErrores = errores;
    }

    /// <summary>CA6 (M-166): autoriza una persona distinta de quien subió, y solo un lote que puede aplicarse.</summary>
    public void Autorizar(Guid actorId, string actorNombre, DateTimeOffset ahora)
    {
        ExigirEstado(EstadoLoteCarga.Subido);
        if (actorId == SubidoPorId)
            throw new BusinessRuleException("LOTE_CARGA_MISMA_PERSONA", "Debe autorizar otra persona: quien subió el lote no puede autorizarlo.");
        if (!PuedeAplicar)
            throw new BusinessRuleException("LOTE_CARGA_CON_ERRORES", "El lote tiene errores de validación; corríjalos y suba el archivo de nuevo.");
        Estado = EstadoLoteCarga.Autorizado;
        DecididoPorId = actorId; DecididoPorNombre = actorNombre; DecididoEn = ahora;
    }

    public void Rechazar(Guid actorId, string actorNombre, string motivo, DateTimeOffset ahora)
    {
        ExigirEstado(EstadoLoteCarga.Subido, EstadoLoteCarga.Autorizado);
        var texto = motivo?.Trim() ?? string.Empty;
        if (texto.Length is < MotivoMin or > MotivoMax)
            throw new BusinessRuleException("LOTE_CARGA_MOTIVO_REQUERIDO", $"Escriba el motivo del rechazo ({MotivoMin} a {MotivoMax} caracteres).");
        Estado = EstadoLoteCarga.Rechazado;
        DecididoPorId = actorId; DecididoPorNombre = actorNombre; DecididoEn = ahora; MotivoRechazo = texto;
    }

    public void MarcarAplicado(string referencia, string? folio, DateTimeOffset ahora)
    {
        ExigirEstado(EstadoLoteCarga.Autorizado);
        Estado = EstadoLoteCarga.Aplicado;
        Referencia = referencia; FolioResultado = folio; AplicadoEn = ahora; UltimoError = null;
    }

    /// <summary>Un error del módulo al aplicar no cambia el estado: queda registrado para corregirlo.</summary>
    public void RegistrarErrorAplicacion(string mensaje) => UltimoError = mensaje.Length > 2000 ? mensaje[..2000] : mensaje;

    public void AgregarHistorial(string historialJson) => HistorialJson = historialJson;

    private void ExigirEstado(params EstadoLoteCarga[] permitidos)
    {
        if (!permitidos.Contains(Estado))
            throw new ConflictException("LOTE_CARGA_ESTADO", $"El lote está {Estado}; esta acción ya no aplica.");
    }
}
