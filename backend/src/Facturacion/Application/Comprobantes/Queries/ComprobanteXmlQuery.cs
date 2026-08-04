using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Comprobantes.Queries;

/// <summary>
/// Familia de comprobante que un endpoint de descarga espera (13-B). El grupo
/// de rutas de cada familia fija el valor para que un id de otra familia no sea
/// descargable con el permiso equivocado (p.ej. una factura de venta vía
/// <c>/anticipos/facturas/{id}/xml</c> con solo <c>anticipos.leer</c>).
/// </summary>
public enum FamiliaComprobante : short
{
    FacturaVenta = 1,
    FacturaAnticipo = 2,
    NotaCredito = 3,
    ReciboPago = 4,
    CartaPorte = 5,
}

/// <summary>
/// Descarga del XML timbrado de cualquier <see cref="Comprobante"/> (13-B —
/// generaliza la query de facturas de venta a las cinco familias). Lee el
/// archivo del repo común (<see cref="ICfdiRepositorioPort"/>) por el
/// <c>CfdiArchivoId</c>. Aplica el alcance de cajas (Capa A): fuera de alcance
/// o de familia distinta a la esperada → mismo 404 que inexistente.
/// </summary>
public sealed record ComprobanteXmlQuery(Guid ComprobanteId, FamiliaComprobante? FamiliaEsperada = null)
    : IRequest<ComprobanteXmlResponse>;

public sealed record ComprobanteXmlResponse(string Xml, string NombreArchivo);

public sealed class ComprobanteXmlHandler : IRequestHandler<ComprobanteXmlQuery, ComprobanteXmlResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly ICfdiRepositorioPort _cfdiRepo;

    public ComprobanteXmlHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance, ICfdiRepositorioPort cfdiRepo)
    {
        _db = db;
        _alcance = alcance;
        _cfdiRepo = cfdiRepo;
    }

    public async Task<ComprobanteXmlResponse> Handle(ComprobanteXmlQuery query, CancellationToken cancellationToken)
    {
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var c = await alcance.AplicarA(_db.Comprobantes.AsNoTracking())
            .FirstOrDefaultAsync(x => x.Id == query.ComprobanteId, cancellationToken);

        if (c is null || !EsDeFamiliaEsperada(c, query.FamiliaEsperada))
            throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{query.ComprobanteId}'.");

        if (c.CfdiArchivoId is not Guid archivoId)
            throw new BusinessRuleException(
                "COMPROBANTE_SIN_XML", "El comprobante no tiene un CFDI timbrado (sin XML).");

        var archivo = await _cfdiRepo.ObtenerAsync(archivoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_ARCHIVO_NO_ENCONTRADO", $"No existe el archivo CFDI {archivoId}.");

        return new ComprobanteXmlResponse(archivo.XmlContenido, $"{c.Folio}.xml");
    }

    internal static bool EsDeFamiliaEsperada(Comprobante c, FamiliaComprobante? esperada) => esperada switch
    {
        null => true,
        FamiliaComprobante.FacturaVenta => c is FacturaVenta,
        FamiliaComprobante.FacturaAnticipo => c is FacturaAnticipo,
        FamiliaComprobante.NotaCredito => c is NotaCredito,
        FamiliaComprobante.ReciboPago => c is ReciboPago,
        FamiliaComprobante.CartaPorte => c is Domain.CartaPorte.CartaPorte,
        _ => false,
    };
}
