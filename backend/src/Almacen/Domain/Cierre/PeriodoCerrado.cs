using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Cierre;

/// <summary>
/// Periodo contable cerrado del módulo Almacén (F8-PR2). Tabla
/// <c>almacen.periodos_cerrados</c> con UNIQUE
/// <c>(empresa_id, año, mes)</c>. Una vez cerrado un periodo,
/// movimientos con <c>fecha_movimiento</c> dentro de ese mes son
/// rechazados con 422 por todos los handlers de captura (cuidado §6.1
/// del 04-cuidados-infra).
/// </summary>
public sealed class PeriodoCerrado : BaseEntity
{
    public Guid EmpresaId { get; private set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public DateTimeOffset CerradoAt { get; private set; }
    public Guid CerradoPor { get; private set; }

    private PeriodoCerrado() { }

    public PeriodoCerrado(Guid id, Guid empresaId, int anio, int mes, Guid cerradoPor) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("PERIODO_SIN_EMPRESA",
                "El periodo cerrado requiere empresa.");
        if (anio is < 2000 or > 2999)
            throw new BusinessRuleException("PERIODO_ANIO_INVALIDO",
                "Año fuera de rango razonable.");
        if (mes is < 1 or > 12)
            throw new BusinessRuleException("PERIODO_MES_INVALIDO",
                "Mes debe estar entre 1 y 12.");
        if (cerradoPor == Guid.Empty)
            throw new BusinessRuleException("PERIODO_SIN_USUARIO",
                "Se requiere usuario que cierra el periodo.");

        EmpresaId = empresaId;
        Anio = anio;
        Mes = mes;
        CerradoAt = DateTimeOffset.UtcNow;
        CerradoPor = cerradoPor;
    }
}
