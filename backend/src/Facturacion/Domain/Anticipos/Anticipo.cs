using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// Saldo amortizable de un anticipo de cliente (§3.bis.3, §6 levantamiento).
/// Agregado <b>separado</b> del CFDI inmutable (<see cref="FacturaAnticipo"/>):
/// el comprobante documenta el cobro; este agregado rastrea cuánto queda por
/// amortizar a lo largo del tiempo (vinculaciones M2 + NCs de amortización M3).
///
/// <para>
/// <b>Tres montos, no confundir (§6.4):</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="MontoCobrado"/> — lo efectivamente cobrado (PUE = total al
///   emitir; PPD = suma de REPP, F6). Es el techo amortizable.</item>
///   <item><see cref="MontoAmortizado"/> — reducido por NCs de amortización
///   timbradas (M3, F4-PR2). En F4-PR1 siempre 0.</item>
///   <item><see cref="Saldo"/> = <c>MontoCobrado − MontoAmortizado</c>: lo que
///   muestra el Control de Anticipos.</item>
/// </list>
///
/// <para>
/// <see cref="SaldoDisponible"/> (calculado, no persistido) descuenta además las
/// vinculaciones comprometidas sin NC: es lo que <see cref="Vincular"/> puede
/// aún comprometer sin sobre-amortizar (invariante 2 / D6: no amortizar más que
/// lo cobrado).
/// </para>
/// </summary>
public sealed class Anticipo : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Cliente dueño del anticipo (master DatosMaestros).</summary>
    public Guid ClienteId { get; private set; }

    /// <summary>RFC del receptor (snapshot del CFDI) — valida "mismo cliente" al vincular.</summary>
    public string ReceptorRfc { get; private set; } = string.Empty;

    public TipoAnticipo TipoAnticipo { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    public decimal MontoCobrado { get; private set; }
    public decimal MontoAmortizado { get; private set; }
    /// <summary>F6.3 · CA6.17: devuelto en dinero al cliente por NC de devolución de anticipo (no aplicado a una factura).</summary>
    public decimal MontoDevuelto { get; private set; }

    /// <summary>Saldo amortizable persistido = <c>MontoCobrado − MontoAmortizado</c>.</summary>
    public decimal Saldo { get; private set; }

    public EstadoAnticipo Estado { get; private set; }

    /// <summary>CFDI de anticipo que originó el saldo (relación 1:1).</summary>
    public Guid FacturaAnticipoId { get; private set; }

    /// <summary>Número de pedido del origen (A+W) para trazabilidad en el reporte.</summary>
    public string? PedidoOrigenRef { get; private set; }

    // ---- Obra (snapshot, para el Control de Anticipos por obra) ----
    public long? ObraId { get; private set; }
    public string? ObraNombre { get; private set; }

    private readonly List<AnticipoVinculacion> _vinculaciones = [];
    public IReadOnlyCollection<AnticipoVinculacion> Vinculaciones => _vinculaciones.AsReadOnly();

    private readonly List<AnticipoLiberacion> _liberaciones = [];
    /// <summary>U-4: apartados liberados a mano (con usuario y motivo).</summary>
    public IReadOnlyCollection<AnticipoLiberacion> Liberaciones => _liberaciones.AsReadOnly();

    /// <summary>
    /// Saldo aún vinculable: <c>Saldo</c> menos lo comprometido por vinculaciones
    /// sin NC. Evita comprometer más de lo cobrado entre M2 y M3.
    /// </summary>
    public decimal SaldoDisponible =>
        Saldo - _vinculaciones.Where(v => v.NcAmortizacionId is null).Sum(v => v.Importe);

    private Anticipo() { }

    private Anticipo(
        Guid id,
        Guid empresaId,
        Guid clienteId,
        string receptorRfc,
        TipoAnticipo tipoAnticipo,
        string moneda,
        decimal montoCobrado,
        Guid facturaAnticipoId,
        string? pedidoOrigenRef,
        long? obraId,
        string? obraNombre) : base(id)
    {
        EmpresaId = empresaId;
        ClienteId = clienteId;
        ReceptorRfc = receptorRfc.ToUpperInvariant();
        TipoAnticipo = tipoAnticipo;
        Moneda = moneda.ToUpperInvariant();
        MontoCobrado = montoCobrado;
        MontoAmortizado = 0m;
        Saldo = montoCobrado;
        FacturaAnticipoId = facturaAnticipoId;
        PedidoOrigenRef = pedidoOrigenRef;
        ObraId = obraId;
        ObraNombre = obraNombre;
        Estado = EstadoAnticipo.Abierto;
    }

    /// <summary>
    /// P45: saldo de un anticipo timbrado fuera del ERP. <paramref name="montoCobrado"/> es el total del CFDI y
    /// <paramref name="saldoVivo"/> lo que queda por aplicar; la diferencia se toma como ya amortizada fuera del ERP.
    /// </summary>
    public static Anticipo CrearExterno(
        Guid empresaId,
        Guid clienteId,
        string receptorRfc,
        TipoAnticipo tipoAnticipo,
        string moneda,
        decimal montoCobrado,
        decimal saldoVivo,
        Guid facturaAnticipoId,
        string? pedidoOrigenRef,
        Guid id)
    {
        if (saldoVivo <= 0 || saldoVivo > montoCobrado)
            throw new BusinessRuleException("ANTICIPO_EXTERNO_SALDO_INVALIDO",
                "El saldo vivo debe ser mayor que cero y no mayor que el total del CFDI de anticipo.");
        var anticipo = Crear(empresaId, clienteId, receptorRfc, tipoAnticipo, moneda, montoCobrado, facturaAnticipoId,
            pedidoOrigenRef, id: id);
        anticipo.MontoAmortizado = montoCobrado - saldoVivo;
        anticipo.Saldo = saldoVivo;
        return anticipo;
    }

    /// <summary>
    /// Crea el saldo de un anticipo al timbrar su CFDI (M1). <paramref name="montoCobrado"/>
    /// es el techo amortizable: para PUE el total del CFDI; para PPD arranca en 0
    /// y lo incrementan los REPP (F6).
    /// </summary>
    public static Anticipo Crear(
        Guid empresaId,
        Guid clienteId,
        string receptorRfc,
        TipoAnticipo tipoAnticipo,
        string moneda,
        decimal montoCobrado,
        Guid facturaAnticipoId,
        string? pedidoOrigenRef = null,
        long? obraId = null,
        string? obraNombre = null,
        Guid? id = null)
    {
        if (montoCobrado < 0)
            throw new BusinessRuleException("ANTICIPO_COBRO_INVALIDO", "El monto cobrado no puede ser negativo.");

        return new Anticipo(
            id ?? Guid.CreateVersion7(),
            empresaId, clienteId, receptorRfc, tipoAnticipo, moneda, montoCobrado,
            facturaAnticipoId, string.IsNullOrWhiteSpace(pedidoOrigenRef) ? null : pedidoOrigenRef.Trim(), obraId, obraNombre);
    }

    /// <summary>
    /// Vincula este anticipo a una factura de venta final (M2, §6.3). Valida que
    /// no se comprometa más que el <see cref="SaldoDisponible"/>. <b>No reduce el
    /// saldo amortizado</b> — solo lo compromete; la reducción es la NC (M3).
    /// </summary>
    public AnticipoVinculacion Vincular(Guid facturaVentaId, decimal importe, DateTimeOffset ahora)
    {
        if (Estado != EstadoAnticipo.Abierto)
            throw new BusinessRuleException(
                "ANTICIPO_NO_VINCULABLE",
                $"Solo un anticipo Abierto puede vincularse (actual: {Estado}).");
        if (importe <= 0)
            throw new BusinessRuleException("ANTICIPO_IMPORTE_INVALIDO", "El importe a vincular debe ser mayor que cero.");
        if (_vinculaciones.Any(v => v.FacturaVentaId == facturaVentaId))
            throw new BusinessRuleException(
                "ANTICIPO_YA_VINCULADO",
                "Este anticipo ya está vinculado a esa factura.");
        if (importe > SaldoDisponible)
            throw new BusinessRuleException(
                "ANTICIPO_SALDO_INSUFICIENTE",
                $"El importe a vincular ({importe}) excede el saldo disponible ({SaldoDisponible}).");

        var vinculacion = new AnticipoVinculacion(Guid.CreateVersion7(), Id, facturaVentaId, importe, ahora);
        _vinculaciones.Add(vinculacion);
        return vinculacion;
    }

    /// <summary>
    /// U-1 (86e3yxn2p): libera el importe comprometido con una factura cuya NC de
    /// amortización no se timbró y se descartó. Solo quita la vinculación sin NC.
    /// </summary>
    public void LiberarVinculacion(Guid facturaVentaId)
    {
        var vinculacion = _vinculaciones.FirstOrDefault(v => v.FacturaVentaId == facturaVentaId && v.NcAmortizacionId is null);
        if (vinculacion is not null)
            _vinculaciones.Remove(vinculacion);
    }

    public const int MotivoLiberacionMin = 5;
    public const int MotivoLiberacionMax = 500;

    /// <summary>
    /// U-4 (86e3yzemw · flujo 3): libera de forma explícita el importe apartado (vinculado sin NC) con
    /// <paramref name="facturaVentaId"/> porque no corresponde. Reúsa <see cref="LiberarVinculacion"/> y deja
    /// constancia (usuario, motivo, importe). Un apartado ya amortizado con su NC no se libera: se cancela la NC.
    /// </summary>
    public AnticipoLiberacion LiberarApartado(Guid facturaVentaId, string motivo, Guid usuarioId, string usuarioNombre, DateTimeOffset ahora)
    {
        var texto = (motivo ?? string.Empty).Trim();
        if (texto.Length < MotivoLiberacionMin || texto.Length > MotivoLiberacionMax)
            throw new BusinessRuleException("ANTICIPO_MOTIVO_REQUERIDO",
                $"Indique por qué se libera el apartado ({MotivoLiberacionMin} a {MotivoLiberacionMax} caracteres).");
        var vinculos = _vinculaciones.Where(v => v.FacturaVentaId == facturaVentaId).ToList();
        if (vinculos.Count == 0)
            throw new BusinessRuleException("ANTICIPO_APARTADO_NO_ENCONTRADO", "El anticipo no tiene importe apartado para esa factura.");
        var apartado = vinculos.FirstOrDefault(v => v.NcAmortizacionId is null)
            ?? throw new BusinessRuleException("ANTICIPO_APARTADO_YA_AMORTIZADO",
                "El importe ya se amortizó con su nota de crédito; para revertirlo se cancela la nota de crédito.");

        LiberarVinculacion(facturaVentaId);
        var liberacion = new AnticipoLiberacion(Guid.CreateVersion7(), Id, facturaVentaId, apartado.Importe, texto,
            usuarioId, string.IsNullOrWhiteSpace(usuarioNombre) ? "Usuario" : usuarioNombre.Trim(), ahora);
        _liberaciones.Add(liberacion);
        return liberacion;
    }

    /// <summary>
    /// Registra la amortización de un anticipo con su NC (Momento 3, §6.3). Reduce
    /// el saldo (<see cref="MontoAmortizado"/> += importe) y asocia la NC a la
    /// vinculación. Si la factura ya estaba pre-vinculada (M2 vía
    /// <see cref="Vincular"/>), reutiliza esa vinculación con su importe; si no,
    /// la crea en el acto (M2+M3 juntos) validando el saldo disponible. Cuando el
    /// saldo llega a 0, el anticipo pasa a <see cref="EstadoAnticipo.Amortizado"/>.
    /// </summary>
    public AnticipoVinculacion RegistrarAmortizacion(Guid facturaVentaId, Guid ncAmortizacionId, decimal importe, DateTimeOffset ahora)
    {
        if (Estado != EstadoAnticipo.Abierto)
            throw new BusinessRuleException(
                "ANTICIPO_NO_AMORTIZABLE",
                $"Solo un anticipo Abierto puede amortizarse (actual: {Estado}).");
        if (importe <= 0)
            throw new BusinessRuleException("ANTICIPO_IMPORTE_INVALIDO", "El importe a amortizar debe ser mayor que cero.");

        var vinculacion = _vinculaciones.FirstOrDefault(v => v.FacturaVentaId == facturaVentaId);
        if (vinculacion is not null)
        {
            if (vinculacion.NcAmortizacionId is not null)
                throw new BusinessRuleException(
                    "ANTICIPO_YA_AMORTIZADO",
                    "Esa vinculación ya tiene una NC de amortización asociada.");
            importe = vinculacion.Importe; // el compromiso M2 manda
        }
        else
        {
            if (importe > SaldoDisponible)
                throw new BusinessRuleException(
                    "ANTICIPO_SALDO_INSUFICIENTE",
                    $"El importe a amortizar ({importe}) excede el saldo disponible ({SaldoDisponible}).");
            vinculacion = new AnticipoVinculacion(Guid.CreateVersion7(), Id, facturaVentaId, importe, ahora);
            _vinculaciones.Add(vinculacion);
        }

        vinculacion.AsociarNotaCredito(ncAmortizacionId);
        MontoAmortizado += importe;
        Saldo = MontoCobrado - MontoAmortizado - MontoDevuelto;
        if (Saldo <= 0m)
            Estado = EstadoAnticipo.Amortizado;

        return vinculacion;
    }

    /// <summary>
    /// F6.3 · CA6.17: el anticipo no aplicado se devuelve en dinero con una NC de devolución. Solo hasta el
    /// <see cref="SaldoDisponible"/> (lo comprometido en vinculaciones no se puede devolver); al llegar a cero el anticipo
    /// queda <see cref="EstadoAnticipo.Amortizado"/> (sin saldo).
    /// </summary>
    /// <summary>
    /// M-344: revierte la aplicación (amortización) de este anticipo a una factura: devuelve el importe al saldo,
    /// reabre el anticipo si se había agotado y retira la vinculación (queda el rastro en la reversa), de modo que
    /// la factura pueda volver a recibir una aplicación. No toca el CFDI de la NC de amortización.
    /// </summary>
    public ReversaAplicacionAnticipo RevertirAplicacion(
        Guid facturaVentaId, string motivo, Guid? aplicadoPorId, Guid autorizadoPorId, string autorizadoPorNombre, DateTimeOffset ahora)
    {
        if (Estado == EstadoAnticipo.Cancelado)
            throw new BusinessRuleException("ANTICIPO_CANCELADO", "Un anticipo cancelado no admite reversa de aplicaciones.");
        var vinculacion = _vinculaciones.FirstOrDefault(v => v.FacturaVentaId == facturaVentaId && v.NcAmortizacionId is not null)
            ?? throw new BusinessRuleException("ANTICIPO_SIN_APLICACION",
                "El anticipo no tiene una aplicación a esa factura que revertir.");
        var reversa = ReversaAplicacionAnticipo.Crear(EmpresaId, Id, facturaVentaId, vinculacion.NcAmortizacionId!.Value,
            vinculacion.Importe, motivo, aplicadoPorId, autorizadoPorId, autorizadoPorNombre, ahora);
        MontoAmortizado -= vinculacion.Importe;
        Saldo = MontoCobrado - MontoAmortizado - MontoDevuelto;
        if (Estado == EstadoAnticipo.Amortizado && Saldo > 0m)
            Estado = EstadoAnticipo.Abierto;
        _vinculaciones.Remove(vinculacion);
        return reversa;
    }

    public void RegistrarDevolucion(decimal importe)
    {
        if (Estado != EstadoAnticipo.Abierto)
            throw new BusinessRuleException("ANTICIPO_NO_ABIERTO",
                $"Solo un anticipo abierto puede devolverse (estado actual: {Estado}).");
        if (importe <= 0m)
            throw new BusinessRuleException("ANTICIPO_IMPORTE_INVALIDO", "El importe a devolver debe ser mayor que cero.");
        if (importe > SaldoDisponible)
            throw new BusinessRuleException("ANTICIPO_SALDO_INSUFICIENTE",
                $"El importe a devolver ({importe:N2}) excede el saldo disponible del anticipo ({SaldoDisponible:N2}).");

        MontoDevuelto += importe;
        Saldo = MontoCobrado - MontoAmortizado - MontoDevuelto;
        if (Saldo <= 0m)
            Estado = EstadoAnticipo.Amortizado;
    }

    /// <summary>
    /// Marca el anticipo como <see cref="EstadoAnticipo.Cancelado"/> al cancelarse
    /// su CFDI (§6.6). La validación de cadena (no cancelar si hay NCs de
    /// amortización vigentes, invariante 8) la hace el handler antes de llamar.
    /// </summary>
    public void Cancelar()
    {
        if (Estado == EstadoAnticipo.Cancelado)
            return;
        Estado = EstadoAnticipo.Cancelado;
    }
}
