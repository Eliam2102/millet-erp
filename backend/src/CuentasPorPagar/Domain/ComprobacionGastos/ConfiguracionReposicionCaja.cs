using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Monto mínimo acumulado para emitir la reposición de caja chica de
/// una sucursal (doc 12 §D2/Q4). Sucursal sin configuración = mínimo 0
/// = la reposición se emite en cuanto se aplica cada comprobación.
/// </summary>
public sealed class ConfiguracionReposicionCaja : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid SucursalId { get; private set; }

    public decimal MontoMinimo { get; private set; }

    private ConfiguracionReposicionCaja() { }

    public static ConfiguracionReposicionCaja Crear(
        Guid empresaId, Guid sucursalId, decimal montoMinimo)
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("REPO_CONFIG_SUCURSAL_VACIA",
                "La sucursal es obligatoria.");

        var config = new ConfiguracionReposicionCaja
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SucursalId = sucursalId,
        };
        config.ActualizarMinimo(montoMinimo);
        return config;
    }

    public void ActualizarMinimo(decimal montoMinimo)
    {
        if (montoMinimo < 0)
            throw new BusinessRuleException("REPO_CONFIG_MINIMO_INVALIDO",
                "El monto mínimo no puede ser negativo.");
        MontoMinimo = montoMinimo;
    }
}
