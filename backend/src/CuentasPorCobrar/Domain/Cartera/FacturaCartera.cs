using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Cartera;

/// <summary>
/// Proyección local de comprobantes cobrables (§3.1 del 00-levantamiento,
/// §4.1 del 01-diseño, CXC-PR3). Se alimenta SOLO de los eventos de
/// Facturación — cero lectura directa a tablas del esquema
/// <c>facturacion</c> (regla de oro de la triada). Es la base de cartera,
/// antigüedad, saldo neto 13-K y estado de cuenta.
///
/// <para>
/// El cliente se correlaciona por <see cref="ReceptorRfc"/> (decisión
/// 2026-07-13 con el owner: RFC + read port). <see cref="ClienteId"/> es
/// nullable: receptor genérico/público o RFC sin match en el master
/// quedan fuera del cálculo de crédito pero visibles en cartera.
/// </para>
///
/// <para>
/// Invariante (§4.2): <c>monto_pagado + monto_nc ≤ total</c>;
/// transiciones solo por eventos idempotentes (correlación por id del
/// comprobante origen vía <see cref="MovimientoCartera"/>).
/// </para>
/// </summary>
public sealed class FacturaCartera : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaVentaId { get; private set; }
    public Guid? ClienteId { get; private set; }
    public string ReceptorRfc { get; private set; } = default!;
    public string ReceptorNombre { get; private set; } = default!;

    public string Uuid { get; private set; } = default!;
    public string Folio { get; private set; } = default!;
    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string MetodoPago { get; private set; } = default!;   // PUE / PPD

    public DateTimeOffset FechaTimbrado { get; private set; }
    public DateTimeOffset FechaVencimiento { get; private set; }

    public decimal MontoPagado { get; private set; }
    public decimal MontoNc { get; private set; }
    public EstadoFacturaCartera Estado { get; private set; }

    private FacturaCartera() { }

    public static FacturaCartera Crear(
        Guid empresaId,
        Guid facturaVentaId,
        Guid? clienteId,
        string receptorRfc,
        string receptorNombre,
        string uuid,
        string folio,
        decimal total,
        string moneda,
        string metodoPago,
        DateTimeOffset fechaTimbrado,
        DateTimeOffset fechaVencimiento)
    {
        if (facturaVentaId == Guid.Empty)
            throw new BusinessRuleException("FC_FACTURA_VACIA", "El id de la factura de venta es obligatorio.");
        if (total <= 0)
            throw new BusinessRuleException("FC_TOTAL_INVALIDO", "El total de la factura debe ser > 0.");
        if (fechaVencimiento < fechaTimbrado)
            throw new BusinessRuleException("FC_VENCIMIENTO_INVALIDO",
                "La fecha de vencimiento no puede ser anterior al timbrado.");

        return new FacturaCartera
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            FacturaVentaId = facturaVentaId,
            ClienteId = clienteId,
            ReceptorRfc = receptorRfc.Trim().ToUpperInvariant(),
            ReceptorNombre = receptorNombre.Trim(),
            Uuid = uuid,
            Folio = folio,
            Total = total,
            Moneda = moneda,
            MetodoPago = metodoPago,
            FechaTimbrado = fechaTimbrado,
            FechaVencimiento = fechaVencimiento,
            MontoPagado = 0m,
            MontoNc = 0m,
            Estado = EstadoFacturaCartera.Abierta,
        };
    }

    public decimal SaldoPendiente => Total - MontoPagado - MontoNc;

    public void AplicarPago(decimal importe)
    {
        AsegurarNoCancelada();
        if (importe <= 0)
            throw new BusinessRuleException("FC_IMPORTE_INVALIDO", "El importe del pago debe ser > 0.");
        if (MontoPagado + MontoNc + importe > Total)
            throw new BusinessRuleException("FC_SOBREPAGO",
                $"El pago de {importe} excede el saldo pendiente ({SaldoPendiente}) de la factura {Folio}.");

        MontoPagado += importe;
        RecalcularEstado();
    }

    public void RevertirPago(decimal importe)
    {
        if (importe <= 0 || importe > MontoPagado)
            throw new BusinessRuleException("FC_REVERSA_INVALIDA",
                $"No se puede revertir {importe}: el monto pagado acumulado es {MontoPagado}.");

        MontoPagado -= importe;
        RecalcularEstado();
    }

    public void AplicarNotaCredito(decimal importe)
    {
        AsegurarNoCancelada();
        if (importe <= 0)
            throw new BusinessRuleException("FC_IMPORTE_INVALIDO", "El importe de la NC debe ser > 0.");
        if (MontoPagado + MontoNc + importe > Total)
            throw new BusinessRuleException("FC_NC_EXCEDE",
                $"La NC de {importe} excede el saldo pendiente ({SaldoPendiente}) de la factura {Folio}.");

        MontoNc += importe;
        RecalcularEstado();
    }

    public void RevertirNotaCredito(decimal importe)
    {
        if (importe <= 0 || importe > MontoNc)
            throw new BusinessRuleException("FC_REVERSA_INVALIDA",
                $"No se puede revertir {importe}: el monto NC acumulado es {MontoNc}.");

        MontoNc -= importe;
        RecalcularEstado();
    }

    /// <summary>
    /// Baja fiscal: la factura de venta fue cancelada ante el SAT
    /// (<c>ComprobanteCanceladoIntegrationEvent</c>). Los acumulados se
    /// conservan como rastro; el estado la saca de cartera cobrable.
    /// </summary>
    public void Cancelar()
    {
        Estado = EstadoFacturaCartera.Cancelada;
    }

    /// <summary>
    /// Re-vincula el cliente cuando el RFC no tenía match al proyectar y
    /// el master se completó después (re-entrega o corrección manual).
    /// </summary>
    public void VincularCliente(Guid clienteId)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("FC_CLIENTE_VACIO", "El cliente es obligatorio.");
        ClienteId = clienteId;
    }

    private void AsegurarNoCancelada()
    {
        if (Estado == EstadoFacturaCartera.Cancelada)
            throw new BusinessRuleException("FC_CANCELADA",
                $"La factura {Folio} está cancelada — no acepta movimientos.");
    }

    private void RecalcularEstado()
    {
        if (Estado == EstadoFacturaCartera.Cancelada) return;

        var aplicado = MontoPagado + MontoNc;
        Estado = aplicado <= 0
            ? EstadoFacturaCartera.Abierta
            : aplicado < Total
                ? EstadoFacturaCartera.Parcial
                : EstadoFacturaCartera.Pagada;
    }
}
