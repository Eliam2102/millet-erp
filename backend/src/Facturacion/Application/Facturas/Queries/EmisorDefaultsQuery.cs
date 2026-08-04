using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.Queries;

/// <summary>
/// Defaults del emisor para el formulario de emisión de CFDI (FAC-UX-PR1,
/// cierra PLATFORM-TODO(&lt;EmisorDefaults&gt;)): RFC, razón social y régimen
/// fiscal de la empresa del contexto + la sucursal única activa si aplica
/// (MVP sucursal única; con &gt;1 activa el usuario elige en el formulario).
/// </summary>
public sealed record EmisorDefaultsQuery : IRequest<EmisorDefaultsResponse>;

/// <param name="TasaIvaDefault">
/// Tasa de IVA default de la empresa (fracción, FAC-DET-PR2) para líneas de
/// captura manual sin tasa del artículo. Extensión aditiva — <c>null</c> si
/// la empresa no la configura (el FE conserva su fallback local).
/// </param>
/// <param name="CodigoPostalEmisor">
/// CP fiscal de la empresa (LugarExpedicion del CFDI, F12-PR1). <c>null</c> =
/// sin capturar; el FE puede advertir que la emisión fallará hasta capturarlo.
/// </param>
public sealed record EmisorDefaultsResponse(
    string RfcEmisor,
    string RazonSocialEmisor,
    string RegimenFiscalEmisor,
    Guid? SucursalIdDefault,
    decimal? TasaIvaDefault,
    string? CodigoPostalEmisor);

public sealed class EmisorDefaultsHandler
    : IRequestHandler<EmisorDefaultsQuery, EmisorDefaultsResponse>
{
    private readonly IEmpresaFiscalReadPort _empresas;
    private readonly ICurrentEmpresaContext _empresa;

    public EmisorDefaultsHandler(IEmpresaFiscalReadPort empresas, ICurrentEmpresaContext empresa)
    {
        _empresas = empresas;
        _empresa = empresa;
    }

    public async Task<EmisorDefaultsResponse> Handle(
        EmisorDefaultsQuery query, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var emisor = await _empresas.ObtenerAsync(empresaId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"La empresa '{empresaId}' no existe o está inactiva.");

        var sucursalDefault = await _empresas.ObtenerSucursalUnicaActivaAsync(cancellationToken);

        return new EmisorDefaultsResponse(
            emisor.Rfc, emisor.RazonSocial, emisor.RegimenFiscal, sucursalDefault,
            emisor.TasaIvaDefault, emisor.CodigoPostal);
    }
}
