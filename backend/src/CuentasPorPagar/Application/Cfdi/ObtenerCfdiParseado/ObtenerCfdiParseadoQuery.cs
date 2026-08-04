using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.ObtenerCfdiParseado;

/// <summary>
/// Re-parsea on-demand el XML de un <see cref="CfdiRecibido"/> desde el
/// blob storage para pre-llenar la captura de factura (encabezado +
/// líneas de <c>cfdi:Concepto</c>). Cierra el
/// PLATFORM-TODO(&lt;CfdiXmlViewer&gt;) en su parte de auto-fill.
///
/// <para>
/// Las líneas NO se persisten en BD (decisión metadata-only, PR12):
/// viven en el XML y se extraen aquí con el mismo
/// <see cref="IXmlCfdiParser"/> de la ingesta. Costo acotado: un XML de
/// CFDI pesa KBs y esta query solo se invoca al capturar.
/// </para>
/// </summary>
public sealed record ObtenerCfdiParseadoQuery(Guid Id) : IRequest<CfdiParseadoResponse>;

public sealed record CfdiParseadoResponse(
    Guid Id,
    string UuidCfdi,
    decimal Total,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    string Moneda,
    decimal? TipoCambio,
    IReadOnlyList<CfdiLineaParseadaResponse> Lineas,
    // TES-PR8 [T-G11]: PUE/PPD para el prellenado de captura (aditivo).
    string? MetodoPago = null,
    // Relaciones <cfdi:CfdiRelacionados> para prellenar la captura de
    // NC/anticipo (TipoRelacion + UUID del CFDI origen). Aditivo.
    IReadOnlyList<CfdiRelacionadosResponse>? CfdiRelacionados = null);

public sealed record CfdiRelacionadosResponse(
    string TipoRelacion,
    IReadOnlyList<string> Uuids);

public sealed record CfdiLineaParseadaResponse(
    int Posicion,
    string ClaveProdServ,
    decimal Cantidad,
    string ClaveUnidad,
    string? Unidad,
    string Descripcion,
    decimal ValorUnitario,
    decimal Importe,
    decimal? Descuento);

public sealed class ObtenerCfdiParseadoHandler
    : IRequestHandler<ObtenerCfdiParseadoQuery, CfdiParseadoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICfdiBlobStorage _blobStorage;
    private readonly IXmlCfdiParser _parser;

    public ObtenerCfdiParseadoHandler(
        CuentasPorPagarDbContext db,
        ICfdiBlobStorage blobStorage,
        IXmlCfdiParser parser)
    {
        _db = db;
        _blobStorage = blobStorage;
        _parser = parser;
    }

    public async Task<CfdiParseadoResponse> Handle(
        ObtenerCfdiParseadoQuery query,
        CancellationToken cancellationToken)
    {
        var cfdi = await _db.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_NO_ENCONTRADO",
                $"No se encontró el CFDI con id '{query.Id}'.");

        if (string.IsNullOrEmpty(cfdi.XmlBlobRef))
        {
            throw new BusinessRuleException(
                "CFDI_SIN_XML",
                "El CFDI no tiene XML almacenado; no se puede extraer el detalle.");
        }

        await using var xml = await _blobStorage.LeerXmlAsync(cfdi.XmlBlobRef, cancellationToken)
            ?? throw new BusinessRuleException(
                "CFDI_XML_NO_DISPONIBLE",
                "El XML del CFDI no está disponible en el almacenamiento.");

        var datos = _parser.Parsear(xml);

        return new CfdiParseadoResponse(
            Id: cfdi.Id,
            UuidCfdi: cfdi.UuidCfdi.Valor,
            Total: datos.Total,
            Subtotal: datos.Subtotal,
            ImpuestosTrasladados: datos.ImpuestosTrasladados,
            Retenciones: datos.Retenciones,
            Moneda: datos.Moneda,
            TipoCambio: datos.TipoCambio,
            Lineas: datos.Lineas.Select(l => new CfdiLineaParseadaResponse(
                l.Posicion,
                l.ClaveProdServ,
                l.Cantidad,
                l.ClaveUnidad,
                l.Unidad,
                l.Descripcion,
                l.ValorUnitario,
                l.Importe,
                l.Descuento)).ToList(),
            MetodoPago: datos.MetodoPago,
            CfdiRelacionados: datos.CfdiRelacionados
                ?.Select(r => new CfdiRelacionadosResponse(r.TipoRelacion, r.Uuids))
                .ToList());
    }
}
