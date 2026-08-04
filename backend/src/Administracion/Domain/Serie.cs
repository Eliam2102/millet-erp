using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Agregado <c>Serie</c> del módulo Series y Folios (F-Admin-PR6.1).
/// Representa una configuración de generación de folios para un tipo
/// de documento (OC, CFDI, póliza, etc.) en una empresa, opcionalmente
/// acotada por sucursal.
///
/// <para>
/// El folio formateado se compone de:
/// <c>{Prefijo}-{PeriodoClave}-{numero:D6}</c> cuando hay reinicio (Anual o
/// Mensual), o <c>{Prefijo}{Sufijo?}-{numero:D6}</c> cuando <see cref="ReinicioPeriodo"/>
/// es <see cref="Domain.ReinicioPeriodo.None"/>. El cálculo vive en el
/// command <c>ReservarFolioCommand</c> + el VO físico que persista
/// el módulo consumer (en Compras, sigue siendo <see cref="Compras.Domain.Folio"/>
/// hasta que se deprecque en una fase B futura).
/// </para>
///
/// <para>
/// La unicidad efectiva por <c>(EmpresaId, SucursalId?, TipoDocumento,
/// Prefijo, Sufijo)</c> está garantizada por un índice único en la
/// configuración EF Core; el <c>NULL</c> de SucursalId se normaliza a
/// <c>Guid.Empty</c> para que el índice único PostgreSQL no descarte
/// duplicados (los NULLs no chocan por default).
/// </para>
///
/// PLATFORM-TODO(&lt;SeriesSchemaMigrate&gt;): Fase A guarda las tablas
/// <c>series</c> y <c>secuencias_folio</c> en el schema <c>compartido</c>
/// porque <see cref="Empresa"/> ya vive ahí y reusa el
/// <c>CompartidoDbContext</c>. Fase B (cuando Administración tenga su
/// propio DbContext y schema) hace migración aditiva renombrando a
/// <c>admin.*</c>.
/// </summary>
public sealed class Serie : BaseEntity, IAuditable
{
    public Guid EmpresaId { get; private set; }
    public Guid? SucursalId { get; private set; }
    public TipoDocumentoSerie TipoDocumento { get; private set; }
    public string Prefijo { get; private set; } = string.Empty;
    public string? Sufijo { get; private set; }
    public ReinicioPeriodo ReinicioPeriodo { get; private set; }
    public bool Activa { get; private set; } = true;

    private Serie() { } // EF Core

    public Serie(
        Guid id,
        Guid empresaId,
        Guid? sucursalId,
        TipoDocumentoSerie tipoDocumento,
        string prefijo,
        string? sufijo,
        ReinicioPeriodo reinicioPeriodo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SERIE_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("SERIE_EMPRESA_INVALIDA",
                "EmpresaId es obligatorio.");
        ValidarPrefijo(prefijo);
        ValidarSufijo(sufijo);

        EmpresaId = empresaId;
        SucursalId = sucursalId;
        TipoDocumento = tipoDocumento;
        Prefijo = prefijo;
        Sufijo = sufijo;
        ReinicioPeriodo = reinicioPeriodo;
    }

    /// <summary>
    /// PATCH parcial: <c>null</c> = no tocar; <c>limpiarSufijo = true</c>
    /// setea <see cref="Sufijo"/> a null. <see cref="EmpresaId"/>,
    /// <see cref="SucursalId"/> y <see cref="TipoDocumento"/> son
    /// inmutables (parte de la business key, cambios requieren alta
    /// nueva).
    /// </summary>
    public void ActualizarDatos(
        string? prefijo = null,
        string? sufijo = null,
        ReinicioPeriodo? reinicioPeriodo = null,
        bool limpiarSufijo = false)
    {
        if (prefijo is not null)
        {
            ValidarPrefijo(prefijo);
            Prefijo = prefijo;
        }
        if (sufijo is not null)
        {
            ValidarSufijo(sufijo);
            Sufijo = sufijo;
        }
        else if (limpiarSufijo)
        {
            Sufijo = null;
        }
        if (reinicioPeriodo.HasValue)
        {
            ReinicioPeriodo = reinicioPeriodo.Value;
        }
    }

    /// <summary>Reactiva la serie. Idempotente.</summary>
    public void Activar() => Activa = true;

    /// <summary>Desactiva la serie. Idempotente.</summary>
    public void Desactivar() => Activa = false;

    /// <summary>
    /// Calcula la clave de período según la política de reinicio. Útil
    /// para queries y para componer el folio. Sin acoplar el dominio a
    /// formato cultural (siempre InvariantCulture).
    /// </summary>
    public static string CalcularPeriodoClave(ReinicioPeriodo reinicio, DateOnly fecha) =>
        reinicio switch
        {
            ReinicioPeriodo.None => string.Empty,
            ReinicioPeriodo.Anual => fecha.Year.ToString("D4", CultureInfo.InvariantCulture),
            ReinicioPeriodo.Mensual => fecha.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

    private static void ValidarPrefijo(string prefijo)
    {
        if (string.IsNullOrWhiteSpace(prefijo))
            throw new BusinessRuleException("SERIE_PREFIJO_INVALIDO",
                "El prefijo es obligatorio.");
        if (prefijo.Length > 10)
            throw new BusinessRuleException("SERIE_PREFIJO_INVALIDO",
                "El prefijo no puede exceder 10 caracteres.");
    }

    private static void ValidarSufijo(string? sufijo)
    {
        if (sufijo is { Length: > 10 })
            throw new BusinessRuleException("SERIE_SUFIJO_INVALIDO",
                "El sufijo no puede exceder 10 caracteres.");
    }
}
