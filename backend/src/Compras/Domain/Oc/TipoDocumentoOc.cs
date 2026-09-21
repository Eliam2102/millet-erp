using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Catálogo de tipos de documento adjunto de OC (diseño §4.12, C6).
/// Tabla <c>compras.tipos_documento_oc</c> con seed inicial de 7 tipos:
/// cotizacion, ficha_tecnica, correo_autorizacion, pedimento,
/// factura_proveedor_extranjero, packing_list, otro.
///
/// Catálogo global (no <see cref="IPerteneceAEmpresa"/>) y no
/// <see cref="IFiscalmenteRelevante"/> — el desactivar usa
/// <see cref="Activo"/> en lugar de soft-delete. NO implementa
/// <see cref="IAuditable"/>: cambios al catálogo son administrativos
/// y poco frecuentes; si se requiere auditoría futura, se promueve.
///
/// <see cref="ObligatorioSiImportacion"/>: si true, el flujo
/// "EnviarAAutorización" de una OC con <c>EsImportacion = true</c>
/// exige al menos un adjunto de este tipo (regla aplicada en F3-PR1).
/// En el seed inicial, solo <c>ficha_tecnica</c> tiene este flag.
/// </summary>
public sealed class TipoDocumentoOc : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool ObligatorioSiImportacion { get; private set; }
    public bool Activo { get; private set; }

    /// <summary>Constructor para EF Core.</summary>
    private TipoDocumentoOc() { }

    public TipoDocumentoOc(
        Guid id,
        string clave,
        string descripcion,
        bool obligatorioSiImportacion = false,
        bool activo = true) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 40)
        {
            throw new BusinessRuleException(
                "TIPO_DOC_CLAVE_INVALIDA",
                "La clave del tipo de documento debe tener 1-40 caracteres.");
        }
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 200)
        {
            throw new BusinessRuleException(
                "TIPO_DOC_DESCRIPCION_INVALIDA",
                "La descripción del tipo de documento debe tener 1-200 caracteres.");
        }

        Clave = clave;
        Descripcion = descripcion;
        ObligatorioSiImportacion = obligatorioSiImportacion;
        Activo = activo;
    }
}
