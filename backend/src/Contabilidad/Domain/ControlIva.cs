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
public enum TipoMovimientoIva : short
{
    Pago = 0,
    Nota = 1,
    /// <summary>M-209 (CA4): corrección del IVA del documento contra su XML; mueve a pagado la parte del ajuste ya pagada.</summary>
    Ajuste = 2,
}

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
    /// <summary>
    /// 86e3zcbjw: retenciones por concepto (clasificador <c>{CONCEPTO}_{IMPUESTO}</c>, ISR e IVA) y cuánto ya movió cada pago o cobro.
    /// </summary>
    public List<RetencionSaldoIva> Retenciones { get; private set; } = [];
    /// <summary>
    /// 86e3zcbjw (Contabilidad, 6-nov): la factura de proveedor se registró por el total sin la retención; cada pago abona la retención
    /// que le toca a «retenciones por pagar» y carga al proveedor el pago más esa retención. False en documentos anteriores (su póliza ya
    /// registró la retención) y en ventas (la retención del cliente va con la factura).
    /// </summary>
    public bool RetencionAlPago { get; private set; }

    /// <summary>
    /// M-209: base e IVA del documento por tratamiento (TASA16, TASA8, TASA0, EXENTO, NOOBJETO, NOACREDITABLE), y cuánto ya movió cada pago o
    /// cobro. Las bases sin IVA (exento, 0 %, no objeto) también quedan, para la DIOT. Documentos anteriores: todo como TASA16.
    /// </summary>
    public List<TratamientoSaldoIva> Tratamientos { get; private set; } = [];
    public Guid PolizaId { get; private set; }
    public EstadoSaldoIva Estado { get; private set; }
    public int Version { get; private set; } = 1;

    private SaldoIvaDocumento() { }

    public static SaldoIvaDocumento Crear(Guid empresaId, LadoIva lado, (string Modulo, string Tipo) origen, Guid documentoId, string? folio,
        string? uuid, string? metodoPago, Guid? sucursalId, Guid? terceroId, DateOnly fecha, string moneda, decimal? tipoCambio, decimal @base,
        decimal iva, decimal ivaRetenido, Guid polizaId, IEnumerable<(string Clasificador, decimal Importe)>? retenciones = null,
        bool retencionAlPago = false, IEnumerable<(string Tratamiento, decimal Base, decimal Iva)>? tratamientos = null) => new()
    {
        Tratamientos = [.. (tratamientos ?? []).Where(t => t.Base != 0m || t.Iva != 0m).GroupBy(t => t.Tratamiento)
            .Select(g => new TratamientoSaldoIva { Tratamiento = g.Key, Base = g.Sum(t => t.Base), Iva = g.Sum(t => t.Iva) })],
        Retenciones = [.. (retenciones ?? []).Where(r => r.Importe > 0m).GroupBy(r => r.Clasificador)
            .Select(g => new RetencionSaldoIva { Clasificador = g.Key, Importe = g.Sum(r => r.Importe) })],
        RetencionAlPago = retencionAlPago,
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

    /// <summary>
    /// 86e3zcbjw: retención por concepto que le toca a un pago o cobro de <paramref name="importe"/>, con la misma regla que
    /// <see cref="Calcular"/> (proporcional a la base; el pago que salda toma el remanente).
    /// </summary>
    public IReadOnlyList<RetencionAplicadaIva> CalcularRetenciones(decimal importe)
    {
        if (Estado != EstadoSaldoIva.Vigente || Base <= 0m || importe <= 0m) return [];
        var salda = Aplicado + importe >= Base - Tolerancia;
        return [.. Retenciones.Select(r => new RetencionAplicadaIva
            {
                Clasificador = r.Clasificador,
                Importe = salda ? r.Pendiente : Math.Min(r.Pendiente, Math.Round(r.Importe * importe / Base, 2, MidpointRounding.AwayFromZero)),
            })
            .Where(r => r.Importe != 0m)];
    }

    /// <summary>
    /// M-209: cómo se reparte por tratamiento un pago o cobro de <paramref name="importe"/> que reclasifica <paramref name="iva"/> (lo que
    /// dio <see cref="Calcular"/>). El IVA va en proporción a lo pendiente de cada tratamiento (el último toma el resto, para que sume
    /// exacto) y la base, en proporción a lo aplicado (el pago que salda toma el remanente).
    /// </summary>
    public IReadOnlyList<TratamientoAplicadoIva> CalcularTratamientos(decimal importe, decimal iva)
    {
        if (Estado != EstadoSaldoIva.Vigente || Base <= 0m || importe <= 0m || Tratamientos.Count == 0) return [];
        var salda = Aplicado + importe >= Base - Tolerancia;
        var pendiente = Tratamientos.Sum(t => t.Pendiente);
        var conIva = Tratamientos.Where(t => t.Pendiente > 0m).ToList();
        var resultado = new List<TratamientoAplicadoIva>();
        var resto = iva;
        foreach (var t in Tratamientos)
        {
            decimal ivaT = 0m;
            if (t.Pendiente > 0m && pendiente > 0m)
            {
                ivaT = ReferenceEquals(t, conIva[^1]) ? resto : Math.Round(iva * t.Pendiente / pendiente, 2, MidpointRounding.AwayFromZero);
                resto -= ivaT;
            }
            var baseT = salda ? t.Base - t.BaseAplicada
                : Math.Min(t.Base - t.BaseAplicada, Math.Round(t.Base * importe / Base, 2, MidpointRounding.AwayFromZero));
            if (ivaT != 0m || baseT != 0m) resultado.Add(new TratamientoAplicadoIva { Tratamiento = t.Tratamiento, Base = baseT, Iva = ivaT });
        }
        return resultado;
    }

    /// <summary>Suma (o resta) por tratamiento lo que movió un pago o cobro; devuelve lo movido con sus importes en MXN.</summary>
    private List<TratamientoAplicadoIva> MoverTratamientos(IEnumerable<TratamientoAplicadoIva> aplicados, int signo, decimal tipoCambio)
    {
        var movidos = new List<TratamientoAplicadoIva>();
        foreach (var a in aplicados)
        {
            var t = Tratamientos.FirstOrDefault(x => x.Tratamiento == a.Tratamiento);
            if (t is null) continue;
            t.BaseAplicada += signo * a.Base;
            t.IvaReclasificado += signo * a.Iva;
            movidos.Add(new TratamientoAplicadoIva
            {
                Tratamiento = a.Tratamiento, Base = a.Base, Iva = a.Iva,
                BaseMxn = Math.Round(a.Base * tipoCambio, 2, MidpointRounding.AwayFromZero),
                IvaMxn = Math.Round(a.Iva * tipoCambio, 2, MidpointRounding.AwayFromZero),
            });
        }
        // Lista nueva para que EF detecte el cambio de la columna JSON.
        Tratamientos = [.. Tratamientos.Select(x => x.Copia())];
        return movidos;
    }

    /// <summary>Registra un pago o cobro con su reclasificación (la que se calculó para su póliza).</summary>
    public ReclasificacionIva Aplicar((string Modulo, string Tipo) pago, Guid pagoId, DateOnly fecha, decimal importe, decimal iva,
        decimal ivaRetenido, Guid polizaId, decimal tipoCambio = 1m, IReadOnlyList<RetencionAplicadaIva>? retenciones = null,
        IReadOnlyList<TratamientoAplicadoIva>? tratamientos = null)
    {
        var porTratamiento = MoverTratamientos(tratamientos ?? CalcularTratamientos(importe, iva), 1, tipoCambio);
        if (importe <= 0m)
            throw new BusinessRuleException("CONTAB_IVA_IMPORTE", "El importe aplicado al documento debe ser mayor a cero.");
        Aplicado += importe;
        IvaReclasificado += iva;
        IvaRetenidoReclasificado += ivaRetenido;
        var movidas = Mover(retenciones ?? [], 1, tipoCambio);
        Version++;
        return new ReclasificacionIva(Guid.CreateVersion7(), EmpresaId, Id, TipoMovimientoIva.Pago, pago, pagoId, fecha, importe, iva,
            ivaRetenido, polizaId, tipoCambio, movidas, porTratamiento);
    }

    /// <summary>Suma (o resta, con <paramref name="signo"/> −1) lo aplicado por concepto; devuelve lo movido con su importe en MXN.</summary>
    private List<RetencionAplicadaIva> Mover(IEnumerable<RetencionAplicadaIva> retenciones, int signo, decimal tipoCambio)
    {
        var movidas = new List<RetencionAplicadaIva>();
        foreach (var r in retenciones)
        {
            var saldo = Retenciones.FirstOrDefault(x => x.Clasificador == r.Clasificador);
            if (saldo is null || r.Importe == 0m) continue;
            saldo.Aplicado += signo * r.Importe;
            movidas.Add(new RetencionAplicadaIva
            {
                Clasificador = r.Clasificador, Importe = r.Importe,
                ImporteMxn = Math.Round(r.Importe * tipoCambio, 2, MidpointRounding.AwayFromZero),
            });
        }
        // Lista nueva para que EF detecte el cambio de la columna JSON.
        Retenciones = [.. Retenciones.Select(x => new RetencionSaldoIva { Clasificador = x.Clasificador, Importe = x.Importe, Aplicado = x.Aplicado })];
        return movidas;
    }

    /// <summary>C1.6-b: un pago revertido o un cobro cancelado devuelve lo aplicado y el IVA a pendiente.</summary>
    public void Revertir(ReclasificacionIva r, DateTimeOffset ahora)
    {
        if (r.SaldoIvaDocumentoId != Id || r.AnuladaEn is not null || r.Tipo != TipoMovimientoIva.Pago) return;
        Aplicado -= r.Importe;
        IvaReclasificado -= r.Iva;
        IvaRetenidoReclasificado -= r.IvaRetenido;
        Mover(r.Retenciones, -1, 1m);
        MoverTratamientos(r.Tratamientos, -1, 1m);
        r.Anular(ahora);
        Version++;
    }

    /// <summary>
    /// Una nota de crédito o de cargo reduce lo que queda por pagar y su IVA: su póliza ya revierte ese IVA pendiente, así que no
    /// reclasifica. Queda como movimiento <see cref="TipoMovimientoIva.Nota"/> (idempotencia y rastro); K10.7 no lo suma.
    /// </summary>
    public ReclasificacionIva Reducir((string Modulo, string Tipo) nota, Guid notaId, DateOnly fecha, decimal @base, decimal iva,
        decimal ivaRetenido, Guid polizaId, IEnumerable<(string Impuesto, decimal Importe)>? retenciones = null)
    {
        var b = Math.Min(Math.Max(0m, @base), Base);
        var i = Math.Min(Math.Max(0m, iva), Iva - IvaReclasificado);
        var r = Math.Min(Math.Max(0m, ivaRetenido), IvaRetenido - IvaRetenidoReclasificado);
        var reducidosTratamiento = ReducirTratamientos(b, i, Base);
        Base -= b; Iva -= i; IvaRetenido -= r;
        // 86e3zcbjw: la retención de la nota reduce la pendiente del mismo impuesto (concepto por concepto, sin pasar de lo pendiente).
        var reducidas = new List<RetencionAplicadaIva>();
        foreach (var (impuesto, importe) in retenciones ?? [])
        {
            var resto = importe;
            foreach (var x in Retenciones.Where(x => RetencionSaldoIva.ImpuestoDe(x.Clasificador) == impuesto && x.Pendiente > 0m))
            {
                if (resto <= 0m) break;
                var parte = Math.Min(resto, x.Pendiente);
                x.Importe -= parte;
                resto -= parte;
                reducidas.Add(new RetencionAplicadaIva { Clasificador = x.Clasificador, Importe = parte, ImporteMxn = parte });
            }
        }
        Retenciones = [.. Retenciones.Select(x => new RetencionSaldoIva { Clasificador = x.Clasificador, Importe = x.Importe, Aplicado = x.Aplicado })];
        Version++;
        return new ReclasificacionIva(Guid.CreateVersion7(), EmpresaId, Id, TipoMovimientoIva.Nota, nota, notaId, fecha, b, i, r, polizaId, 1m,
            reducidas, reducidosTratamiento);
    }

    /// <summary>
    /// M-209: una nota reduce cada tratamiento en proporción: el IVA, a lo pendiente de cada uno (el último toma el resto); la base, a la
    /// proporción de la base del documento que reduce la nota.
    /// </summary>
    private List<TratamientoAplicadoIva> ReducirTratamientos(decimal @base, decimal iva, decimal baseDocumento)
    {
        if (Tratamientos.Count == 0 || (@base <= 0m && iva <= 0m)) return [];
        var pendiente = Tratamientos.Sum(t => t.Pendiente);
        var conIva = Tratamientos.Where(t => t.Pendiente > 0m).ToList();
        var resto = iva;
        var reducidos = new List<TratamientoAplicadoIva>();
        foreach (var t in Tratamientos)
        {
            decimal ivaT = 0m;
            if (iva > 0m && t.Pendiente > 0m && pendiente > 0m)
            {
                ivaT = ReferenceEquals(t, conIva[^1]) ? resto : Math.Round(iva * t.Pendiente / pendiente, 2, MidpointRounding.AwayFromZero);
                resto -= ivaT;
            }
            var baseT = baseDocumento > 0m ? Math.Min(t.Base - t.BaseAplicada, Math.Round(t.Base * @base / baseDocumento, 2, MidpointRounding.AwayFromZero)) : 0m;
            t.Iva -= ivaT;
            t.Base -= baseT;
            if (ivaT != 0m || baseT != 0m)
                reducidos.Add(new TratamientoAplicadoIva { Tratamiento = t.Tratamiento, Base = baseT, Iva = ivaT, BaseMxn = baseT, IvaMxn = ivaT });
        }
        Tratamientos = [.. Tratamientos.Select(x => x.Copia())];
        return reducidos;
    }

    public void Cancelar()
    {
        Estado = EstadoSaldoIva.Cancelado;
        Version++;
    }

    /// <summary>
    /// M-209 (CA4): diferencias por tratamiento entre el XML y lo contabilizado (lo vigente más lo que redujeron las notas). La parte
    /// del IVA de la diferencia que corresponde a lo ya pagado (aplicado ÷ base) va directo a pagado; el resto queda pendiente.
    /// Solo tratamientos con diferencia de un centavo o más en base o IVA.
    /// </summary>
    public IReadOnlyList<DiferenciaIvaDocumento> Diferencias(IEnumerable<(string Tratamiento, decimal Base, decimal Iva)> xml,
        IReadOnlyDictionary<string, (decimal Base, decimal Iva)> reducidoPorNotas)
    {
        var delXml = xml.GroupBy(x => x.Tratamiento).ToDictionary(g => g.Key, g => (Base: g.Sum(x => x.Base), Iva: g.Sum(x => x.Iva)));
        var pagado = Base > 0m ? Math.Min(1m, Aplicado / Base) : 0m;
        var resultado = new List<DiferenciaIvaDocumento>();
        foreach (var tratamiento in delXml.Keys.Union(Tratamientos.Select(t => t.Tratamiento)).Order(StringComparer.Ordinal))
        {
            var t = Tratamientos.FirstOrDefault(x => x.Tratamiento == tratamiento);
            var notas = reducidoPorNotas.GetValueOrDefault(tratamiento);
            var contabilizado = (Base: (t?.Base ?? 0m) + notas.Base, Iva: (t?.Iva ?? 0m) + notas.Iva);
            var documento = delXml.GetValueOrDefault(tratamiento);
            var (baseD, ivaD) = (documento.Base - contabilizado.Base, documento.Iva - contabilizado.Iva);
            if (Math.Abs(baseD) < Tolerancia && Math.Abs(ivaD) < Tolerancia) continue;
            resultado.Add(new DiferenciaIvaDocumento(tratamiento, contabilizado.Base, contabilizado.Iva, documento.Base, documento.Iva,
                baseD, ivaD, Math.Round(ivaD * pagado, 2, MidpointRounding.AwayFromZero),
                Math.Round(baseD * pagado, 2, MidpointRounding.AwayFromZero)));
        }
        return resultado;
    }

    /// <summary>
    /// M-209 (CA4): aplica las diferencias al saldo (base e IVA por tratamiento; la parte ya pagada cuenta como reclasificada) y devuelve
    /// el movimiento de ajuste, ligado a su póliza, con la parte que pasó a pagado (la liquidación del mes la suma).
    /// </summary>
    public ReclasificacionIva Corregir(IReadOnlyList<DiferenciaIvaDocumento> diferencias, Guid ajusteId, DateOnly fecha, Guid polizaId,
        decimal tipoCambio)
    {
        if (Estado != EstadoSaldoIva.Vigente)
            throw new BusinessRuleException("CONTAB_IVA_DOCUMENTO_CANCELADO", "El documento está cancelado: su IVA no se corrige.");
        foreach (var d in diferencias)
        {
            var t = Tratamientos.FirstOrDefault(x => x.Tratamiento == d.Tratamiento);
            if (t is null) Tratamientos.Add(t = new TratamientoSaldoIva { Tratamiento = d.Tratamiento });
            t.Base += d.BaseDiferencia;
            t.Iva += d.IvaDiferencia;
            t.BaseAplicada += d.BasePagada;
            t.IvaReclasificado += d.IvaPagado;
        }
        Tratamientos = [.. Tratamientos.Select(x => x.Copia())];
        var ivaPagado = diferencias.Sum(d => d.IvaPagado);
        Iva += diferencias.Sum(d => d.IvaDiferencia);
        IvaReclasificado += ivaPagado;
        Version++;
        return new ReclasificacionIva(Guid.CreateVersion7(), EmpresaId, Id, TipoMovimientoIva.Ajuste, ("contabilidad", "ajuste_iva"), ajusteId,
            fecha, 0m, ivaPagado, 0m, polizaId, tipoCambio, null,
            [.. diferencias.Select(d => new TratamientoAplicadoIva
            {
                Tratamiento = d.Tratamiento, Base = d.BasePagada, Iva = d.IvaPagado,
                BaseMxn = Math.Round(d.BasePagada * tipoCambio, 2, MidpointRounding.AwayFromZero),
                IvaMxn = Math.Round(d.IvaPagado * tipoCambio, 2, MidpointRounding.AwayFromZero),
            })]);
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
    /// <summary>86e3zcbjw: retención por concepto que movió el pago o cobro (o que redujo la nota).</summary>
    public List<RetencionAplicadaIva> Retenciones { get; private set; } = [];
    /// <summary>M-209: base e IVA por tratamiento que movió el pago o cobro (o que redujo la nota); la liquidación los suma por mes.</summary>
    public List<TratamientoAplicadoIva> Tratamientos { get; private set; } = [];
    public Guid PolizaId { get; private set; }
    public DateTimeOffset? AnuladaEn { get; private set; }

    private ReclasificacionIva() { }

    internal ReclasificacionIva(Guid id, Guid empresaId, Guid saldoId, TipoMovimientoIva tipo, (string Modulo, string Tipo) pago, Guid pagoId,
        DateOnly fecha, decimal importe, decimal iva, decimal ivaRetenido, Guid polizaId, decimal tipoCambio,
        List<RetencionAplicadaIva>? retenciones = null, List<TratamientoAplicadoIva>? tratamientos = null)
    {
        Retenciones = retenciones ?? [];
        Tratamientos = tratamientos ?? [];
        Id = id; EmpresaId = empresaId; SaldoIvaDocumentoId = saldoId; Tipo = tipo; PagoModulo = pago.Modulo; PagoTipo = pago.Tipo; PagoId = pagoId;
        Fecha = fecha; Importe = importe; Iva = iva; IvaRetenido = ivaRetenido; PolizaId = polizaId;
        IvaMxn = Math.Round(iva * tipoCambio, 2, MidpointRounding.AwayFromZero);
        IvaRetenidoMxn = Math.Round(ivaRetenido * tipoCambio, 2, MidpointRounding.AwayFromZero);
    }

    internal void Anular(DateTimeOffset ahora) => AnuladaEn = ahora;
}

/// <summary>86e3zcbjw · retención de un documento por concepto (JSON en el saldo).</summary>
public sealed class RetencionSaldoIva
{
    /// <summary><c>{CONCEPTO}_{IMPUESTO}</c> (p. ej. FLETES_IVA, HONORARIOS_PF_ISR) o solo el impuesto si el documento no trae concepto.</summary>
    public string Clasificador { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal Aplicado { get; set; }

    [JsonIgnore]
    public decimal Pendiente => Importe - Aplicado;

    /// <summary>Impuesto del clasificador: el último segmento (ISR, IVA o IEPS).</summary>
    public static string ImpuestoDe(string clasificador)
    {
        var i = clasificador.LastIndexOf('_');
        return i < 0 ? clasificador : clasificador[(i + 1)..];
    }
}

/// <summary>86e3zcbjw · retención que movió un pago, cobro o nota (JSON en la reclasificación).</summary>
public sealed class RetencionAplicadaIva
{
    public string Clasificador { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public decimal ImporteMxn { get; set; }
}

/// <summary>M-209 · base e IVA de un documento por tratamiento (JSON en el saldo).</summary>
public sealed class TratamientoSaldoIva
{
    /// <summary>TASA16, TASA8, TASA0, EXENTO, NOOBJETO o NOACREDITABLE (<c>ImpuestosTrasladados</c>).</summary>
    public string Tratamiento { get; set; } = string.Empty;
    public decimal Base { get; set; }
    public decimal Iva { get; set; }
    public decimal BaseAplicada { get; set; }
    public decimal IvaReclasificado { get; set; }

    [JsonIgnore]
    public decimal Pendiente => Iva - IvaReclasificado;

    internal TratamientoSaldoIva Copia() => new()
    {
        Tratamiento = Tratamiento, Base = Base, Iva = Iva, BaseAplicada = BaseAplicada, IvaReclasificado = IvaReclasificado,
    };
}

/// <summary>M-209 · base e IVA por tratamiento que movió un pago, cobro o nota (JSON en la reclasificación).</summary>
public sealed class TratamientoAplicadoIva
{
    public string Tratamiento { get; set; } = string.Empty;
    public decimal Base { get; set; }
    public decimal Iva { get; set; }
    public decimal BaseMxn { get; set; }
    public decimal IvaMxn { get; set; }
}

/// <summary>
/// M-209 (CA4) · diferencia de un tratamiento entre lo contabilizado y el XML del documento. <c>IvaPagado</c>/<c>BasePagada</c> = la parte
/// de la diferencia que corresponde a lo ya pagado del documento (va directo a pagado).
/// </summary>
public sealed record DiferenciaIvaDocumento(string Tratamiento, decimal BaseContabilizada, decimal IvaContabilizado, decimal BaseXml,
    decimal IvaXml, decimal BaseDiferencia, decimal IvaDiferencia, decimal IvaPagado, decimal BasePagada);
