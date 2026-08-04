using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>
/// Resuelve el snapshot del emisor al emitir un comprobante NUEVO (F12-PR1):
/// RFC y régimen vienen del comando (prellenados por el FE desde
/// emisor-defaults, FAC-UX-PR1); razón social y CP fiscal (LugarExpedicion)
/// se leen del master de la empresa vía <see cref="IEmpresaFiscalReadPort"/>.
/// Falla ANTES de reservar folio si la empresa no tiene CP capturado
/// (<c>EMISOR_SIN_LUGAR_EXPEDICION</c>) — no se queman folios ni timbres.
///
/// <para>Los flujos derivados (NC de una factura, REPP, siguiente tramo) NO
/// usan esto: heredan <c>Comprobante.SnapshotEmisor()</c> del origen.</para>
/// </summary>
public static class EmisorSnapshot
{
    public static async Task<DatosFiscalesEmisor> ResolverAsync(
        IEmpresaFiscalReadPort empresas,
        Guid empresaId,
        string rfcEmisor,
        string regimenFiscalEmisor,
        CancellationToken cancellationToken)
    {
        var lectura = await empresas.ObtenerAsync(empresaId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"La empresa '{empresaId}' no existe o está inactiva.");

        return new DatosFiscalesEmisor(
            Rfc: rfcEmisor,
            Nombre: lectura.RazonSocial,
            RegimenFiscal: regimenFiscalEmisor,
            LugarExpedicion: lectura.CodigoPostal ?? string.Empty).Validar();
    }
}
