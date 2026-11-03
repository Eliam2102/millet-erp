using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Cargas;
using static Millet.DatosMaestros.Application.Cargas.CargaMaestrosDatos;

namespace Millet.Api.Endpoints.DatosMaestros.Cargas;

public sealed class CentrosCostoCarga(CentrosCostoDbContext db, int dimension) : IAdaptadorCargaMaestros
{
    public string Catalogo => "centros-costo-dim" + dimension;
    public DbContext Db => db;
    public IReadOnlyList<ColumnaCarga> Columnas => dimension == 1 ? Basicas : [.. Basicas,
        new("Padre", "Obligatorio para alta; clave del nivel anterior activo. No se permite cambiar el padre."),
        new("Grupo", "Obligatorio para alta; nombre del grupo activo de esta dimensión.")];
    private static readonly ColumnaCarga[] Basicas = [new("Clave", "Obligatoria; hasta 10 caracteres; única dentro de la dimensión."), new("Nombre", "Obligatorio para alta; hasta 254 caracteres.")];
    public IReadOnlyList<string> LlavesArchivo(FilaMaestros fila) => fila.Texto("Clave") is { } clave ? ["clave:" + clave] : [];
    public async Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct)
    {
        var f = fila;
        var clave = f.Requerido("Clave"); var nombre = f.Texto("Nombre");
        if (dimension == 1)
        {
            var p = await db.Dim1s.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == clave, ct);
            object comando = p is null ? new CrearDim1Command(clave, f.Requerido("Nombre")) : new EditarDim1Command(p.Id, p.Version, clave, nombre ?? p.Nombre);
            return Plan(p, comando, ["clave:" + clave], ("Nombre", p?.Nombre, nombre));
        }
        var padre = f.Texto("Padre"); var grupo = f.Texto("Grupo");
        if (dimension == 2)
        {
            var p = await db.Dim2s.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == clave, ct);
            var padreId = padre is null ? p?.Dim1Id : await db.Dim1s.Where(x => x.Clave == padre && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            var grupoId = grupo is null ? p?.GrupoDim2Id : await db.GruposDim2.Where(x => x.Nombre == grupo && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            ValidarReferencias(padreId, grupoId, p?.Dim1Id);
            object comando = p is null ? new CrearDim2Command(padreId!.Value, clave, f.Requerido("Nombre"), grupoId!.Value) : new EditarDim2Command(p.Id, p.Version, clave, nombre ?? p.Nombre, grupoId!.Value);
            return Plan(p, comando, ["clave:" + clave], ("Nombre", p?.Nombre, nombre), ("Grupo", p?.GrupoDim2Id, grupoId));
        }
        else
        {
            var p = await db.Dim3s.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == clave, ct);
            var padreId = padre is null ? p?.Dim2Id : await db.Dim2s.Where(x => x.Clave == padre && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            var grupoId = grupo is null ? p?.GrupoDim3Id : await db.GruposDim3.Where(x => x.Nombre == grupo && x.Estatus == EstatusCatalogo.Activo).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            ValidarReferencias(padreId, grupoId, p?.Dim2Id);
            object comando = p is null ? new CrearDim3Command(padreId!.Value, clave, f.Requerido("Nombre"), grupoId!.Value) : new EditarDim3Command(p.Id, p.Version, clave, nombre ?? p.Nombre, grupoId!.Value);
            return Plan(p, comando, ["clave:" + clave], ("Nombre", p?.Nombre, nombre), ("Grupo", p?.GrupoDim3Id, grupoId));
        }
    }
    private static void ValidarReferencias(Guid? padre, Guid? grupo, Guid? actual)
    {
        if (padre is null) throw Error("Padre", "Capture la clave de un padre activo del nivel anterior.");
        if (grupo is null) throw Error("Grupo", "Capture el nombre de un grupo activo de esta dimensión.");
        if (actual is not null && padre != actual) throw Error("Padre", "No se permite cambiar el padre de un centro de costo existente.");
    }
}
