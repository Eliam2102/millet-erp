using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Proveedores;

/// <summary>
/// Error único cuando RQs y OCs usan un proveedor que no está Activo. Desde G1.1 (F1-ADM-05) el proveedor nuevo
/// nace «En revisión»: se responde <c>PROVEEDOR_EN_REVISION</c> para que el usuario sepa que falta la validación
/// de CxP, igual que CxP y Tesorería. Inactivo conserva <c>PROVEEDOR_INACTIVO</c>.
/// </summary>
internal static class ProveedorNoUtilizable
{
    public static BusinessRuleException Error(string clave, EstatusCatalogo estatus, string mensajeInactivo) =>
        estatus == EstatusCatalogo.EnRevision
            ? new BusinessRuleException(
                "PROVEEDOR_EN_REVISION",
                $"El proveedor '{clave}' está en revisión: Cuentas por Pagar debe validarlo antes de usarlo en requisiciones u órdenes de compra.")
            : new BusinessRuleException("PROVEEDOR_INACTIVO", mensajeInactivo);
}
