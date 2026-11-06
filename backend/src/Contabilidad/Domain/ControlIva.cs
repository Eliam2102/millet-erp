using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>Lado fiscal del IVA de un documento. Fijo por ABI: agregar al final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LadoIva : short
{
    /// <summary>Factura de proveedor: IVA acreditable (pendiente → pagado).</summary>
    Acreditable = 0,
    /// <summary>Factura de venta o de anticipo: IVA trasladado (no cobrado → cobrado).</summary>
    Trasladado = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoSaldoIva : short { Vigente = 0, Cancelado = 1 }

/// <summary>Qué movió el saldo: un pago o cobro (reclasifica IVA) o una nota de crédito o de cargo (reduce el documento). Agregar al final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoMovimientoIva : short { Pago = 0, Nota = 1 }

/// <summary>
/// C1.6 · saldo del IVA de un documento (factura de proveedor #2, factura de venta #7 o de anticipo #8). Nace con la póliza del documento y
/// guarda lo que el motor necesita para reclasificar el IVA en proporción a lo pagado o cobrado (flujo de efectivo, LIVA):
/// <c>IVA reclasificado = IVA × aplicado ÷ base</c>, con base = total menos retenciones (lo que se paga). El pago que salda el documento
/// toma el remanente, para que no queden centavos en la cuenta de pendiente. Importes en la moneda del documento.
/// </summary>
public sealed class SaldoIvaDocumento : IPerteneceAEmpresa
{
    /// <summary>Lo aplicado se considera saldado a menos de un centavo de la base.</summary>
    public const decimal Tolerancia = 0.01m;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public LadoIva Lado { get; private set; }
    public string OrigenModulo { get; private set; } = string.Empty;
    public string OrigenTipo { get; private set; } = string.Empty;
    public Guid DocumentoId { get; private set; }
    public string? Folio { get; private set; }
    public string? Uuid { get; private set; }
    public string? MetodoPago { get; private set; }
    public Guid? SucursalId { get; private set; }
    /// <summary>Proveedor (acreditable) o cliente (trasladado).</summary>
    public Guid? TerceroId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }
    /// <summary>Lo que se paga o cobra: total menos retenciones.</summary>
    public decimal Base { get; private set; }
    public decimal Iva { get; private set; }
    /// <summary>IVA retenido (impuesto 002): a proveedores (por enterar) o por clientes (a favor).</summary>
    public decimal IvaRetenido { get; private set; }
    public decimal Aplicado { get; private set; }
    public decimal IvaReclasificado { get; private set; }
    public decimal IvaRetenidoReclasificado { get; private set; }
    public Guid PolizaId { get; private set; }
    public EstadoSaldoIva Estado { get; private set; }
    public int Version { get; private set; } = 1;

    private SaldoIvaDocumento() { }

    public static SaldoIvaDocumento Crear(Guid empresaId, LadoIva lado, (string Modulo, string Tipo) origen, Guid documentoId, string? folio,
        string? uuid, string? metodoPago, Guid? sucursalId, Guid? terceroId, DateOnly fecha, string moneda, decimal? tipoCambio, decimal @base,
        decimal iva, decimal ivaRetenido, Guid polizaId) => new()
    {
        Id = Guid.CreateVersion7(), EmpresaId = empresaId, Lado = lado, OrigenModulo = origen.Modulo, OrigenTipo = origen.Tipo,
        DocumentoId = documentoId, Folio = folio, Uuid = uuid, MetodoPago = string.IsNullOrWhiteSpace(metodoPago) ? null : metodoPago.Trim(),
        SucursalId = sucursalId, TerceroId = terceroId, Fecha = fecha, Moneda = moneda, TipoCambio = tipoCambio, Base = @base, Iva = iva,
        IvaRetenido = ivaRetenido, PolizaId = polizaId, Estado = EstadoSaldoIva.Vigente,
    };

    public decimal Saldo => Base - Aplicado;
    public decimal IvaPendiente => Iva - IvaReclasificado;
    public bool Saldado => Base > 0m && Saldo <= Tolerancia;

    /// <summary>
    /// Cuánto IVA (y IVA retenido) le toca a un pago o cobro de <paramref name="importe"/>, sin aplicarlo. Proporcional a la base; si el
    /// pago salda el documento (o lo excede), el remanente. Un documento sin base o cancelado no reclasifica.
    /// </summary>
    public (decimal Iva, decimal IvaRetenido) Calcular(decimal importe)
    {
        if (Estado != EstadoSaldoIva.Vigente || Base <= 0m || importe <= 0m) return (0m, 0m);
        if (Aplicado + importe >= Base - Tolerancia) return (IvaPendiente, IvaRetenido - IvaRetenidoReclasificado);
        var iva = Math.Round(Iva * importe / Base, 2, MidpointRounding.AwayFromZero);
        var retenido = Math.Round(IvaRetenido * importe / Base, 2, MidpointRounding.AwayFromZero);
        return (Math.Min(iva, IvaPendiente), Math.Min(retenido, IvaRetenido - IvaRetenidoReclasificado));
    }

    /// <summary>Registra un pago o cobro con su reclasificación (la que se calculó para su póliza).</summary>
    public ReclasificacionIva Aplicar((string Modulo, string Tipo) pago, Guid pagoId, DateOnly fecha, decimal importe, decimal iva,
        decimal ivaRetenido, Guid polizaId, decimal tipoCambio = 1m)
    {
        if (importe <= 0m)
            throw new BusinessRuleException("CONTAB_IVA_IMPORTE", "El importe aplicado al documento debe ser mayor a cero.");
        Aplicado += importe;
        IvaReclasificado += iva;
        IvaRetenidoReclasificado += ivaRetenido;
        Version++;
        return new ReclasificacionIva(Guid.CreateVersion7(), EmpresaId, Id, TipoMovimientoIva.Pago, pago, pagoId, fecha, importe, iva,
            ivaRetenido, polizaId, tipoCambio);
    }

    /// <summary>C1.6-b: un pago revertido o un cobro cancelado devuelve lo aplicado y el IVA a pendiente.</summary>
    public void Revertir(ReclasificacionIva r, DateTimeOffset ahora)
    {
        if (r.SaldoIvaDocumentoId != Id || r.AnuladaEn is not null || r.Tipo != TipoMovimientoIva.Pago) return;
        Aplicado -= r.Importe;
        IvaReclasificado -= r.Iva;
        IvaRetenidoReclasificado -= r.IvaRetenido;
        r.Anular(ahora);
        Version++;
    }

    /// <summary>
    /// Una nota de crédito o de cargo reduce lo que queda por pagar y su IVA: su póliza ya revierte ese IVA pendiente, así que no
    /// reclasifica. Queda como movimiento <see cref="TipoMovimientoIva.Nota"/> (idempotencia y rastro); K10.7 no lo suma.
    /// </summary>
    public ReclasificacionIva Reducir((string Modulo, string Tipo) nota, Guid notaId, DateOnly fecha, decimal @base, decimal iva,
        decimal ivaRetenido, Guid polizaId)
    {
        var b = Math.Min(Math.Max(0m, @base), Base);
        var i = Math.Min(Math.Max(0m, iva), Iva - IvaReclasificado);
        var r = Math.Min(Math.Max(0m, ivaRetenido), IvaRetenido - IvaRetenidoReclasificado);
        Base -= b; Iva -= i; IvaRetenido -= r;
        Version++;
        return new ReclasificacionIva(Guid.CreateVersion7(), EmpresaId, Id, TipoMovimientoIva.Nota, nota, notaId, fecha, b, i, r, polizaId, 1m);
    }

    public void Cancelar()
    {
        Estado = EstadoSaldoIva.Cancelado;
        Version++;
    }
}

