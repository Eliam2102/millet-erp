using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// Catálogo de vehículos para Carta Porte 3.1 (§4.6 levantamiento). Datos del
/// autotransporte federal que el complemento exige (placa, configuración
/// vehicular, permiso SCT, seguro). Catálogo del módulo Facturación.
/// </summary>
public sealed class Vehiculo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string Placa { get; private set; } = string.Empty;

    /// <summary>Clave de configuración vehicular (<c>c_ConfigAutotransporte</c>).</summary>
    public string ConfigVehicular { get; private set; } = string.Empty;

    public int AnioModelo { get; private set; }

    /// <summary>Permiso SCT (<c>c_TipoPermiso</c>) + número.</summary>
    public string? TipoPermisoSct { get; private set; }
    public string? NumPermisoSct { get; private set; }

    public string? Aseguradora { get; private set; }
    public string? PolizaSeguro { get; private set; }

    /// <summary>
    /// Peso bruto vehicular en toneladas (atributo <c>PesoBrutoVehicular</c> del
    /// nodo Autotransporte — obligatorio en Carta Porte 3.1). Nullable en el
    /// catálogo; el builder de emisión lo exige al timbrar una Carta Porte.
    /// </summary>
    public decimal? PesoBrutoVehicular { get; private set; }

    public bool Activo { get; private set; } = true;

    private Vehiculo() { }

    private Vehiculo(
        Guid id, Guid empresaId, string placa, string configVehicular, int anioModelo,
        string? tipoPermisoSct, string? numPermisoSct, string? aseguradora, string? polizaSeguro,
        decimal? pesoBrutoVehicular) : base(id)
    {
        EmpresaId = empresaId;
        Placa = placa.ToUpperInvariant();
        ConfigVehicular = configVehicular;
        AnioModelo = anioModelo;
        TipoPermisoSct = tipoPermisoSct;
        NumPermisoSct = numPermisoSct;
        Aseguradora = aseguradora;
        PolizaSeguro = polizaSeguro;
        PesoBrutoVehicular = pesoBrutoVehicular;
    }

    public static Vehiculo Crear(
        Guid empresaId, string placa, string configVehicular, int anioModelo,
        string? tipoPermisoSct = null, string? numPermisoSct = null, string? aseguradora = null, string? polizaSeguro = null,
        decimal? pesoBrutoVehicular = null)
    {
        if (string.IsNullOrWhiteSpace(placa))
            throw new BusinessRuleException("VEHICULO_PLACA_INVALIDA", "La placa del vehículo es obligatoria.");
        if (string.IsNullOrWhiteSpace(configVehicular))
            throw new BusinessRuleException("VEHICULO_CONFIG_INVALIDA", "La configuración vehicular es obligatoria.");
        if (pesoBrutoVehicular is <= 0)
            throw new BusinessRuleException("VEHICULO_PESO_BRUTO_INVALIDO", "El peso bruto vehicular debe ser mayor que cero (toneladas).");

        return new Vehiculo(Guid.CreateVersion7(), empresaId, placa, configVehicular, anioModelo,
            tipoPermisoSct, numPermisoSct, aseguradora, polizaSeguro, pesoBrutoVehicular);
    }

    /// <summary>
    /// Actualiza los datos editables del vehículo. La placa es inmutable —
    /// es la identidad operativa del vehículo en el catálogo. Los campos
    /// opcionales enviados como <c>null</c> se limpian (semántica de
    /// reemplazo completo de los datos editables).
    /// </summary>
    public void Actualizar(
        string configVehicular, int anioModelo,
        string? tipoPermisoSct, string? numPermisoSct, string? aseguradora, string? polizaSeguro,
        decimal? pesoBrutoVehicular)
    {
        if (string.IsNullOrWhiteSpace(configVehicular))
            throw new BusinessRuleException("VEHICULO_CONFIG_INVALIDA", "La configuración vehicular es obligatoria.");
        if (pesoBrutoVehicular is <= 0)
            throw new BusinessRuleException("VEHICULO_PESO_BRUTO_INVALIDO", "El peso bruto vehicular debe ser mayor que cero (toneladas).");

        ConfigVehicular = configVehicular;
        AnioModelo = anioModelo;
        TipoPermisoSct = tipoPermisoSct;
        NumPermisoSct = numPermisoSct;
        Aseguradora = aseguradora;
        PolizaSeguro = polizaSeguro;
        PesoBrutoVehicular = pesoBrutoVehicular;
    }

    /// <summary>Reactiva el vehículo. Idempotente — si ya está activo, no-op.</summary>
    public void Activar() => Activo = true;

    /// <summary>
    /// Desactiva el vehículo: sale de los selectores de emisión sin tocar
    /// las Cartas Porte históricas que lo referencian. Idempotente.
    /// </summary>
    public void Desactivar() => Activo = false;
}
