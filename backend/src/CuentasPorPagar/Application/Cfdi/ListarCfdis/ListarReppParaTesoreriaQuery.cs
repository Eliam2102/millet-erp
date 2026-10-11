using System.Xml;
using System.Xml.Linq;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;

public sealed record ListarReppParaTesoreriaQuery(Guid FacturaId) : IRequest<IReadOnlyList<CfdiListItemResponse>>;

/// <summary>Lectura documental para el permiso propio de Tesorería, sin dar acceso general a CxP.</summary>
public sealed class ListarReppParaTesoreriaHandler(CuentasPorPagarDbContext db, ICfdiBlobStorage blobs,
    ICurrentUserContext user, ICurrentUserPermissions permisos, Millet.Administracion.Application.Abstractions.IUsuarioSucursalReadPort sucursales)
    : IRequestHandler<ListarReppParaTesoreriaQuery, IReadOnlyList<CfdiListItemResponse>>
{
    public async Task<IReadOnlyList<CfdiListItemResponse>> Handle(ListarReppParaTesoreriaQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var facturas = await db.FacturasProveedor.AsNoTracking().Select(f => new { f.Id, f.UuidCfdi, f.SucursalId }).ToListAsync(ct);
        var factura = facturas.SingleOrDefault(f => f.Id == query.FacturaId)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "No se encontró la factura del proveedor.");
        var corporativo = await permisos.TieneAsync("tesoreria.documentos.leer-todas-sucursales", ct);
        var permitidas = new HashSet<Guid>();
        foreach (var sucursal in facturas.Select(f => f.SucursalId).Distinct())
            if (corporativo || (user.UserId is Guid uid && await sucursales.EstaAsociadoAsync(uid, sucursal, ct))) permitidas.Add(sucursal);
        if (!permitidas.Contains(factura.SucursalId))
            throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tienes acceso a los datos de esta sucursal.");
        var mapa = facturas.Where(f => f.UuidCfdi is not null).GroupBy(f => f.UuidCfdi!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(f => f.SucursalId).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        var candidatos = await db.CfdisRecibidos.AsNoTracking()
            .Where(c => c.Tipo == TipoCfdi.Pago && c.Estado == EstadoCfdiRecibido.PorProcesar && c.XmlBlobRef != null).ToListAsync(ct);
        var resultado = new List<CfdiListItemResponse>();
        foreach (var c in candidatos)
        {
            await using var xml = await blobs.LeerXmlAsync(c.XmlBlobRef!, ct);
            if (xml is null) continue;
            try
            {
                using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var doc = XDocument.Load(reader);
                XNamespace pagos = "http://www.sat.gob.mx/Pagos20";
                var uuids = doc.Descendants(pagos + "DoctoRelacionado").Select(d => (string?)d.Attribute("IdDocumento") ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!uuids.Contains(factura.UuidCfdi, StringComparer.OrdinalIgnoreCase) ||
                    uuids.Any(id => !mapa.TryGetValue(id, out var ramas) || ramas.Any(s => !permitidas.Contains(s)))) continue;
                resultado.Add(new(c.Id, c.UuidCfdi.Valor, c.RfcEmisor.Valor, c.Tipo, c.Folio, c.Serie,
                    c.FechaCfdi, c.Total, c.Subtotal, c.ImpuestosTrasladados, c.Retenciones, c.TipoCambio,
                    c.Moneda, c.CanalOrigen, c.FechaRecepcion, c.Estado));
            }
            catch (XmlException) { /* Documento inválido: no autoriza lectura operativa. */ }
        }
        return resultado.OrderByDescending(c => c.FechaRecepcion).ToArray();
    }
}
