using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.ObtenerCfdiPorId;

/// <summary>
/// Detalle de un <see cref="CfdiRecibido"/> para la vista de detalle de
/// la bandeja (viewer, F1-PR1 diferido). Cierra la parte JSON del
/// PLATFORM-TODO(&lt;CfdiBlobDownload&gt;); la descarga binaria vive en
/// <c>DescargarCfdiBlobQuery</c>.
/// </summary>
public sealed record ObtenerCfdiPorIdQuery(Guid Id) : IRequest<CfdiDetalleResponse>;

public sealed record CfdiDetalleResponse(
    Guid Id,
    string UuidCfdi,
    string RfcEmisor,
    TipoCfdi Tipo,
    string? Folio,
    string? Serie,
    DateTimeOffset FechaCfdi,
    decimal Total,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal? TipoCambio,
    string Moneda,
    CanalOrigenCfdi CanalOrigen,
    DateTimeOffset FechaRecepcion,
    EstadoCfdiRecibido Estado,
    string? MotivoDescarte,
    Guid? CfdiOriginalId,
    Guid? DocumentoDestinoId,
    bool TieneXml,
    bool TienePdf);

public sealed class ObtenerCfdiPorIdHandler
    : IRequestHandler<ObtenerCfdiPorIdQuery, CfdiDetalleResponse>
{
    private readonly CuentasPorPagarDbContext _db;

    public ObtenerCfdiPorIdHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<CfdiDetalleResponse> Handle(
        ObtenerCfdiPorIdQuery query,
        CancellationToken cancellationToken)
    {
        var c = await _db.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_NO_ENCONTRADO",
                $"No se encontró el CFDI con id '{query.Id}'.");

        return new CfdiDetalleResponse(
            Id: c.Id,
            UuidCfdi: c.UuidCfdi.Valor,
            RfcEmisor: c.RfcEmisor.Valor,
            Tipo: c.Tipo,
            Folio: c.Folio,
            Serie: c.Serie,
            FechaCfdi: c.FechaCfdi,
            Total: c.Total,
            Subtotal: c.Subtotal,
            ImpuestosTrasladados: c.ImpuestosTrasladados,
            Retenciones: c.Retenciones,
            TipoCambio: c.TipoCambio,
            Moneda: c.Moneda,
            CanalOrigen: c.CanalOrigen,
            FechaRecepcion: c.FechaRecepcion,
            Estado: c.Estado,
            MotivoDescarte: c.MotivoDescarte,
            CfdiOriginalId: c.CfdiOriginalId,
            DocumentoDestinoId: c.DocumentoDestinoId,
            TieneXml: !string.IsNullOrEmpty(c.XmlBlobRef),
            TienePdf: !string.IsNullOrEmpty(c.PdfBlobRef));
    }
}
