using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>Efecto posterior al timbrado que se registra y se puede reintentar por separado (M-285).</summary>
public enum TipoEfectoComprobante : short
{
    /// <summary>Evento de timbrado al Outbox: Contabilidad arma la póliza y CxC la cartera.</summary>
    EventoContable = 1,

    /// <summary>Estado del pedido facturable y control de ingesta de A+W (aviso de facturado).</summary>
    EstadoPedido = 2,

    /// <summary>NC automática por la ranura del pedido de A+W.</summary>
    NcRanura = 3,

    /// <summary>Solicitud de cancelación de la factura que esta sustituye (FAC-08b).</summary>
    CancelacionSustituida = 4,
}

public enum EstadoEfectoComprobante : short
{
    Pendiente = 1,
    Aplicado = 2,
}

/// <summary>
/// M-285 (86e3zr7af): un efecto posterior al timbrado de un comprobante. Se crea «Pendiente» en el
/// mismo guardado que marca el timbrado; se aplica por separado y, si falla, conserva el error sin
/// deshacer el timbrado. «Reintentar pendientes» aplica solo los que siguen pendientes.
/// </summary>
public sealed class EfectoComprobante : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const int ErrorMaxLength = 1000;

    public Guid EmpresaId { get; set; }
    public Guid ComprobanteId { get; private set; }
    public TipoEfectoComprobante Tipo { get; private set; }

    /// <summary>Cliente contable del evento (el que capturó la emisión), para publicarlo igual al reintentar.</summary>
    public Guid? ClienteId { get; private set; }
    public EstadoEfectoComprobante Estado { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoErrorCodigo { get; private set; }
    public string? UltimoError { get; private set; }
    public DateTimeOffset? UltimoIntentoAt { get; private set; }
    public DateTimeOffset? AplicadoAt { get; private set; }

    private EfectoComprobante() { }

    public static EfectoComprobante Pendiente(Guid empresaId, Guid comprobanteId, TipoEfectoComprobante tipo, Guid? clienteId = null) => new()
    {
        ClienteId = clienteId,
        Id = Guid.CreateVersion7(),
        EmpresaId = empresaId,
        ComprobanteId = comprobanteId,
        Tipo = tipo,
        Estado = EstadoEfectoComprobante.Pendiente,
    };

    public void MarcarAplicado(DateTimeOffset ahora)
    {
        Intentos++;
        Estado = EstadoEfectoComprobante.Aplicado;
        UltimoIntentoAt = ahora;
        AplicadoAt = ahora;
        UltimoErrorCodigo = null;
        UltimoError = null;
    }

    public void RegistrarError(string? codigo, string mensaje, DateTimeOffset ahora)
    {
        Intentos++;
        UltimoIntentoAt = ahora;
        UltimoErrorCodigo = codigo is null || codigo.Length <= 50 ? codigo : codigo[..50];
        UltimoError = mensaje.Length <= ErrorMaxLength ? mensaje : mensaje[..ErrorMaxLength];
    }
}
