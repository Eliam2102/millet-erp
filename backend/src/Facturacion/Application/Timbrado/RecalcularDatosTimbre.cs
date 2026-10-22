using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>Mantenimiento explícito por comprobante, sin endpoint público ni ejecución automática.</summary>
public sealed record RecalcularDatosTimbreCommand(Guid ComprobanteId) : IRequest;

public sealed class RecalcularDatosTimbreHandler(FacturacionDbContext db, ICfdiRepositorioPort archivos)
    : IRequestHandler<RecalcularDatosTimbreCommand>
{
    public async Task Handle(RecalcularDatosTimbreCommand command, CancellationToken cancellationToken)
    {
        var comprobante = await db.Comprobantes.FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", "Selecciona un comprobante existente para recuperar los datos del timbre.");
        var archivo = comprobante.CfdiArchivoId is Guid id
            ? await archivos.ObtenerAsync(id, cancellationToken) : null;
        if (archivo is null)
            throw new BusinessRuleException("XML_TIMBRADO_REQUERIDO", "Recupera el XML timbrado del comprobante antes de recalcular sus datos.");
        if (Domain.Comprobantes.CadenaOriginalTimbre.Desde(archivo.XmlContenido) is null
            || Domain.Comprobantes.CadenaOriginalTimbre.CertificadoEmisorDesde(archivo.XmlContenido) is null)
            throw new BusinessRuleException("XML_TIMBRADO_INCOMPLETO", "El XML no contiene los datos completos del timbre y del certificado; recupera el archivo correcto del PAC.");
        comprobante.RecalcularDatosTimbre(archivo.XmlContenido);
        await db.SaveChangesAsync(cancellationToken);
    }
}
