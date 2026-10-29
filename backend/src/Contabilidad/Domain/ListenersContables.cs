using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// C1.5 · marca de idempotencia de los listeners de Contabilidad (mismo patrón que <c>cuentas_por_pagar.eventos_procesados</c>). La
/// póliza ya es idempotente por su llave; esta marca evita además volver a guardar cobros o consumos cuando Service Bus reentrega.
/// </summary>
public sealed class EventoContableProcesado : INotAudited
{
    public Guid EventoId { get; private set; }
    public string EventoTipo { get; private set; } = string.Empty;
    public DateTimeOffset ProcesadoEn { get; private set; }

    private EventoContableProcesado() { }

    public EventoContableProcesado(Guid eventoId, string eventoTipo, DateTimeOffset procesadoEn)
    {
        EventoId = eventoId;
        EventoTipo = eventoTipo;
        ProcesadoEn = procesadoEn;
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EstadoCobroEfectivo : short { Pendiente = 0, Liquidado = 1, Cancelado = 2 }

/// <summary>
/// C1.5 · #11b: cobro en efectivo de una sesión de caja, guardado hasta que la sesión se cierra (reglas de cobro del 23-oct: una
/// póliza por liquidación del cajero, día y sucursal). Al cerrar la sesión, los cobros pendientes forman la póliza y quedan
/// <see cref="EstadoCobroEfectivo.Liquidado"/> con su póliza.
/// </summary>
public sealed class CobroEfectivoCaja : IPerteneceAEmpresa, INotAudited
{
    public Guid CobroId { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid CajaSesionId { get; private set; }
    public Guid? SucursalId { get; private set; }
    public Guid? ClienteId { get; private set; }
    public decimal Importe { get; private set; }
    public DateTimeOffset CobradoEn { get; private set; }
    public EstadoCobroEfectivo Estado { get; private set; }
    public Guid? PolizaLiquidacionId { get; private set; }

    private CobroEfectivoCaja() { }

    public CobroEfectivoCaja(Guid cobroId, Guid empresaId, Guid cajaSesionId, Guid? sucursalId, Guid? clienteId, decimal importe, DateTimeOffset cobradoEn)
    {
        CobroId = cobroId;
        EmpresaId = empresaId;
        CajaSesionId = cajaSesionId;
        SucursalId = sucursalId;
        ClienteId = clienteId;
        Importe = importe;
        CobradoEn = cobradoEn;
    }

    public void Liquidar(Guid polizaId)
    {
        Estado = EstadoCobroEfectivo.Liquidado;
        PolizaLiquidacionId = polizaId;
    }

    public void Cancelar() => Estado = EstadoCobroEfectivo.Cancelado;
}

/// <summary>
/// C1.5 · #14/#15a: una línea de salida contabilizada con la cuenta y el centro de costo que usó su póliza de consumo. La devolución
/// interna reversa con la misma cuenta y centro de costo (V64), aunque el rol cambie de cuenta después.
/// </summary>
public sealed class ConsumoContabilizado : IPerteneceAEmpresa, INotAudited
{
    public Guid LineaSalidaId { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid SalidaId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public Guid? CentroCostoId { get; private set; }
    public Guid CuentaId { get; private set; }
    public decimal Monto { get; private set; }
    public Guid PolizaId { get; private set; }

    private ConsumoContabilizado() { }

    public ConsumoContabilizado(Guid lineaSalidaId, Guid empresaId, Guid salidaId, Guid articuloId, Guid? centroCostoId, Guid cuentaId,
        decimal monto, Guid polizaId)
    {
        LineaSalidaId = lineaSalidaId;
        EmpresaId = empresaId;
        SalidaId = salidaId;
        ArticuloId = articuloId;
        CentroCostoId = centroCostoId;
        CuentaId = cuentaId;
        Monto = monto;
        PolizaId = polizaId;
    }
}
