namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// CXC-06 (86e3qddef): lectura de las pólizas de cobro vigentes para que CxC concilie su cartera contra la contabilidad. Solo lee
/// pólizas autorizadas que no son anulación ni están anuladas; la empresa sale del contexto del usuario (filtro global).
/// </summary>
public interface ICobrosContabilizadosReadPort
{
    /// <summary>Pólizas de cobro (pago de cliente, cobro de mostrador y liquidación de efectivo) con fecha en el rango.</summary>
    Task<IReadOnlyList<PolizaCobroLectura>> ListarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct);

    /// <summary>Pólizas vigentes de los documentos indicados, sin importar su fecha (para cerrar el cruce en los bordes del rango).</summary>
    Task<IReadOnlyList<PolizaCobroLectura>> ListarPorDocumentosAsync(string tipoDocumento, IReadOnlyCollection<Guid> documentos, CancellationToken ct);

    /// <summary>Cobros en efectivo de mostrador y si ya se liquidaron en la póliza de cierre de caja (#11b).</summary>
    Task<IReadOnlyList<EfectivoCobroLectura>> EfectivoAsync(IReadOnlyCollection<Guid> cobrosMostrador, CancellationToken ct);
}

/// <summary>Tipos de documento origen de las pólizas de cobro.</summary>
public static class DocumentoCobroContable
{
    public const string PagoCliente = "pago_cliente";
    public const string CobroMostrador = "cobro_mostrador";
    public const string CajaSesion = "caja_sesion";
}

/// <summary>
/// <paramref name="AbonoClientes"/>: lo que la póliza abona a la cuenta de clientes (abonos menos cargos de las partidas con auxiliar de
/// cliente), en la moneda del cobro.
/// </summary>
public sealed record PolizaCobroLectura(
    Guid PolizaId, string Folio, DateOnly Fecha, string TipoDocumento, Guid DocumentoId, Guid? ClienteId, string Moneda, decimal AbonoClientes);

public sealed record EfectivoCobroLectura(Guid CobroId, decimal Importe, bool Liquidado, Guid? PolizaLiquidacionId);
