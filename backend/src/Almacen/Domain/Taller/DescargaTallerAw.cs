using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Taller;

/// <summary>
/// Una posición de un pedido interno Serie 9000 (folios 90000000–99999999, hito 119) leída de A+W: descarga el
/// inventario de hojas del taller de la sucursal (PT5.10). Tabla <c>almacen.descargas_taller_aw</c>: llave única
/// (empresa, origen, pedido, posición), así la descarga baja una sola vez aunque el pedido se lea otra vez. También es
/// la bandeja: <see cref="EnRevision"/> (sin sucursal, artículo, medida o existencia; se reintenta).
/// </summary>
public sealed class DescargaTallerAw : IPerteneceAEmpresa
{
    public const string Recibido = "RECIBIDO";
    public const string EnRevision = "EN_REVISION";

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public string Origen { get; private set; } = string.Empty;
    public string NumeroPedido { get; private set; } = string.Empty;
    public int Posicion { get; private set; }
    public string ArticuloRef { get; private set; } = string.Empty;
    public decimal Cantidad { get; private set; }
    public decimal? AnchoMm { get; private set; }
    public decimal? AltoMm { get; private set; }
    public string? ClaveSucursal { get; private set; }
    /// <summary>Sucursal del ERP; se llena al descontar.</summary>
    public Guid? SucursalId { get; private set; }
    /// <summary>Día del pedido en A+W (el del cierre del taller).</summary>
    public DateOnly FechaPedido { get; private set; }
    public string Estado { get; private set; } = string.Empty;
    /// <summary>m² descargados = hojas × área de la hoja; null mientras no se resuelva la medida.</summary>
    public decimal? M2 { get; private set; }
    public string? Folio { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset RegistradoAt { get; private set; }
    public DateTimeOffset IntentadoAt { get; private set; }

    private DescargaTallerAw() { }

    public DescargaTallerAw(Guid empresaId, string origen, string numeroPedido, int posicion, string articuloRef, decimal cantidad,
        decimal? anchoMm, decimal? altoMm, string? claveSucursal, DateOnly fechaPedido)
    {
        Id = Guid.CreateVersion7();
        EmpresaId = empresaId; Origen = origen; NumeroPedido = numeroPedido; Posicion = posicion; ArticuloRef = articuloRef;
        Cantidad = cantidad; AnchoMm = anchoMm; AltoMm = altoMm; ClaveSucursal = claveSucursal; FechaPedido = fechaPedido;
        RegistradoAt = DateTimeOffset.UtcNow;
    }

    public void Resolver(string estado, Guid? sucursalId, decimal? m2, string? folio, string? error)
    {
        Estado = estado; SucursalId = sucursalId; M2 = m2; Folio = folio;
        Error = error?.Length > 1000 ? error[..1000] : error;
        IntentadoAt = DateTimeOffset.UtcNow;
    }
}
