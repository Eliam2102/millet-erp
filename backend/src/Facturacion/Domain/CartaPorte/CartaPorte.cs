using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// Carta Porte 3.1 — especialización de <see cref="Comprobante"/> (§4.6
/// levantamiento). Entidad independiente de la factura: un pedido genera N Carta
/// Portes (una por tramo con vehículo u operador distinto). Puede ser CFDI tipo
/// <b>T</b> (Traslado, mercancía propia, total 0) o <b>I</b> (Ingreso, cuando se
/// factura el servicio de transporte en el mismo tramo).
///
/// <para>
/// Multi-tramo (invariante 11): un cambio de vehículo/operador crea una Carta
/// Porte nueva que referencia la anterior (<see cref="CartaPortePreviaId"/>); no
/// se muta la existente.
/// </para>
/// </summary>
public sealed class CartaPorte : Comprobante
{
    public string Origen { get; private set; } = string.Empty;
    public string Destino { get; private set; } = string.Empty;
    public decimal DistanciaKm { get; private set; }

    // ---- Domicilio SAT de las ubicaciones (CP 3.1 exige Domicilio en Origen y
    // Destino; país siempre MEX en el MVP). Nullable en el agregado: la
    // obligatoriedad se valida al timbrar real (CfdiEmisionBuilder), no al
    // crear el borrador — F12-PR3.
    /// <summary>Código postal del origen del tramo (5 dígitos).</summary>
    public string? OrigenCodigoPostal { get; private set; }
    /// <summary>Estado del origen (<c>c_Estado</c>, 3 chars p.ej. QUE/CMX).</summary>
    public string? OrigenEstado { get; private set; }
    /// <summary>Código postal del destino del tramo (5 dígitos).</summary>
    public string? DestinoCodigoPostal { get; private set; }
    /// <summary>Estado del destino (<c>c_Estado</c>, 3 chars).</summary>
    public string? DestinoEstado { get; private set; }

    public Guid VehiculoId { get; private set; }
    public Guid OperadorId { get; private set; }

    /// <summary>Carta Porte previa (continuación de tramo); null en el primer tramo.</summary>
    public Guid? CartaPortePreviaId { get; private set; }

    public Guid? PedidoFacturableId { get; private set; }

    public DateTimeOffset FechaSalida { get; private set; }
    public DateTimeOffset FechaLlegadaEstimada { get; private set; }

    private readonly List<CartaPorteMercancia> _mercancias = [];
    public IReadOnlyCollection<CartaPorteMercancia> Mercancias => _mercancias.AsReadOnly();

    private CartaPorte() { }

    private CartaPorte(
        Guid id, Guid empresaId, TipoComprobante tipoCfdi, string folio, long folioNumero, Guid sucursalId,
        Guid? cajaId, Guid? usuarioEmisorId, DatosFiscalesReceptor receptor, DatosFiscalesEmisor emisor,
        string moneda, int periodoAnio, int periodoMes, string origen, string destino, decimal distanciaKm,
        Guid vehiculoId, Guid operadorId, Guid? cartaPortePreviaId, Guid? pedidoFacturableId,
        DateTimeOffset fechaSalida, DateTimeOffset fechaLlegadaEstimada,
        string? origenCodigoPostal, string? origenEstado, string? destinoCodigoPostal, string? destinoEstado)
        : base(id, empresaId, tipoCfdi, folio, folioNumero, sucursalId, cajaId, usuarioEmisorId, receptor,
               emisor, metodoPago: "PUE", formaPago: "01", moneda, tipoCambio: null,
               periodoAnio, periodoMes)
    {
        Origen = origen;
        Destino = destino;
        DistanciaKm = distanciaKm;
        OrigenCodigoPostal = origenCodigoPostal;
        OrigenEstado = origenEstado;
        DestinoCodigoPostal = destinoCodigoPostal;
        DestinoEstado = destinoEstado;
        VehiculoId = vehiculoId;
        OperadorId = operadorId;
        CartaPortePreviaId = cartaPortePreviaId;
        PedidoFacturableId = pedidoFacturableId;
        FechaSalida = fechaSalida;
        FechaLlegadaEstimada = fechaLlegadaEstimada;
    }