/// <summary>
/// C1.6 · un pago o cobro aplicado a un documento con IVA: cuánto se aplicó y cuánto IVA pasó a pagado o cobrado en su póliza. K10.7 suma
/// estas filas por mes para la liquidación de IVA. Una reversa no se borra: queda con <see cref="AnuladaEn"/>.
/// </summary>
public sealed class ReclasificacionIva : IPerteneceAEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid SaldoIvaDocumentoId { get; private set; }
    public TipoMovimientoIva Tipo { get; private set; }
    public string PagoModulo { get; private set; } = string.Empty;
    public string PagoTipo { get; private set; } = string.Empty;
    public Guid PagoId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public decimal Importe { get; private set; }
    public decimal Iva { get; private set; }
    public decimal IvaRetenido { get; private set; }
    /// <summary>IVA en MXN como quedó en la póliza (importe × tipo de cambio del pago): K10.7 lo cuadra contra las cuentas.</summary>
    public decimal IvaMxn { get; private set; }
    public decimal IvaRetenidoMxn { get; private set; }
    public Guid PolizaId { get; private set; }
    public DateTimeOffset? AnuladaEn { get; private set; }

    private ReclasificacionIva() { }

    internal ReclasificacionIva(Guid id, Guid empresaId, Guid saldoId, TipoMovimientoIva tipo, (string Modulo, string Tipo) pago, Guid pagoId,
        DateOnly fecha, decimal importe, decimal iva, decimal ivaRetenido, Guid polizaId, decimal tipoCambio)
    {
        Id = id; EmpresaId = empresaId; SaldoIvaDocumentoId = saldoId; Tipo = tipo; PagoModulo = pago.Modulo; PagoTipo = pago.Tipo; PagoId = pagoId;
        Fecha = fecha; Importe = importe; Iva = iva; IvaRetenido = ivaRetenido; PolizaId = polizaId;
        IvaMxn = Math.Round(iva * tipoCambio, 2, MidpointRounding.AwayFromZero);
        IvaRetenidoMxn = Math.Round(ivaRetenido * tipoCambio, 2, MidpointRounding.AwayFromZero);
    }

    internal void Anular(DateTimeOffset ahora) => AnuladaEn = ahora;
}
