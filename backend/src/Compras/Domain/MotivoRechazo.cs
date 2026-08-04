using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>
/// Catálogo de motivos para terminar una requisición (rechazo /
/// eliminación / cancelación). Diseño §3.bis.3. Cross-empresa: vive en
/// <c>compras.motivos_rechazo</c> y aplica a todas las empresas.
///
/// Implementa <see cref="IAuditable"/> (cambios al catálogo se auditan).
/// NO implementa <see cref="IPerteneceAEmpresa"/> (es global) ni
/// <see cref="IFiscalmenteRelevante"/> (no se borra; se desactiva con
/// <see cref="Activo"/>).
/// </summary>
public sealed class MotivoRechazo : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    public bool PermiteTextoLibre { get; private set; }

    public MotivoRechazoAplicaA AplicaA { get; private set; }

    public bool Activo { get; private set; }

    private MotivoRechazo() { }

    public MotivoRechazo(
        Guid id,
        string clave,
        string descripcion,
        MotivoRechazoAplicaA aplicaA,
        bool permiteTextoLibre = false,
        bool activo = true) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
        {
            throw new BusinessRuleException(
                "MOTIVO_CLAVE_INVALIDA",
                "La clave del motivo es requerida y no puede exceder 20 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 200)
        {
            throw new BusinessRuleException(
                "MOTIVO_DESCRIPCION_INVALIDA",
                "La descripción del motivo es requerida y no puede exceder 200 caracteres.");
        }

        if (aplicaA == MotivoRechazoAplicaA.Ninguno)
        {
            throw new BusinessRuleException(
                "MOTIVO_APLICA_A_VACIO",
                "El motivo debe aplicar al menos a una de: rechazo, eliminación, cancelación.");
        }

        Clave = clave;
        Descripcion = descripcion;
        AplicaA = aplicaA;
        PermiteTextoLibre = permiteTextoLibre;
        Activo = activo;
    }
}
