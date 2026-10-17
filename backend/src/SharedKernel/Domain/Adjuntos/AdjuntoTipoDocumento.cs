using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>
/// Catálogo de tipos de documento por tipo de entidad dueña (p. ej. los
/// documentos del expediente de un proveedor). Otros módulos siembran sus
/// propios tipos con otro <see cref="TipoEntidad"/> (F1-ADM-11 G1.2).
/// Tabla <c>compartido.adjunto_tipos_documento</c>.
/// </summary>
public sealed class AdjuntoTipoDocumento : BaseEntity, IAuditable
{
    /// <summary>Entidad dueña en snake_case, p. ej. <c>proveedor</c>.</summary>
    public string TipoEntidad { get; private set; } = string.Empty;

    /// <summary>Código estable, único por <see cref="TipoEntidad"/>.</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    public int Orden { get; private set; }

    /// <summary>Forma parte del expediente mínimo para considerarlo completo.</summary>
    public bool Obligatorio { get; private set; }

    /// <summary>Meses de vigencia por defecto; <c>null</c> = el documento no vence.</summary>
    public int? VigenciaMeses { get; private set; }

    /// <summary>Aplica solo a entidades que son persona moral.</summary>
    public bool SoloPersonaMoral { get; private set; }

    /// <summary>
    /// Aplica solo a entidades nacionales (p. ej. constancia de situación
    /// fiscal del SAT; un proveedor extranjero no la tiene). 86e3pdmee · ADM-01.
    /// </summary>
    public bool SoloNacional { get; private set; }

    public bool Activo { get; private set; } = true;

    private AdjuntoTipoDocumento() { }

    /// <summary>
    /// Matriz configurable por Millet (86e3pdmee · ADM-01): obligatoriedad,
    /// vigencia y a quién aplica. Cada cambio queda en la bitácora (IAuditable).
    /// </summary>
    public void Configurar(string nombre, bool obligatorio, int? vigenciaMeses, bool soloPersonaMoral, bool soloNacional, bool activo)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 200)
            throw new BusinessRuleException("ADJUNTO_TIPO_NOMBRE_INVALIDO", "El nombre del tipo de documento debe tener 1-200 caracteres.");
        if (vigenciaMeses is <= 0)
            throw new BusinessRuleException("ADJUNTO_TIPO_VIGENCIA_INVALIDA", "Los meses de vigencia deben ser mayores a cero.");
        Nombre = nombre.Trim();
        Obligatorio = obligatorio;
        VigenciaMeses = vigenciaMeses;
        SoloPersonaMoral = soloPersonaMoral;
        SoloNacional = soloNacional;
        Activo = activo;
    }

    public AdjuntoTipoDocumento(
        Guid id,
        string tipoEntidad,
        string codigo,
        string nombre,
        int orden,
        bool obligatorio,
        int? vigenciaMeses,
        bool soloPersonaMoral) : base(id)
    {
        if (string.IsNullOrWhiteSpace(tipoEntidad) || tipoEntidad.Length > 60)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_ENTIDAD_INVALIDO", "El tipo de entidad debe tener 1-60 caracteres.");
        }
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 80)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_CODIGO_INVALIDO", "El código del tipo de documento debe tener 1-80 caracteres.");
        }
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 200)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_NOMBRE_INVALIDO", "El nombre del tipo de documento debe tener 1-200 caracteres.");
        }
        if (vigenciaMeses is <= 0)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_VIGENCIA_INVALIDA", "Los meses de vigencia deben ser mayores a cero.");
        }

        TipoEntidad = tipoEntidad;
        Codigo = codigo;
        Nombre = nombre;
        Orden = orden;
        Obligatorio = obligatorio;
        VigenciaMeses = vigenciaMeses;
        SoloPersonaMoral = soloPersonaMoral;
    }
}
