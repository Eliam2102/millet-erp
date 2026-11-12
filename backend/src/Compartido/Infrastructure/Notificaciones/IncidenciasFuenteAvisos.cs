using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Infrastructure.Notificaciones;

public sealed class IncidenciasFuenteAvisos(CompartidoDbContext db) : IFuenteAvisosUsuario
{
    public string Nombre => "incidencias";

    public async Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
    {
        var q = db.Incidencias.AsNoTracking().Include(x => x.Historial)
            .Where(x => x.EmpresaId == contexto.EmpresaId && x.ResponsableUsuarioId == contexto.UsuarioId &&
                (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion));
        if (contexto.SucursalId is { } sid) q = q.Where(x => x.SucursalId == null || x.SucursalId == sid);
        var filas = await q.ToListAsync(ct);
        return filas.Select(x =>
        {
            var asignacion = x.Historial.Where(h => h.Accion == "Reasignada").MaxBy(h => h.Orden);
            var clave = x.Modulo == "almacen" && x.TipoDocumento == "vale" && x.CodigoMotivo == "VALE_SIN_REGULARIZAR"
                && x.PasoReanudacion == PasosIncidencia.AlmValeRegularizar
                ? ClaveAviso.Crear(contexto.EmpresaId, "almacen", "movimiento", x.DocumentoId, "regularizar-vale", "1")
                : ClaveAviso.Crear(contexto.EmpresaId, "incidencias", "incidencia", x.Id, "atender", "1",
                    (asignacion?.Orden ?? 0).ToString(CultureInfo.InvariantCulture));
            return new AvisoUsuario(clave, Nombre, "incidencia", x.Id,
                $"INC-{x.Numero.ToString(CultureInfo.InvariantCulture)}", PasosIncidencia.Nombre(x.Paso), x.Motivo,
                asignacion?.En ?? x.AbiertaEn, "Accion", "/incidencias", x.SucursalId);
        }).ToList();
    }
}
