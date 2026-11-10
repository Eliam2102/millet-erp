using Millet.SharedKernel.Application.Adjuntos;
using MediatR;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Portal;

/// <summary>
/// El proveedor sube su factura (tipo I) o su complemento de pago (tipo P) desde el portal.
/// <see cref="ProveedorId"/> sale del token del portal, nunca del cliente. El XML se valida ANTES de
/// guardar nada (RFC emisor = RFC del proveedor y tipo I/P); después sigue el mismo flujo que los
/// demás canales vía <see cref="IngresarCfdiCommand"/> con canal <see cref="CanalOrigenCfdi.PortalProveedor"/>.
/// </summary>
public sealed record SubirCfdiPortalCommand(Guid ProveedorId, byte[] Xml, byte[]? Pdf) : IRequest<SubirCfdiPortalResponse>;

/// <param name="Resultado"><c>Recibido</c> o <c>YaLoTeniamos</c> (UUID ya registrado: no se duplica).</param>
public sealed record SubirCfdiPortalResponse(string Resultado, string Uuid, string Mensaje, Guid? CfdiId);

public sealed class SubirCfdiPortalHandler(IXmlCfdiParser parser, IProveedorReadPort proveedores, ISender mediator, IReppPortalPort repp)
    : IRequestHandler<SubirCfdiPortalCommand, SubirCfdiPortalResponse>
{
    public const int MaxXmlBytes = 2 * 1024 * 1024;
    public const int MaxPdfBytes = 10 * 1024 * 1024;

    public async Task<SubirCfdiPortalResponse> Handle(SubirCfdiPortalCommand request, CancellationToken cancellationToken)
    {
        if (request.Xml.Length == 0)
            throw new BusinessRuleException("PORTAL_XML_REQUERIDO", "Sube el archivo XML de tu CFDI.");
        if (request.Xml.Length > MaxXmlBytes)
            throw new BusinessRuleException("PORTAL_XML_MUY_GRANDE", "El XML excede el tamaño máximo permitido (2 MB).");
        if (request.Pdf is { Length: > MaxPdfBytes })
            throw new BusinessRuleException("PORTAL_PDF_MUY_GRANDE", "El PDF excede el tamaño máximo permitido (10 MB).");
        if (request.Pdf is { Length: > 0 } pdf && !AdjuntosPoliticaOptions.EsPdfValido(pdf))
            throw new BusinessRuleException("PORTAL_PDF_INVALIDO", "Adjunta un PDF completo, sin contraseña y con al menos una página.");

        var proveedor = await proveedores.ObtenerAsync(request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PORTAL_PROVEEDOR_NO_ENCONTRADO", "No encontramos el proveedor.");

        DatosCfdiParseados datos;
        try
        {
            using var ms = new MemoryStream(request.Xml, writable: false);
            datos = parser.Parsear(ms);
        }
        catch (CfdiParseException ex)
        {
            throw new BusinessRuleException("PORTAL_XML_INVALIDO", $"No pudimos leer tu XML: {ex.Message}");
        }

        if (!string.Equals(datos.RfcEmisor?.Trim(), proveedor.Rfc.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("PORTAL_RFC_NO_COINCIDE",
                "El RFC del emisor del CFDI no coincide con el de tu empresa. No se guardó el documento.");
        if (datos.Tipo is not (TipoCfdi.Ingreso or TipoCfdi.Pago))
            throw new BusinessRuleException("PORTAL_TIPO_NO_ACEPTADO",
                "Solo se aceptan facturas (tipo Ingreso) y complementos de pago (tipo Pago).");

        try
        {
            using var xml = new MemoryStream(request.Xml, writable: false);
            using var pdfStream = request.Pdf is { Length: > 0 } p ? new MemoryStream(p, writable: false) : null;
            var r = await mediator.Send(new IngresarCfdiCommand(xml, pdfStream, CanalOrigenCfdi.PortalProveedor), cancellationToken);
            return new("Recibido", r.UuidCfdi, await Mensaje("Recibimos tu documento.", datos, request, cancellationToken), r.Id);
        }
        catch (BusinessRuleException ex) when (ex.Code == "CFDI_DUPLICADO")
        {
            // El dedupe es por UUID y el RFC emisor ya coincide con el del proveedor: no se revela nada ajeno.
            // Un complemento ya recibido se vuelve a intentar ligar (p. ej. se subió antes de que Tesorería registrara el pago).
            return new("YaLoTeniamos", datos.UuidCfdi, await Mensaje("Ya lo teníamos.", datos, request, cancellationToken), null);
        }
    }

    /// <summary>Complemento de pago (tipo P): se liga solo a los pagos aplicados y se le dice al proveedor el resultado.</summary>
    private async Task<string> Mensaje(string recibido, DatosCfdiParseados datos, SubirCfdiPortalCommand request, CancellationToken cancellationToken)
    {
        if (datos.Tipo != TipoCfdi.Pago) return recibido;
        var r = await repp.LigarAsync(request.ProveedorId, request.Xml, cancellationToken);
        return $"{recibido} {r.Mensaje}";
    }

}
