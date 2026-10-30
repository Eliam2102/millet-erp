using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using static Millet.DatosMaestros.Application.Cargas.CargaMaestrosDatos;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed class ArticulosCarga(CompartidoDbContext db) : IAdaptadorCargaMaestros
{
    public string Catalogo => "articulos";
    public DbContext Db => db;
    public IReadOnlyList<ColumnaCarga> Columnas { get; } = [
        new("Clave", "Obligatoria; clave SAP ItemCode, hasta 20 caracteres.", "item_code", "ItemCode"),
        new("Nombre", "Obligatorio para alta; hasta 254 caracteres."),
        new("Unidad de medida", "Obligatoria para alta; código de unidad activa. PIEZA=PZA, LITRO=L, METRO=M.", "unidad_medida"),
        new("Inventariable", "Y o N; N se carga como Servicio, Y como Estándar.", "invnt_item"),
        new("Código de grupo", "Opcional; grupos 142 a 147 se excluyen por pertenecer a A+W.", "grupo_cod"),
        new("Categoría", "Opcional; nombre de categoría activa (hasta 100 caracteres).", "categoria"),
        new("Última compra", "Opcional; fecha informativa de SAP. No cambia el artículo.", "ultima_compra"),
        new("Clave legacy", "Opcional; si falta, se conserva la clave SAP al crear.", "ClaveLegacy")];
    public IReadOnlyList<string> LlavesArchivo(FilaMaestros fila)
    {
        return new[] { fila.Texto("Clave"), fila.Texto("Clave legacy") }.OfType<string>().Select(x => "clave:" + x).ToList();
    }
    public async Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct)
    {
        var f = fila;
        var clave = f.Requerido("Clave"); var legacy = f.Texto("Clave legacy") ?? clave;
        var p = await db.Articulos.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == clave, ct)
            ?? await db.Articulos.AsNoTracking().SingleOrDefaultAsync(x => x.ClaveLegacy == legacy, ct);
        if (f.Texto("Código de grupo") is { } grupo && (!int.TryParse(grupo, out var g) || g is >= 142 and <= 147))
            throw Error("Código de grupo", "Revise el grupo: los grupos 142 a 147 pertenecen a A+W y se cargan por su sincronización.");
        if (await db.ProductosAw.AnyAsync(x => x.ReferenciaExterna == clave, ct) || await db.Set<ProductoAwComponente>().AnyAsync(x => x.ComponenteRef == clave, ct))
            throw Error("Clave", "Esta clave corresponde a A+W. Use la sincronización de productos.");
        var unidadTexto = f.Texto("Unidad de medida")?.TrimEnd('.').ToUpperInvariant();
        unidadTexto = unidadTexto switch { "PIEZA" => "PZA", "LITRO" => "L", "METRO" => "M", _ => unidadTexto };
        var unidad = unidadTexto is null ? p?.UnidadMedidaId : await db.UnidadesMedida.Where(x => x.Codigo == unidadTexto && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (unidad is null && (p is null || unidadTexto is not null)) throw Error("Unidad de medida", "Elija un código de unidad de medida activa del catálogo.");
        var categoriaTexto = f.Texto("Categoría");
        var categoria = categoriaTexto is null ? p?.CategoriaId : await db.CategoriasArticulo.Where(x => x.Nombre == categoriaTexto && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (categoriaTexto is not null && categoria is null) throw Error("Categoría", "La categoría no existe o está inactiva. Regístrela antes de cargar.");
        var inv = f.Texto("Inventariable");
        var naturaleza = inv switch { null => p?.Naturaleza ?? Naturaleza.Estandar, "Y" => Naturaleza.Estandar, "N" => Naturaleza.Servicio, _ => throw Error("Inventariable", "Use Y para inventariable o N para servicio.") };
        var nombre = f.Texto("Nombre");
        object comando = p is null ? new CrearArticuloCommand(clave, f.Requerido("Nombre"), unidad!.Value, naturaleza, null, categoria, null, null, ClaveLegacy: legacy)
            : new ActualizarArticuloCommand(p.Id, nombre, null, unidadTexto is null ? null : unidad, inv is null ? null : naturaleza, categoriaTexto is null ? null : categoria, null, null, false, false, false);
        return Plan(p, comando, ["clave:" + clave, "legacy:" + legacy, p is null ? "nuevo:" + clave : "id:" + p.Id],
            ("Nombre", p?.Nombre, nombre), ("Unidad de medida", p?.UnidadMedidaId, unidad), ("Naturaleza", p?.Naturaleza, naturaleza), ("Categoría", p?.CategoriaId, categoria));
    }
}
