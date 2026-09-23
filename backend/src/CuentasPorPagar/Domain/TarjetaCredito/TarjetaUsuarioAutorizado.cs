using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Usuario autorizado a usar una <see cref="Tarjeta"/> (§3.4 del anexo
/// TC, F7-PR4). PK compuesta: (tarjeta_id, empleado_id, vigencia_desde).
///
/// <para>
/// El <c>titular_id</c> de la tarjeta NO se duplica aquí — el titular
/// puede o no estar listado como usuario autorizado dependiendo de si
/// también usa la TC operativamente (D10).
/// </para>
/// </summary>
public sealed class TarjetaUsuarioAutorizado : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid TarjetaId { get; private set; }
    public Guid EmpleadoId { get; private set; }
    public DateOnly VigenciaDesde { get; private set; }
    public DateOnly? VigenciaHasta { get; private set; }

    /// <summary>Tope mensual individual. Null = sin tope individual (aplica el de la tarjeta).</summary>
    public decimal? MontoMaxMensualMxn { get; private set; }

    private TarjetaUsuarioAutorizado() { }

    internal TarjetaUsuarioAutorizado(
        Guid id,
        Guid empresaId,
        Guid tarjetaId,
        Guid empleadoId,
        DateOnly vigenciaDesde,
        DateOnly? vigenciaHasta,
        decimal? montoMaxMensualMxn) : base(id)
    {
        if (empleadoId == Guid.Empty)
            throw new BusinessRuleException("TC_USR_EMPLEADO_VACIO",
                "El empleado autorizado es obligatorio.");
        if (vigenciaHasta is DateOnly hasta && hasta < vigenciaDesde)
            throw new BusinessRuleException("TC_USR_VIGENCIA_INVALIDA",
                "VigenciaHasta no puede ser anterior a VigenciaDesde.");
        if (montoMaxMensualMxn is decimal monto && monto <= 0)
            throw new BusinessRuleException("TC_USR_TOPE_INVALIDO",
                "El tope mensual individual debe ser > 0 si se especifica.");

        EmpresaId = empresaId;
        TarjetaId = tarjetaId;
        EmpleadoId = empleadoId;
        VigenciaDesde = vigenciaDesde;
        VigenciaHasta = vigenciaHasta;
        MontoMaxMensualMxn = montoMaxMensualMxn;
    }

    internal void Cerrar(DateOnly fecha)
    {
        if (fecha < VigenciaDesde)
            throw new BusinessRuleException("TC_USR_VIGENCIA_INVALIDA",
                "La fecha de cierre no puede ser anterior a VigenciaDesde.");
        VigenciaHasta = fecha;
    }

    public bool EstaVigente(DateOnly fecha) =>
        VigenciaDesde <= fecha && (VigenciaHasta is null || VigenciaHasta >= fecha);
}
