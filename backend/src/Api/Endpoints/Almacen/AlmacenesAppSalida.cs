using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL de la app de Almacenes (F1-MP-07). Resuelve sucursal, artículo A+W y
/// medida (la girada es la misma hoja) y delega en <see cref="SalidaAppAlmacenes"/>. Artículo, medida o sucursal
/// inexistente → ERROR sin mover nada (freno por referencia del lado de la app). Sin existencia → se acepta y queda
/// en revisión. Campos de <c>datos</c> (supuesto, el contrato v1 no los fija): sucursal, articuloAw, ancho/alto
/// (opcionales), piezas o cantidad, costoMxn (opcional; si falta, costo promedio) y comentario.
/// </summary>
public static class AlmacenesAppSalida
{
    public static readonly IReadOnlyDictionary<string, TipoMovimiento> Tipos = new Dictionary<string, TipoMovimiento>
    {
        // Supuesto por validar con Jorge (D1): AJUSTE_SALIDA = consumo del corte (contrato §tabla de mensajes).
        ["AJUSTE_SALIDA"] = TipoMovimiento.SalidaConsumo,
        ["ROTURA"] = TipoMovimiento.BajaPorDano,
        ["SALIDA_MANUAL"] = TipoMovimiento.AjusteNegativo,
    };

    public static async Task<(string Estado, string? Folio, string? Error)> ManejarAsync(
        string tipo, JsonElement datos, AlmacenDbContext db, CompartidoDbContext maestros,
        IUsuarioServicioReadPort usuarioServicio, ICurrentEmpresaContext empresa, CancellationToken ct)
    {
        var sucursal = Texto(datos, "sucursal");
        var articuloAw = datos.TryGetProperty("articuloAw", out var a)
            ? a.ValueKind == JsonValueKind.Number ? a.GetRawText() : a.ValueKind == JsonValueKind.String ? a.GetString() : null
            : null;
        if (string.IsNullOrWhiteSpace(sucursal)) return ("ERROR", null, "datos.sucursal es requerida.");
        if (string.IsNullOrWhiteSpace(articuloAw)) return ("ERROR", null, "datos.articuloAw es requerido.");
        var cantidad = Numero(datos, "piezas") ?? Numero(datos, "cantidad");
        if (cantidad is null or <= 0) return ("ERROR", null, "datos.piezas (o cantidad) debe ser mayor que cero.");
        var ancho = Numero(datos, "ancho");
        var alto = Numero(datos, "alto");
        if (ancho is <= 0 || alto is <= 0 || (ancho is null) != (alto is null))
            return ("ERROR", null, "ancho y alto deben venir juntos y ser mayores que cero, o no venir.");
        var costo = Numero(datos, "costoMxn");
        if (costo is < 0) return ("ERROR", null, "costoMxn no puede ser negativo.");

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return ("ERROR", null, "El usuario de servicio del ERP no está disponible.");

        using var bypass = empresa.Bypass();
        var suc = sucursal.Trim();
        var sucursalId = await maestros.Sucursales.AsNoTracking()
            .Where(x => x.Clave == suc || x.ClaveAw == suc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (sucursalId is null) return ("ERROR", null, $"No existe la sucursal '{suc}'.");

        var clave = articuloAw.Trim();
        var arts = await (from art in maestros.Articulos.AsNoTracking()
                          join p in maestros.ProductosAw.AsNoTracking() on art.ProductoAwId equals p.Id
                          where p.ReferenciaExterna == clave
                          select new { art.Id, art.UnidadMedidaDefault, ProductoAwId = p.Id }).ToListAsync(ct);
        if (arts.Count == 0) return ("ERROR", null, $"No existe un artículo ligado al artículo A+W {clave}.");
        if (arts.Count > 1) return ("ERROR", null, $"El artículo A+W {clave} está ligado a más de un artículo del ERP.");
        var articulo = arts[0];

        Guid? varianteId = null;
        decimal? anchoVar = null, altoVar = null;
        if (ancho is not null)
        {
            var variante = await maestros.ProductosAwVariantes.AsNoTracking()
                .Where(v => v.ProductoAwId == articulo.ProductoAwId
                    && ((v.AnchoMm == ancho && v.AltoMm == alto) || (v.AnchoMm == alto && v.AltoMm == ancho)))
                .Select(v => new { v.Id, v.AnchoMm, v.AltoMm }).FirstOrDefaultAsync(ct);
            if (variante is null)
                return ("ERROR", null, $"No existe la medida {ancho:0.##}x{alto:0.##} del artículo A+W {clave}.");
            (varianteId, anchoVar, altoVar) = (variante.Id, variante.AnchoMm, variante.AltoMm);
        }

        var comentario = Texto(datos, "comentario");
        var motivo = $"App Almacenes {tipo}" + (string.IsNullOrWhiteSpace(comentario) ? "" : $": {comentario.Trim()}");
        return await SalidaAppAlmacenes.RegistrarAsync(db, sp.EmpresaId, sp.Id,
            new SalidaApp(Tipos[tipo], sucursalId.Value, articulo.Id, articulo.UnidadMedidaDefault, varianteId,
                cantidad.Value, costo, motivo.Length > 500 ? motivo[..500] : motivo, anchoVar, altoVar), ct);
    }

    private static string? Texto(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? Numero(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
}
