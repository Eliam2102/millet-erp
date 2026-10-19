using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.RespuestaProveedor;

internal static class RespuestaProveedorMapper
{
    public static RespuestaProveedorOcResponse ToResponse(RespuestaProveedorOc r, bool vigente) =>
        new(r.Id, r.Numero, r.Tipo, r.FechaRespuesta, r.Evidencia, r.ContactoProveedor, r.Motivo,
            r.TieneDiferencias, r.RegistradaPorId, r.RegistradaEn, vigente,
            r.Compromisos.OrderBy(c => c.FechaPrometida)
                .Select(c => new CompromisoEntregaOcResponse(c.Id, c.LineaOrdenCompraId, c.Cantidad, c.FechaPrometida))
                .ToList());
}
