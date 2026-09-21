using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Reposición agregada de caja chica (doc 12 §D2, GI-PR1). Agrupa N
/// comprobaciones de caja chica <c>Aplicadas</c> de la misma
/// <c>(sucursal, destino)</c> y representa el pasivo interno real hacia
/// Tesorería (los proveedores de los CFDIs ya cobraron en efectivo — lo
/// que se debe es reponer la caja). Análoga al corte de TC contra el
/// banco.
///
/// <para>
/// Se emite cuando el saldo acumulado por reponer alcanza el monto
/// mínimo configurado para la sucursal (Q4), o por corte manual para
/// vaciar el saldo sin alcanzar el mínimo. Al emitirse publica
/// <c>pasivo.autorizado-para-pago.v1</c> con
/// <c>TipoBeneficiario = CajaSucursal | Empleado</c> y
/// <c>OrigenTipo = ReposicionCajaChica</c>.
/// </para>
/// </summary>
public sealed class ReposicionCajaChica : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid SucursalId { get; private set; }

    public DestinoReposicionCaja Destino { get; private set; }

    /// <summary>Sucursal (CuentaSucursal) o empleado responsable (Responsable) según el destino.</summary>
    public Guid BeneficiarioId { get; private set; }

    public string Moneda { get; private set; } = "MXN";

    public decimal MontoTotal { get; private set; }

    public int NumeroComprobaciones { get; private set; }

    /// <summary>True si se emitió por corte manual (sin alcanzar el mínimo).</summary>
    public bool EsCorteManual { get; private set; }

    public Guid? EmitidaPor { get; private set; }
    public DateTimeOffset FechaEmision { get; private set; }

    private ReposicionCajaChica() { }

    public static ReposicionCajaChica Emitir(
        Guid empresaId,
        Guid sucursalId,
        DestinoReposicionCaja destino,
        Guid beneficiarioId,
        string moneda,
        decimal montoTotal,
        int numeroComprobaciones,
        bool esCorteManual,
        Guid? emitidaPor,
        DateTimeOffset ahora)
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("REPO_SUCURSAL_VACIA",
                "La sucursal de la reposición es obligatoria.");
        if (beneficiarioId == Guid.Empty)
            throw new BusinessRuleException("REPO_BENEFICIARIO_VACIO",
                "El beneficiario de la reposición es obligatorio.");
        if (montoTotal <= 0)
            throw new BusinessRuleException("REPO_MONTO_INVALIDO",
                "El monto de la reposición debe ser > 0.");
        if (numeroComprobaciones <= 0)
            throw new BusinessRuleException("REPO_SIN_COMPROBACIONES",
                "Una reposición debe cubrir al menos una comprobación.");

        return new ReposicionCajaChica
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SucursalId = sucursalId,
            Destino = destino,
            BeneficiarioId = beneficiarioId,
            Moneda = moneda.ToUpperInvariant(),
            MontoTotal = montoTotal,
            NumeroComprobaciones = numeroComprobaciones,
            EsCorteManual = esCorteManual,
            EmitidaPor = emitidaPor,
            FechaEmision = ahora,
        };
    }
}
