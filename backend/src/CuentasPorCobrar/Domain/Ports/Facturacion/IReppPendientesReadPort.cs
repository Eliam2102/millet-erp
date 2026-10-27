namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

public interface IReppPendientesReadPort
{
    Task<ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken);

    /// <summary>
    /// C7.4 · parcialidades: saldo por cobrar y «parcialidad siguiente» de facturas PPD
    /// timbradas, con los mismos datos que <c>FacturasCobrablesPpdQuery</c> de Facturación
    /// (Total − NC − REP vigentes; REP vigentes + 1). Facturas no PPD o no timbradas no
    /// aparecen. Implementación por omisión vacía: CxC cae a su proyección de cartera.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ParcialidadPpd>> ParcialidadesAsync(
        IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<Guid, ParcialidadPpd>>(new Dictionary<Guid, ParcialidadPpd>());
}

/// <summary>Saldo y parcialidad siguiente de una factura PPD según Facturación (C7.4).</summary>
public sealed record ParcialidadPpd(Guid FacturaVentaId, decimal Saldo, int NumParcialidadSiguiente);

/// <summary>
/// Estado de REP del cliente. <see cref="Pendientes"/> (C7.8) detalla los
/// pagos aún pendientes de REP y los REP emitidos, para que el usuario vea
/// qué bloquea el estado de cuenta; es opcional para no romper consumidores
/// que solo necesitan el booleano.
/// </summary>
public sealed record ReppClienteEstado(
    bool TienePendientes,
    IReadOnlyList<Guid> RecibosEmitidos,
    IReadOnlyList<ReppPendienteResumen>? Pendientes = null);

/// <summary>
/// Pago confirmado del cliente visto desde la bandeja de REP pendientes.
/// <c>Estado</c>: "Pendiente" (sin REP) o "Emitido" (con <c>ReciboPagoId</c>).
/// </summary>
public sealed record ReppPendienteResumen(
    Guid Id,
    string Estado,
    DateOnly FechaValor,
    DateOnly FechaLimite,
    decimal Monto,
    string Moneda,
    string? Referencia,
    Guid? ReciboPagoId);
