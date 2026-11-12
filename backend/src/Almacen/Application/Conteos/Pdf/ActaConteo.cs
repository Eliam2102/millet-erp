using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Documentos;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Unit = QuestPDF.Infrastructure.Unit;

namespace Millet.Almacen.Application.Conteos.Pdf;

public sealed record RenglonActaConteo(string Clave, string Descripcion, string Ubicacion, decimal Teorico, decimal Contado,
    decimal Diferencia, string? FolioAjuste, decimal SaldoResultante);

public sealed record ActaConteoImpresion(EmpresaEncabezado? Empresa, string Numero, string Tipo, DateOnly Fecha, DateTimeOffset? AplicadoAt,
    IReadOnlyList<RenglonActaConteo> Renglones, IReadOnlyList<string> Contaron, string? Aprobo);

public sealed record ActaConteoPdf(string NombreArchivo, byte[] Contenido);

/// <summary>
/// M-224: acta del conteo aplicado. Por partida: lo contado, el teórico congelado, la diferencia, el folio del ajuste y el saldo que quedó
/// (siempre igual a lo contado: un conteo que no cuadra no se aplica). Firmas de quienes contaron y de quien aprobó. null = no existe o
/// todavía no se aplica.
/// </summary>
public sealed record ActaConteoPdfQuery(Guid ConteoId) : IRequest<ActaConteoPdf?>;

public sealed class ActaConteoPdfHandler(AlmacenDbContext db, IArticuloReadPort articulos, IUsuarioReadPort usuarios, IEmpresaImpresionReadPort empresas)
    : IRequestHandler<ActaConteoPdfQuery, ActaConteoPdf?>
{
    public async Task<ActaConteoPdf?> Handle(ActaConteoPdfQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var c = await db.Set<ConteoInventario>().AsNoTracking().Include(x => x.Lineas).FirstOrDefaultAsync(x => x.Id == request.ConteoId, ct);
        if (c is null || c.Estado != EstadoConteo.Aplicado) return null;
        var lineas = c.Lineas.Where(l => l.CantidadAplicada is not null).ToList();
        var arts = await articulos.ObtenerPorIdsAsync(lineas.Select(l => l.ArticuloId).Distinct().ToArray(), ct);
        var ubicacionIds = lineas.Select(l => l.UbicacionId).Distinct().ToList();
        var ubicaciones = await db.Ubicaciones.AsNoTracking().Where(u => ubicacionIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Clave, ct);
        var personas = lineas.Where(l => l.CapturadoPor is not null).Select(l => l.CapturadoPor!.Value)
            .Concat(c.AprobadorId is { } a ? [a] : []).Distinct().ToArray();
        var nombres = await usuarios.ObtenerNombresAsync(personas, ct);
        string Nombre(Guid id) => nombres.GetValueOrDefault(id) ?? id.ToString();

        var numero = Numero(c.Id);
        var renglones = lineas.OrderBy(l => ubicaciones.GetValueOrDefault(l.UbicacionId)).ThenBy(l => arts.GetValueOrDefault(l.ArticuloId)?.Clave)
            .Select(l =>
            {
                var a = arts.GetValueOrDefault(l.ArticuloId);
                var descripcion = l.Lote is null ? a?.Descripcion ?? "" : $"{a?.Descripcion}\nLote {l.Lote}";
                return new RenglonActaConteo(a?.Clave ?? l.ArticuloId.ToString(), descripcion, ubicaciones.GetValueOrDefault(l.UbicacionId) ?? "",
                    l.CantidadTeorica, l.CantidadAplicada!.Value, l.CantidadAplicada.Value - l.CantidadTeorica, l.FolioAjuste, l.SaldoResultante!.Value);
            }).ToList();
        var impresion = new ActaConteoImpresion(await empresas.ObtenerAsync(c.EmpresaId, ct), numero, c.Tipo.ToString(), c.FechaPlanificada,
            c.FechaCierre, renglones, lineas.Where(l => l.CapturadoPor is not null).Select(l => Nombre(l.CapturadoPor!.Value)).Distinct().ToList(),
            c.AprobadorId is { } ap ? Nombre(ap) : null);
        return new ActaConteoPdf($"Acta-{numero}.pdf", ActaConteoDocumento.Generar(impresion));
    }

    /// <summary>Número legible del conteo (el conteo no tiene folio propio): CNT + los últimos 8 del id.</summary>
    public static string Numero(Guid id) => $"CNT-{id.ToString("N")[^8..].ToUpperInvariant()}";
}

