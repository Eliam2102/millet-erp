using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Recepciones.Pdf;

public sealed record ComprobanteRecepcionPdf(string NombreArchivo, byte[] Contenido);

/// <summary>Comprobante imprimible de una recepción (G-10); null = no existe.</summary>
public sealed record ComprobanteRecepcionPdfQuery(Guid Id) : IRequest<ComprobanteRecepcionPdf?>;

public sealed class ComprobanteRecepcionPdfHandler(
    AlmacenDbContext db, ISender sender, IEmpresaImpresionReadPort empresas, IComprasOcReadPort ordenes, IProveedorReadPort proveedores)
    : IRequestHandler<ComprobanteRecepcionPdfQuery, ComprobanteRecepcionPdf?>
{
    public async Task<ComprobanteRecepcionPdf?> Handle(ComprobanteRecepcionPdfQuery request, CancellationToken cancellationToken)
    {
        // Mismos datos (y nombres resueltos) que el detalle en pantalla.
        var r = await sender.Send(new ObtenerRecepcionPorIdQuery(request.Id), cancellationToken);
        if (r is null) return null;

        var empresaId = await db.Movimientos.AsNoTracking().Where(m => m.Id == r.Id).Select(m => m.EmpresaId).FirstAsync(cancellationToken);
        var oc = r.OrdenCompraId is { } ocId ? await ordenes.ObtenerAsync(ocId, cancellationToken) : null;
        var proveedor = oc is null ? null : await proveedores.ObtenerAsync(oc.ProveedorId, cancellationToken);

        var impresion = new ComprobanteRecepcionImpresion(
            await empresas.ObtenerAsync(empresaId, cancellationToken), r.Folio, r.FechaMovimiento,
            r.OrdenCompraFolio ?? "", proveedor?.RazonSocial ?? "",
            r.Lineas.OrderBy(l => l.Posicion).Select(l => new RenglonRecepcionImpresion(l.Posicion, l.ArticuloClave ?? "", Descripcion(l),
                l.UnidadCapturada ?? l.UnidadMedida, l.CantidadCapturada ?? l.Cantidad, l.UbicacionClave ?? "")).ToList(),
            (r.Rechazos ?? []).Select(x => new RenglonRechazoImpresion(x.ArticuloClave ?? "", x.ArticuloDescripcion ?? "", x.UnidadMedida, x.Cantidad, x.Motivo)).ToList(),
            Comentarios(r), r.RegistradoPorNombre, r.RecibidoPorNombre ?? r.RegistradoPorNombre);
        return new ComprobanteRecepcionPdf($"{r.Folio}.pdf", ComprobanteRecepcionDocumento.Generar(impresion));
    }

    private static string Descripcion(RecepcionLineaItem l)
    {
        var partes = new List<string?> { l.ArticuloDescripcion };
        if (l.AnchoMm is { } ancho && l.AltoMm is { } alto) partes.Add($"{ancho:0.##} × {alto:0.##} mm");
        if (!string.IsNullOrWhiteSpace(l.Lote)) partes.Add($"Lote {l.Lote}");
        if (l.EnRevision) partes.Add($"EN REVISIÓN (dañado): {l.MotivoDano}");
        return string.Join("\n", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static string Comentarios(RecepcionDetalle r)
    {
        var remision = string.IsNullOrWhiteSpace(r.Remision) ? null : $"REMISIÓN: {r.Remision}";
        return string.Join(" ", new[] { remision, r.Observaciones }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
