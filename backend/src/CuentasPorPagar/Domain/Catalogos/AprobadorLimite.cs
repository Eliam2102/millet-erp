using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Catalogos;

/// <summary>
/// Catálogo local en CxP de aprobadores con límite por tipo de gasto
/// (§5.5, §13.1 punto 2 del 00-levantamiento, F7-PR3). Si un aprobador
/// excede su <see cref="MontoMax"/>, el flujo escala automáticamente a
/// Dirección de Finanzas.
///
/// <para>
/// Mantenimiento por RH o por el responsable de CxP. Permite
/// vigencias para retirar permisos sin perder histórico.
/// </para>
/// </summary>
public sealed class AprobadorLimite : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid EmpleadoId { get; private set; }
    public TipoGastoAprobador TipoGasto { get; private set; }
    public decimal MontoMax { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    public DateOnly VigenciaDesde { get; private set; }
    public DateOnly? VigenciaHasta { get; private set; }

    private AprobadorLimite() { }

    public static AprobadorLimite Crear(
        Guid empresaId,
        Guid empleadoId,
        TipoGastoAprobador tipoGasto,
        decimal montoMax,
        string moneda,
        DateOnly vigenciaDesde,
        DateOnly? vigenciaHasta)
    {
        if (empleadoId == Guid.Empty)
            throw new BusinessRuleException("APROBADOR_EMPLEADO_VACIO",
                "El empleado es obligatorio.");
        if (montoMax <= 0)
            throw new BusinessRuleException("APROBADOR_MONTO_INVALIDO",
                "El monto máximo debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("APROBADOR_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (vigenciaHasta is DateOnly hasta && hasta < vigenciaDesde)
            throw new BusinessRuleException("APROBADOR_VIGENCIA_INVALIDA",
                "VigenciaHasta no puede ser anterior a VigenciaDesde.");

        return new AprobadorLimite
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            EmpleadoId = empleadoId,
            TipoGasto = tipoGasto,
            MontoMax = montoMax,
            Moneda = moneda.ToUpperInvariant(),
            VigenciaDesde = vigenciaDesde,
            VigenciaHasta = vigenciaHasta,
        };
    }

    public void ActualizarLimite(decimal montoMax, string moneda)
    {
        if (montoMax <= 0)
            throw new BusinessRuleException("APROBADOR_MONTO_INVALIDO",
                "El monto máximo debe ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("APROBADOR_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");

        MontoMax = montoMax;
        Moneda = moneda.ToUpperInvariant();
    }

    public void Cerrar(DateOnly fecha)
    {
        if (fecha < VigenciaDesde)
            throw new BusinessRuleException("APROBADOR_VIGENCIA_INVALIDA",
                "La fecha de cierre no puede ser anterior a VigenciaDesde.");

        VigenciaHasta = fecha;
    }

    /// <summary>
    /// Indica si este aprobador puede autorizar un monto dado en una
    /// fecha específica.
    /// </summary>
    public bool PuedeAutorizar(decimal monto, DateOnly fecha)
    {
        if (fecha < VigenciaDesde) return false;
        if (VigenciaHasta is DateOnly hasta && fecha > hasta) return false;
        return monto <= MontoMax;
    }
}