public static class ActaConteoDocumento
{
    private static string N(decimal v) => v.ToString("#,##0.####", CultureInfo.InvariantCulture);

    public static byte[] Generar(ActaConteoImpresion a) => Document.Create(documento => documento.Page(pagina =>
    {
        PdfEstilos.ConfigurarPagina(pagina);
        pagina.Header().PaddingBottom(8).Row(fila =>
        {
            fila.Spacing(12);
            if (a.Empresa?.Logo is { Length: > 0 } logo)
                fila.ConstantItem(PdfEstilos.LogoAnchoCm, Unit.Centimetre).MaxHeight(2.5f, Unit.Centimetre).Image(logo).FitArea();
            fila.RelativeItem().Column(e =>
            {
                if (a.Empresa is null) return;
                e.Item().Text(a.Empresa.RazonSocial).Bold();
                foreach (var linea in a.Empresa.DomicilioLineas) e.Item().Text(linea);
            });
            fila.ConstantItem(8.5f, Unit.Centimetre).Column(d =>
            {
                d.Spacing(3);
                d.Item().AlignRight().BorderLeft(4).BorderColor(PdfEstilos.GrisBarra).PaddingLeft(5).Text("ACTA DE CONTEO FISICO")
                    .FontSize(PdfEstilos.Titulo).Bold();
                d.Item().AlignRight().Text($"Conteo {a.Numero} · {a.Tipo}").Bold();
                d.Item().AlignRight().Text($"Fecha del conteo: {a.Fecha:dd/MM/yyyy}");
                if (a.AplicadoAt is { } ap) d.Item().AlignRight().Text($"Aplicado: {ap.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm}");
            });
        });
        pagina.Content().Column(contenido =>
        {
            contenido.Spacing(10);
            contenido.Item().Component(new TablaRenglones<RenglonActaConteo>(a.Renglones,
            [
                new("Clave", r => r.Clave, 1.5f),
                new("Descripción", r => r.Descripcion, 3.6f),
                new("Ubicación", r => r.Ubicacion, 1.8f),
                new("Teórico", r => N(r.Teorico), 1.1f, AlineacionColumna.Derecha),
                new("Contado", r => N(r.Contado), 1.1f, AlineacionColumna.Derecha),
                new("Diferencia", r => N(r.Diferencia), 1.1f, AlineacionColumna.Derecha),
                new("Ajuste", r => r.FolioAjuste ?? "—", 1.7f, AlineacionColumna.Centro),
                new("Saldo final", r => N(r.SaldoResultante), 1.1f, AlineacionColumna.Derecha),
                new("Dif. final", r => N(r.SaldoResultante - r.Contado), 1f, AlineacionColumna.Derecha),
            ]));
            contenido.Item().Text($"{a.Renglones.Count} partidas · {a.Renglones.Count(r => r.FolioAjuste is not null)} ajustadas. " +
                "Después de aplicar, el saldo de cada partida quedó igual a lo contado.");
            contenido.Item().ShowEntire().PaddingTop(16).Row(fila =>
            {
                fila.Spacing(24);
                fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Contó: nombre y firma", string.Join(", ", a.Contaron)));
                fila.RelativeItem().AlignBottom().Element(x => Firma(x, "Aprobó: nombre y firma", a.Aprobo));
            });
        });
        pagina.Footer().AlignRight().Text(t => { t.Span("Página "); t.CurrentPageNumber(); t.Span("/"); t.TotalPages(); });
    })).GeneratePdf();

    private static void Firma(IContainer container, string etiqueta, string? nombre) => container.Column(f =>
    {
        f.Item().PaddingTop(28).AlignCenter().Text(nombre ?? "").FontSize(PdfEstilos.Detalle);
        f.Item().BorderTop(0.75f).PaddingTop(2).AlignCenter().Text(etiqueta).Bold();
    });
}
