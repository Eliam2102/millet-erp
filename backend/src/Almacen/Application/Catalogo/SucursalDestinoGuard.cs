using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// F5 (86e3pc50v): un documento dirigido a una sucursal no se opera en un almacén de otra; aplica aunque el usuario tenga bypass.
/// </summary>
internal static class SucursalDestinoGuard
{
    public static void Validar(Guid? sucursalDocumento, Guid? sucursalAlmacen, string codigo, string mensaje)
    {
        if (sucursalDocumento is Guid d && sucursalAlmacen is Guid a && d != a)
            throw new BusinessRuleException(codigo, mensaje);
    }
}
