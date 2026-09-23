using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Catalogos;

/// <summary>
/// Política de viáticos por puesto + tipo de destino (§7.4.2, §13.1
/// punto 3 del 00-levantamiento, F7-PR3). Define el tope automático
/// que <c>SolicitudViaticos</c> usa al validar la solicitud del
/// empleado: <c>tope = MontoMaxDia * dias_estimados</c>; si la solicitud
/// excede el tope o supera <see cref="DiasMax"/>, el flujo requiere
/// autorización adicional de Dirección de Finanzas.
///
/// <para>
/// Mantenimiento por RH + Dirección. La unicidad
/// <c>(PuestoId, TipoDestino)</c> se garantiza con índice único en
/// EF Core.
/// </para>
/// </summary>
public sealed class PoliticaViaticos : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid PuestoId { get; private set; }
    public TipoDestinoViatico TipoDestino { get; private set; }
    public decimal MontoMaxDia { get; private set; }
    public int DiasMax { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    private PoliticaViaticos() { }

    public static PoliticaViaticos Crear(
        Guid empresaId,
        Guid puestoId,
        TipoDestinoViatico tipoDestino,
        decimal montoMaxDia,
        int diasMax,
        string moneda)
    {
        if (puestoId == Guid.Empty)
            throw new BusinessRuleException("POLITICA_PUESTO_VACIO",
                "El puesto es obligatorio.");
        if (montoMaxDia <= 0)
            throw new BusinessRuleException("POLITICA_MONTO_INVALIDO",
                "El monto máximo por día debe ser > 0.");
        if (diasMax <= 0)
            throw new BusinessRuleException("POLITICA_DIAS_INVALIDO",
                "Los días máximos deben ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("POLITICA_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");

        return new PoliticaViaticos
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            PuestoId = puestoId,
            TipoDestino = tipoDestino,
            MontoMaxDia = montoMaxDia,
            DiasMax = diasMax,
            Moneda = moneda.ToUpperInvariant(),
        };
    }

    public void Actualizar(decimal montoMaxDia, int diasMax, string moneda)
    {
        if (montoMaxDia <= 0)
            throw new BusinessRuleException("POLITICA_MONTO_INVALIDO",
                "El monto máximo por día debe ser > 0.");
        if (diasMax <= 0)
            throw new BusinessRuleException("POLITICA_DIAS_INVALIDO",
                "Los días máximos deben ser > 0.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("POLITICA_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");

        MontoMaxDia = montoMaxDia;
        DiasMax = diasMax;
        Moneda = moneda.ToUpperInvariant();
    }

    /// <summary>
    /// Calcula el tope para una solicitud según días estimados.
    /// </summary>
    public decimal CalcularTope(int diasEstimados) =>
        MontoMaxDia * diasEstimados;

    /// <summary>
    /// Devuelve true si la solicitud excede política (monto > tope o
    /// días > DiasMax).
    /// </summary>
    public bool ExcedePolitica(decimal montoSolicitado, int diasEstimados) =>
        montoSolicitado > CalcularTope(diasEstimados) || diasEstimados > DiasMax;
}
