using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>Automático = lo verifica el ERP con los datos de su módulo dueño; Manual = lo confirma una persona con nota.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoPasoCierre : short { Automatico = 0, Manual = 1 }

/// <summary>Mensual = periodos 1 a 12; Anual = periodo 13 (cierre del ejercicio, R20).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlcancePasoCierre : short { Mensual = 0, Anual = 1 }

/// <summary>Excepción = se cierra aunque el paso no se cumpla (lo autoriza quien tiene el permiso); Confirmación = paso manual hecho.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoMarcaPasoCierre : short { Excepcion = 0, Confirmacion = 1 }

/// <summary>
/// CON-12 (86e3jvvme) · paso del checklist de cierre (P10.4 con base en R17 mientras Millet no mande el suyo, V13). Los automáticos
/// nacen de los verificadores registrados (la clave los une); Contabilidad puede cambiar su orden, si son obligatorios y si están activos,
/// y agregar pasos manuales propios.
/// </summary>
public sealed class PasoCierreContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int ClaveMaxima = 60, NombreMaximo = 120, DescripcionMaxima = 500;

    public Guid EmpresaId { get; set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public TipoPasoCierre Tipo { get; private set; }
    public AlcancePasoCierre Alcance { get; private set; }
    public int Orden { get; private set; }
    public bool Obligatorio { get; private set; }
    public bool Activo { get; private set; }

    private PasoCierreContable() { }

    public PasoCierreContable(Guid id, string clave, string nombre, string descripcion, TipoPasoCierre tipo, AlcancePasoCierre alcance,
        int orden, bool obligatorio) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > ClaveMaxima)
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_CLAVE_INVALIDA", $"La clave del paso es obligatoria (hasta {ClaveMaxima} caracteres).");
        Clave = clave.Trim().ToUpperInvariant();
        Tipo = tipo;
        Alcance = alcance;
        Activo = true;
        Configurar(nombre, descripcion, orden, obligatorio, true);
    }

    public void Configurar(string nombre, string? descripcion, int orden, bool obligatorio, bool activo)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > NombreMaximo)
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_NOMBRE_INVALIDO", $"El nombre del paso es obligatorio (hasta {NombreMaximo} caracteres).");
        if (descripcion?.Trim().Length > DescripcionMaxima)
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_DESCRIPCION_INVALIDA", $"La descripción admite hasta {DescripcionMaxima} caracteres.");
        if (orden is < 0 or > 10_000)
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_ORDEN_INVALIDO", "El orden debe estar entre 0 y 10000.");
        Nombre = nombre.Trim();
        Descripcion = descripcion?.Trim() ?? string.Empty;
        Orden = orden;
        Obligatorio = obligatorio;
        Activo = activo;
    }

    /// <summary>¿Aplica al periodo? Los mensuales a 1–12 y los anuales al 13.</summary>
    public bool AplicaA(int numeroPeriodo) => Alcance == AlcancePasoCierre.Anual
        ? numeroPeriodo == PeriodoContable.NumeroAjuste
        : numeroPeriodo != PeriodoContable.NumeroAjuste;
}

/// <summary>
/// Excepción autorizada (CON-12-a) o confirmación de un paso manual en un periodo: quién, cuándo y por qué. Se retira (no se borra)
/// si deja de valer; el cierre guarda la foto de las marcas vigentes.
/// </summary>
public sealed class MarcaPasoCierre : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MotivoMinimo = 10, MotivoMaximo = 500;

    public Guid EmpresaId { get; set; }
    public Guid PeriodoId { get; private set; }
    public string PasoClave { get; private set; } = string.Empty;
    public TipoMarcaPasoCierre Tipo { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid? UsuarioId { get; private set; }
    public string UsuarioNombre { get; private set; } = string.Empty;
    public DateTimeOffset RegistradaEn { get; private set; }
    public string? RetiradaPor { get; private set; }
    public DateTimeOffset? RetiradaEn { get; private set; }

    public bool Vigente => RetiradaEn is null;

    private MarcaPasoCierre() { }

    public MarcaPasoCierre(Guid id, Guid periodoId, string pasoClave, TipoMarcaPasoCierre tipo, string motivo, Guid? usuarioId,
        string usuarioNombre, DateTimeOffset ahora) : base(id)
    {
        if (motivo is null || motivo.Trim().Length is < MotivoMinimo or > MotivoMaximo)
            throw new BusinessRuleException("CONTAB_CIERRE_MOTIVO_INVALIDO",
                $"Indique el motivo (entre {MotivoMinimo} y {MotivoMaximo} caracteres): queda registrado con su nombre.");
        PeriodoId = periodoId;
        PasoClave = pasoClave;
        Tipo = tipo;
        Motivo = motivo.Trim();
        UsuarioId = usuarioId;
        UsuarioNombre = usuarioNombre;
        RegistradaEn = ahora;
    }

    public void Retirar(string usuarioNombre, DateTimeOffset ahora)
    {
        if (!Vigente)
            throw new ConflictException("CONTAB_CIERRE_MARCA_RETIRADA", "La marca ya se había retirado.");
        RetiradaPor = usuarioNombre;
        RetiradaEn = ahora;
    }
}

/// <summary>
/// Foto del checklist al cerrar el periodo (CON-12-a): cada paso con su estado y, si se cerró con excepción, quién la autorizó.
/// Un periodo reabierto y vuelto a cerrar tiene una foto por cierre.
/// </summary>
public sealed class CierrePeriodoRegistro : IPerteneceAEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid PeriodoId { get; private set; }
    public string CerradoPor { get; private set; } = string.Empty;
    public DateTimeOffset CerradoEn { get; private set; }
    public int Excepciones { get; private set; }
    /// <summary>JSON con la evaluación completa (<c>ChecklistCierreResponse</c>).</summary>
    public string Checklist { get; private set; } = "{}";

    private CierrePeriodoRegistro() { }

    public CierrePeriodoRegistro(Guid id, Guid periodoId, string cerradoPor, DateTimeOffset cerradoEn, int excepciones, string checklist)
    {
        Id = id;
        PeriodoId = periodoId;
        CerradoPor = cerradoPor;
        CerradoEn = cerradoEn;
        Excepciones = excepciones;
        Checklist = checklist;
    }
}
