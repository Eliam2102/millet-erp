using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Uso CFDI SAT del catálogo cross-empresa <c>compartido.usos_cfdi</c>
/// (F-Admin-PR5.3). Catálogo oficial <c>c_UsoCFDI</c> del SAT (3
/// caracteres): G01 Adquisición de mercancías, G03 Gastos en general,
/// P01 Por definir, etc. Usado por Facturación CFDI 4.0 para validar
/// el uso declarado por el receptor.
///
/// <para>
/// Read-mostly: actualizaciones via migraciones aditivas cuando el SAT
/// publica versiones nuevas; no hay CRUD en UI.
/// </para>
/// </summary>
public sealed class UsoCfdi : BaseEntity, IAuditable
{
    /// <summary>Clave SAT (3 chars alfanuméricos): G01, I04, D01, S01, CP01, CN01...</summary>
    public string ClaveSat { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    /// <summary>A qué tipo de persona aplica este uso (regla SAT explícita).</summary>
    public AplicaTipoPersona AplicaTipoPersona { get; private set; }

    public bool Activa { get; private set; } = true;

    private UsoCfdi() { }

    public UsoCfdi(
        Guid id,
        string claveSat,
        string descripcion,
        AplicaTipoPersona aplicaTipoPersona,
        bool activa = true) : base(id)
    {
        if (string.IsNullOrWhiteSpace(claveSat) || claveSat.Length is < 3 or > 4)
            throw new BusinessRuleException("USO_CFDI_CLAVE_INVALIDA",
                "La clave SAT debe tener 3-4 caracteres.");
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 254)
            throw new BusinessRuleException("USO_CFDI_DESCRIPCION_INVALIDA",
                "La descripción es requerida y no puede exceder 254 caracteres.");

        ClaveSat = claveSat;
        Descripcion = descripcion;
        AplicaTipoPersona = aplicaTipoPersona;
        Activa = activa;
    }
}
