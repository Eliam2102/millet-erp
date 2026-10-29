using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Salidas.Pdf;

public sealed record ComprobanteSalidaPdf(string NombreArchivo, byte[] Contenido);

/// <summary>Comprobante de salida (con RQ o por vale) en el formato M2 de Millet; null = no existe.</summary>
public sealed record ComprobanteSalidaPdfQuery(Guid Id) : IRequest<ComprobanteSalidaPdf?>;

public sealed class ComprobanteSalidaPdfHandler(
    AlmacenDbContext db, ISender sender, IEmpresaImpresionReadPort empresas, IUsuarioReadPort usuarios)
    : IRequestHandler<ComprobanteSalidaPdfQuery, ComprobanteSalidaPdf?>
{
    public async Task<ComprobanteSalidaPdf?> Handle(ComprobanteSalidaPdfQuery request, CancellationToken cancellationToken)
    {
        // Mismos datos (y nombres resueltos) que el detalle en pantalla.
        var s = await sender.Send(new ObtenerSalidaPorIdQuery(request.Id), cancellationToken);
        if (s is null) return null;

        var empresaId = await db.Movimientos.AsNoTracking().Where(m => m.Id == s.Id)
            .Select(m => m.EmpresaId).FirstAsync(cancellationToken);
        string? elaboro = null;
        if (s.RegistradoPor is { } registro)
            (await usuarios.ObtenerNombresAsync([registro], cancellationToken)).TryGetValue(registro, out elaboro);

        var impresion = new ComprobanteSalidaImpresion(
            await empresas.ObtenerAsync(empresaId, cancellationToken),
            s.Folio, s.FechaMovimiento,
            s.RegistradoAt is { } r ? FechaContable.HoyLocal(r) : null,
            s.Lineas.OrderBy(l => l.Posicion).Select(l => new RenglonSalidaImpresion(
                l.Posicion, l.ArticuloClave ?? "", Descripcion(l), l.UnidadMedida, l.Cantidad)).ToList(),
            Comentarios(s), elaboro, s.PersonaDestinatariaNombre);
        return new ComprobanteSalidaPdf($"{s.Folio}.pdf", ComprobanteSalidaDocumento.Generar(impresion));
    }

    private static string Descripcion(SalidaLineaItem l)
    {
        var partes = new List<string?> { l.ArticuloDescripcion };
        if (l.AnchoMm is { } ancho && l.AltoMm is { } alto) partes.Add($"{ancho:0.##} × {alto:0.##} mm");
        partes.Add(l.ComentarioLinea);
        return string.Join("\n", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Como en el M2: «RQ: C00195865 AC SE ENTREGA A JULIO TUYUB - EPP COTTOLENGO».</summary>
    public static string Comentarios(SalidaDetalle s)
    {
        var rq = s.EsPorVale
            ? "VALE URGENTE" + (s.RqRegularizadoraFolio is { } reg ? $" · RQ: {reg}" : "")
            : s.RqFolio is { } folio ? $"RQ: {folio}" : null;
        var entrega = s.PersonaDestinatariaNombre is { } persona ? $"SE ENTREGA A {persona.ToUpperInvariant()}" : null;
        return string.Join(" ", new[] { rq, s.Observaciones, entrega }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
