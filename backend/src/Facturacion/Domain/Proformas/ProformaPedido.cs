using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Proformas;

/// <summary>
/// Proforma de un pedido facturable (U1.8a): vista previa SIN VALOR FISCAL de
/// lo que se va a timbrar, para que el cliente la revise antes del CFDI. Cada
/// generación es una versión nueva e inmutable (1, 2, 3…) y su folio
/// <c>PF-{numeroPedido}-{version}</c> NO consume folio fiscal (no usa
/// <c>Compartido.Series</c>).
///
/// <para>
/// El contenido comparable (emisor, receptor, conceptos, impuestos y totales)
/// se guarda serializado en <see cref="ContenidoJson"/> (jsonb) para mostrar la
/// proforma tal como se entregó y comparar contra lo que se timbra después.
/// </para>
/// </summary>
public sealed class ProformaPedido : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    /// <summary>Prefijo del folio de proforma (no fiscal).</summary>
    public const string PrefijoFolio = "PF";

    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public Guid PedidoFacturableId { get; private set; }

    /// <summary>Versión de la proforma dentro del pedido (1..n); única por pedido.</summary>
    public int NumeroVersion { get; private set; }

    public string Folio { get; private set; } = string.Empty;
    public string? NumeroPedido { get; private set; }

    /// <summary>Contenido comparable de la proforma (ProformaCfdi serializado, jsonb).</summary>
    public string ContenidoJson { get; private set; } = string.Empty;

    /// <summary>Avisos de datos fiscales incompletos al generar (arreglo de textos, jsonb).</summary>
    public string AvisosJson { get; private set; } = "[]";

    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    public Guid? GeneradaPorUsuarioId { get; private set; }
    public string? GeneradaPorNombre { get; private set; }
    public DateTimeOffset GeneradaEn { get; private set; }

    private ProformaPedido() { }

    private ProformaPedido(
        Guid id, Guid empresaId, Guid sucursalId, Guid pedidoFacturableId, int numeroVersion, string? numeroPedido,
        string contenidoJson, string avisosJson, decimal total, string moneda,
        Guid? generadaPorUsuarioId, string? generadaPorNombre, DateTimeOffset generadaEn) : base(id)
    {
        if (numeroVersion < 1)
            throw new BusinessRuleException("FAC_PROFORMA_VERSION_INVALIDA", "La versión de la proforma debe ser >= 1.");
        if (string.IsNullOrWhiteSpace(contenidoJson))
            throw new BusinessRuleException("FAC_PROFORMA_SIN_CONTENIDO", "La proforma requiere contenido.");

        EmpresaId = empresaId;
        SucursalId = sucursalId;
        PedidoFacturableId = pedidoFacturableId;
        NumeroVersion = numeroVersion;
        NumeroPedido = numeroPedido;
        Folio = FolioPara(numeroPedido, pedidoFacturableId, numeroVersion);
        ContenidoJson = contenidoJson;
        AvisosJson = string.IsNullOrWhiteSpace(avisosJson) ? "[]" : avisosJson;
        Total = total;
        Moneda = moneda;
        GeneradaPorUsuarioId = generadaPorUsuarioId;
        GeneradaPorNombre = generadaPorNombre;
        GeneradaEn = generadaEn;
    }

    public static ProformaPedido Generar(
        Guid empresaId, Guid sucursalId, Guid pedidoFacturableId, int numeroVersion, string? numeroPedido,
        string contenidoJson, string avisosJson, decimal total, string moneda,
        Guid? generadaPorUsuarioId, string? generadaPorNombre, DateTimeOffset generadaEn) =>
        new(Guid.CreateVersion7(), empresaId, sucursalId, pedidoFacturableId, numeroVersion, numeroPedido,
            contenidoJson, avisosJson, total, moneda, generadaPorUsuarioId, generadaPorNombre, generadaEn);

    /// <summary>
    /// Folio <c>PF-{numeroPedido}-{version}</c>; un pedido manual sin número usa
    /// los primeros 8 caracteres de su id.
    /// </summary>
    public static string FolioPara(string? numeroPedido, Guid pedidoFacturableId, int numeroVersion)
    {
        var referencia = string.IsNullOrWhiteSpace(numeroPedido)
            ? pedidoFacturableId.ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant()
            : numeroPedido.Trim();
        return string.Create(CultureInfo.InvariantCulture, $"{PrefijoFolio}-{referencia}-{numeroVersion}");
    }
}
