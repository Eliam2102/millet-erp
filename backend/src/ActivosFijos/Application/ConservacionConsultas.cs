using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
namespace Millet.ActivosFijos.Application;
public sealed record VencimientoDato(Guid Id, Guid ActivoId, string Numero, string Descripcion, Guid ResponsableId, Guid SucursalId, string Referencia, DateOnly Vence, bool EsMantenimiento);
public static class ConservacionConsultas
{
    public static IQueryable<VencimientoDato> PorVencer(ActivosFijosDbContext db, IReadOnlyList<Guid>? ids, DateOnly hasta)
    {
        var activos = db.Activos.Where(x => x.Estado != EstadoActivo.DadoDeBaja && (ids == null || ids.Contains(x.SucursalId)));
        var documentos = from d in db.Documentos join a in activos on d.ActivoId equals a.Id
            where !d.Reemplazado && (d.Tipo == TipoDocumentoActivo.Seguro || d.Tipo == TipoDocumentoActivo.Garantia || d.Tipo == TipoDocumentoActivo.Arrendamiento) && d.VigenteHasta <= hasta
            select new { d.Id, ActivoId = a.Id, a.Numero, a.Descripcion, a.ResponsableId, a.SucursalId, Referencia = d.Folio, Vence = d.VigenteHasta!.Value, EsMantenimiento = false };
        var mantenimientos = from h in db.Mantenimientos join a in activos on h.ActivoId equals a.Id where h.Proximo <= hasta
            select new { h.Id, ActivoId = a.Id, a.Numero, a.Descripcion, a.ResponsableId, a.SucursalId, Referencia = h.Descripcion, Vence = h.Proximo!.Value, EsMantenimiento = true };
        return documentos.Concat(mantenimientos).OrderBy(x => x.Vence).ThenBy(x => x.Numero)
            .Select(x => new VencimientoDato(x.Id, x.ActivoId, x.Numero, x.Descripcion, x.ResponsableId, x.SucursalId, x.Referencia, x.Vence, x.EsMantenimiento));
    }
}
