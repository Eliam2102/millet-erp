using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.AplicacionPagos;

/// <summary>Estados de una <see cref="PropuestaAplicacionPago"/> (§3.1).</summary>
public enum EstadoPropuestaAplicacion : short
{
    Propuesta = 1,
    Confirmada = 2,
    Rechazada = 3,
}

/// <summary>
/// Propuesta de aplicación de pago (§3.1 del 00-levantamiento, §4.1-4.2
/// del 01-diseño, CXC-PR7): matching depósito ↔ facturas construido desde
/// el remittance del cliente. CxC PROPONE; Ingresos confirma o rechaza
/// (interino A2: endpoint manual con permiso <c>aplicacion-pago.confirmar</c>
/// hasta que exista el emisor real de <c>PagoClienteConfirmadoEvent</c> —
/// PLATFORM-TODO(&lt;PagoClienteConfirmado&gt;)).
///
/// <para>
/// Invariantes (§4.2): <c>Σ importe_aplicado + ajuste_no_fiscal =
/// monto_deposito</c>; el ajuste solo puede ser negativo (depósito corto)
/// y menor a la tolerancia no fiscal (parámetro, default $50 USD — gate
/// fiscal pendiente); sin <c>remittance_ref</c> no se puede proponer
/// (regla 2.1). La confirmación NO aplica pagos a cartera — eso lo hace
/// el REPP timbrado vía eventos de Facturación.
/// </para>
/// </summary>
public sealed class PropuestaAplicacionPago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; private set; }
    public string DepositoRef { get; private set; } = default!;
    public decimal MontoDeposito { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string RemittanceRef { get; private set; } = default!;

    /// <summary>Diferencia depósito − Σ importes; solo ≤ 0 y dentro de tolerancia.</summary>
    public decimal AjusteNoFiscal { get; private set; }

    public EstadoPropuestaAplicacion Estado { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public Guid? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    private readonly List<PropuestaAplicacionFactura> _facturas = [];
    public IReadOnlyCollection<PropuestaAplicacionFactura> Facturas => _facturas.AsReadOnly();

    private PropuestaAplicacionPago() { }

    public static PropuestaAplicacionPago Crear(
        Guid empresaId,
        Guid clienteId,
        string depositoRef,
        decimal montoDeposito,
        string moneda,
        string remittanceRef,
        IReadOnlyList<(string FacturaUuid, Guid FacturaCarteraId, decimal ImporteAplicado, int? NumParcialidad)> lineas,
        decimal toleranciaNoFiscal)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("PAP_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(depositoRef))
            throw new BusinessRuleException("PAP_DEPOSITO_VACIO", "La referencia del depósito es obligatoria.");
        if (montoDeposito <= 0)
            throw new BusinessRuleException("PAP_MONTO_INVALIDO", "El monto del depósito debe ser > 0.");
        // Regla 2.1 del levantamiento: sin remittance no hay propuesta.
        if (string.IsNullOrWhiteSpace(remittanceRef))
            throw new BusinessRuleException("PAP_SIN_REMITTANCE",
                "Sin remittance del cliente no se puede proponer la aplicación.");
        if (lineas.Count == 0)
            throw new BusinessRuleException("PAP_SIN_FACTURAS", "La propuesta debe aplicar al menos una factura.");
        if (lineas.Any(l => l.ImporteAplicado <= 0))
            throw new BusinessRuleException("PAP_IMPORTE_INVALIDO", "Cada importe aplicado debe ser > 0.");
        if (lineas.GroupBy(l => l.FacturaUuid).Any(g => g.Count() > 1))
            throw new BusinessRuleException("PAP_FACTURA_DUPLICADA", "Una factura no puede repetirse en la propuesta.");

        var suma = lineas.Sum(l => l.ImporteAplicado);
        var ajuste = montoDeposito - suma;

        if (ajuste > 0)
            throw new BusinessRuleException("PAP_DEPOSITO_EXCEDENTE",
                $"El depósito ({montoDeposito}) excede la suma aplicada ({suma}) — un excedente no es ajuste no fiscal; captúrelo como anticipo o corrija el desglose.");
        if (ajuste < 0 && Math.Abs(ajuste) >= toleranciaNoFiscal)
            throw new BusinessRuleException("PAP_TOLERANCIA_EXCEDIDA",
                $"La diferencia ({ajuste}) excede la tolerancia no fiscal ({toleranciaNoFiscal} {moneda}).");

        var propuesta = new PropuestaAplicacionPago
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            DepositoRef = depositoRef.Trim(),
            MontoDeposito = montoDeposito,
            Moneda = moneda,
            RemittanceRef = remittanceRef.Trim(),
            AjusteNoFiscal = ajuste,
            Estado = EstadoPropuestaAplicacion.Propuesta,
        };

        foreach (var l in lineas)
        {
            propuesta._facturas.Add(new PropuestaAplicacionFactura(
                Guid.CreateVersion7(), empresaId, propuesta.Id,
                l.FacturaCarteraId, l.FacturaUuid, l.ImporteAplicado, l.NumParcialidad));
        }

        return propuesta;
    }

    /// <summary>Confirmación interina de Ingresos (A2) — solo cambia estado.</summary>
    public void Confirmar(Guid usuarioId, DateTimeOffset ahora)
    {
        AsegurarPropuesta();
        Estado = EstadoPropuestaAplicacion.Confirmada;
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        AsegurarPropuesta();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("PAP_MOTIVO_VACIO", "El motivo de rechazo es obligatorio.");

        Estado = EstadoPropuestaAplicacion.Rechazada;
        MotivoRechazo = motivo.Trim();
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    private void AsegurarPropuesta()
    {
        if (Estado != EstadoPropuestaAplicacion.Propuesta)
            throw new BusinessRuleException("PAP_YA_RESUELTA",
                $"Solo una propuesta pendiente puede resolverse (estado actual: {Estado}).");
    }
}

/// <summary>Detalle por factura de la propuesta (unión §3.1).</summary>
public sealed class PropuestaAplicacionFactura : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid PropuestaId { get; private set; }

    /// <summary>Factura en la proyección de cartera (correlación local).</summary>
    public Guid FacturaCarteraId { get; private set; }

    /// <summary>UUID fiscal — la referencia que viaja en el remittance.</summary>
    public string FacturaUuid { get; private set; } = default!;

    public decimal ImporteAplicado { get; private set; }
    public int? NumParcialidad { get; private set; }

    private PropuestaAplicacionFactura() { }

    internal PropuestaAplicacionFactura(
        Guid id, Guid empresaId, Guid propuestaId, Guid facturaCarteraId,
        string facturaUuid, decimal importeAplicado, int? numParcialidad) : base(id)
    {
        EmpresaId = empresaId;
        PropuestaId = propuestaId;
        FacturaCarteraId = facturaCarteraId;
        FacturaUuid = facturaUuid;
        ImporteAplicado = importeAplicado;
        NumParcialidad = numParcialidad;
    }
}