    /// <summary>
    /// Crea una Carta Porte en <see cref="EstadoTimbrado.Borrador"/>. <paramref name="tipoCfdi"/>
    /// debe ser <c>Traslado</c> (total 0) o <c>Ingreso</c> (servicio facturado:
    /// <paramref name="montoServicio"/> + IVA). El handler agrega las mercancías.
    /// </summary>
    public static CartaPorte CrearBorrador(
        Guid empresaId, TipoComprobante tipoCfdi, string folio, long folioNumero, Guid sucursalId, Guid? cajaId,
        Guid? usuarioEmisorId, DatosFiscalesReceptor receptor, DatosFiscalesEmisor emisor,
        string moneda, int periodoAnio, int periodoMes, string origen, string destino, decimal distanciaKm,
        Guid vehiculoId, Guid operadorId, Guid? cartaPortePreviaId, Guid? pedidoFacturableId,
        DateTimeOffset fechaSalida, DateTimeOffset fechaLlegadaEstimada,
        decimal montoServicio = 0m, decimal? tasaIvaServicio = null,
        string? origenCodigoPostal = null, string? origenEstado = null,
        string? destinoCodigoPostal = null, string? destinoEstado = null)
    {
        if (tipoCfdi is not (TipoComprobante.Traslado or TipoComprobante.Ingreso))
            throw new BusinessRuleException("CARTA_PORTE_TIPO_INVALIDO", "La Carta Porte solo admite CFDI tipo T (Traslado) o I (Ingreso).");
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            throw new BusinessRuleException("CARTA_PORTE_TRAMO_INVALIDO", "El origen y el destino del tramo son obligatorios.");
        if (vehiculoId == Guid.Empty || operadorId == Guid.Empty)
            throw new BusinessRuleException("CARTA_PORTE_TRANSPORTE_INVALIDO", "El vehículo y el operador son obligatorios.");
        if (tipoCfdi == TipoComprobante.Ingreso && montoServicio <= 0)
            throw new BusinessRuleException("CARTA_PORTE_MONTO_INVALIDO", "Una Carta Porte tipo I debe facturar el servicio de transporte (monto > 0).");

        var cp = new CartaPorte(
            Guid.CreateVersion7(), empresaId, tipoCfdi, folio, folioNumero, sucursalId, cajaId, usuarioEmisorId,
            receptor, emisor, moneda, periodoAnio, periodoMes, origen, destino, distanciaKm,
            vehiculoId, operadorId, cartaPortePreviaId, pedidoFacturableId, fechaSalida, fechaLlegadaEstimada,
            origenCodigoPostal, origenEstado, destinoCodigoPostal, destinoEstado);

        if (tipoCfdi == TipoComprobante.Ingreso)
        {
            var iva = tasaIvaServicio is > 0 ? Math.Round(montoServicio * tasaIvaServicio.Value, 2) : 0m;
            cp.EstablecerTotales(montoServicio, 0m, iva, 0m, montoServicio + iva);
        }
        else
        {
            cp.EstablecerTotales(0m, 0m, 0m, 0m, 0m); // Traslado: sin importe
        }

        return cp;
    }

    /// <summary>Agrega una mercancía transportada. Solo en Borrador.</summary>
    public CartaPorteMercancia AgregarMercancia(
        string descripcion, string bienesTransp, string claveUnidad, decimal cantidad, decimal pesoEnKg, bool materialPeligroso)
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("CARTA_PORTE_INMUTABLE", $"No se pueden agregar mercancías a una Carta Porte en estado {Estado}.");
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new BusinessRuleException("CARTA_PORTE_MERCANCIA_INVALIDA", "La descripción de la mercancía es obligatoria.");
        if (cantidad <= 0 || pesoEnKg <= 0)
            throw new BusinessRuleException("CARTA_PORTE_MERCANCIA_INVALIDA", "La cantidad y el peso deben ser mayores que cero.");

        var mercancia = new CartaPorteMercancia(Guid.CreateVersion7(), Id, descripcion, bienesTransp, claveUnidad, cantidad, pesoEnKg, materialPeligroso);
        _mercancias.Add(mercancia);
        return mercancia;
    }
}
