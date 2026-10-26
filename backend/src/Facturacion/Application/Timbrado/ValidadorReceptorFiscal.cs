using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Timbrado;

public sealed class ValidadorReceptorFiscal(
    IClientesReadPort clientes, ICatalogosSatReadPort catalogos, FacturacionDbContext db)
{
    public async Task ValidarAsync(DatosFiscalesReceptor receptor, DatosFiscalesEmisor emisor,
        Guid? clienteId, CancellationToken ct, bool esRep = false,
        bool exportacionConCce = false, string? numRegIdTrib = null, string? paisResidencia = null)
    {
        var errores = new List<CampoFiscalInvalido>();
        if (clienteId is { } id)
        {
            var maestro = await clientes.ObtenerAsync(id, ct);
            if (maestro is null)
                errores.Add(new("clienteId", "El cliente no existe en el maestro fiscal."));
            else
            {
                Comparar("rfc", "RFC", maestro.Rfc, receptor.Rfc);
                if (string.IsNullOrWhiteSpace(maestro.RazonSocial)) errores.Add(new("razonSocial", "Falta la razón social en el maestro del cliente."));
                Comparar("regimenFiscal", "régimen fiscal", maestro.RegimenFiscal, receptor.RegimenFiscal);
                Comparar("codigoPostalFiscal", "CP fiscal", maestro.CodigoPostalFiscal, receptor.CodigoPostal);
            }
        }
        var uso = esRep ? "CP01" : ValidacionReceptorFiscal.Normalizar(receptor.UsoCfdi);
        var regimenExiste = await catalogos.ExisteRegimenFiscalAsync(ValidacionReceptorFiscal.Normalizar(receptor.RegimenFiscal), ct);
        var usoExiste = await catalogos.ExisteUsoCfdiAsync(uso, ct);
        errores.AddRange(ValidacionReceptorFiscal.Validar(receptor, emisor.LugarExpedicion,
            regimenExiste, usoExiste, esRep, exportacionConCce, numRegIdTrib, paisResidencia));
        if (errores.Count > 0) throw new ReceptorFiscalInvalidoException(errores.Distinct().ToArray(), clienteId);

        void Comparar(string campo, string nombre, string? valorMaestro, string valor)
        {
            if (string.IsNullOrWhiteSpace(valorMaestro)) errores.Add(new(campo, $"Falta el {nombre} en el maestro del cliente."));
            else if (ValidacionReceptorFiscal.Normalizar(valorMaestro) != ValidacionReceptorFiscal.Normalizar(valor))
                errores.Add(new(campo, $"El {nombre} difiere del maestro del cliente, que es la fuente fiscal; corrige el cliente y recarga el documento."));
        }
    }

    public async Task ValidarComprobanteAsync(Comprobante comprobante, CancellationToken ct, bool esRep = false)
    {
        if (comprobante is Domain.CartaPorte.CartaPorte) return;
        var clienteId = await ResolverClienteAsync(comprobante, ct);
        var factura = comprobante as FacturaVenta;
        if (factura is { ComportamientoFiscal: ComportamientoFiscal.ExportacionConCce, ComplementoCce: null }
            && db.Entry(factura).State != EntityState.Detached)
            await db.Entry(factura).Reference(f => f.ComplementoCce).LoadAsync(ct);
        await ValidarAsync(Snapshot(comprobante), comprobante.SnapshotEmisor(), clienteId, ct,
            esRep || comprobante is ReciboPago,
            factura?.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce,
            factura?.ComplementoCce?.ReceptorNumRegIdTrib, factura?.ComplementoCce?.ReceptorPaisResidencia);
    }

    private async Task<Guid?> ResolverClienteAsync(Comprobante comprobante, CancellationToken ct)
    {
        switch (comprobante)
        {
            case FacturaVenta { PedidoFacturableId: { } pedidoId }:
                return await db.PedidosFacturables.Where(p => p.Id == pedidoId).Select(p => (Guid?)p.ClienteId).SingleOrDefaultAsync(ct);
            case FacturaAnticipo anticipo:
                return await db.Anticipos.Where(a => a.FacturaAnticipoId == anticipo.Id).Select(a => (Guid?)a.ClienteId).SingleOrDefaultAsync(ct);
            case NotaCredito { FacturaRelacionadaId: { } facturaId }:
                var origen = await db.FacturasVenta.SingleOrDefaultAsync(f => f.Id == facturaId, ct);
                return origen is null ? null : await ResolverClienteAsync(origen, ct);
            case ReciboPago rep:
                var origenId = await db.RecibosPago.Where(r => r.Id == rep.Id)
                    .SelectMany(r => r.FacturasPagadas).Select(f => f.FacturaVentaId).FirstOrDefaultAsync(ct);
                var factura = await db.FacturasVenta.SingleOrDefaultAsync(f => f.Id == origenId, ct);
                return factura is null ? null : await ResolverClienteAsync(factura, ct);
            default: return null;
        }
    }

    private static DatosFiscalesReceptor Snapshot(Comprobante c) => new(
        c.ReceptorRfc, c.ReceptorNombre, c.ReceptorRegimenFiscal, c.ReceptorCodigoPostal,
        c.ReceptorUsoCfdi, c.ReceptorPais, c.ReceptorEsGenerico);
}
