using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;

namespace Millet.Tesoreria.Application.Pagos;

/// <summary>
/// Reglas de destino de un egreso a proveedor (F1-ADM-05 G1.1, 86e3pdmee): el proveedor debe existir,
/// estar Activo y fuera de revisión, y se paga solo a una cuenta activa en la moneda de la cuenta de egreso.
/// Compartido por pago directo y pago a cuenta.
/// </summary>
public static class ReglasDestinoProveedor
{
    public static CuentaProveedorDto Resolver(ProveedorBancoDto? info, string monedaEgreso, Guid? cuentaProveedorId)
    {
        if (info is null)
            throw new BusinessRuleException("PROVEEDOR_NO_ENCONTRADO",
                "El proveedor del pago no existe o no tiene datos bancarios; regístralo y valídalo en Datos maestros antes de pagar.");
        // 86e3tactf: bloqueo total; cubre pago directo y pago a cuenta (ambos pasan por aquí).
        if (info.Bloqueado)
            throw new BusinessRuleException("PROVEEDOR_BLOQUEADO",
                $"Proveedor bloqueado: '{info.RazonSocial}' no admite pagos ({info.MotivoBloqueo}).");
        if (info.EnRevision)
            throw new BusinessRuleException("PROVEEDOR_EN_REVISION",
                $"El proveedor '{info.RazonSocial}' está en revisión y no admite pagos.");
        if (!info.Activo)
            throw new BusinessRuleException("PROVEEDOR_NO_ACTIVO",
                $"El proveedor '{info.RazonSocial}' no está activo y no admite pagos.");

        var elegida = cuentaProveedorId is Guid id
            ? info.Cuentas.FirstOrDefault(c => c.Id == id)
            : info.CuentaPara(monedaEgreso);
        if (elegida is null && cuentaProveedorId is null
            && info.Cuentas.Count(c => c.Activa && c.Moneda == monedaEgreso) > 1)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_PREDETERMINADA_REQUERIDA",
                $"El proveedor '{info.RazonSocial}' tiene varias cuentas activas en {monedaEgreso} y ninguna es la predeterminada. Marca una como predeterminada en Datos maestros antes de pagar.");
        if (elegida is null)
            throw new BusinessRuleException("PROVEEDOR_SIN_CUENTA_ACTIVA",
                $"El proveedor '{info.RazonSocial}' no tiene una cuenta activa en {monedaEgreso}. Regístrala y valídala en Datos maestros antes de pagar.");
        if (!elegida.Activa)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_ACTIVA",
                $"La cuenta {elegida.Banco} {Clabe.Enmascarar(elegida.Numero)} no está activa; solo se paga a cuentas activas.");
        if (elegida.Moneda != monedaEgreso)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_OTRA_MONEDA",
                $"La cuenta del proveedor es {elegida.Moneda} y la cuenta de egreso es {monedaEgreso}.");
        return elegida;
    }
}
