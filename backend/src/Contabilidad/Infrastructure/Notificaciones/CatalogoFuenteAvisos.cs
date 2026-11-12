using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Notificaciones;

namespace Millet.Contabilidad.Infrastructure.Notificaciones;

public sealed class CatalogoFuenteAvisos(ContabilidadDbContext db, ICurrentUserPermissions permisos) : IFuenteAvisosUsuario
{
    public string Nombre => "contabilidad";

    public async Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
    {
        if (!await permisos.TieneAsync("contabilidad.catalogo.leer", ct) ||
            !await permisos.TieneAsync("contabilidad.catalogo.autorizar", ct)) return [];

        var solicitudes = await db.SolicitudesCatalogo.AsNoTracking().Where(s =>
            s.EmpresaId == contexto.EmpresaId && s.Estado == "Pendiente" && s.PreparadaPorId != contexto.UsuarioId).ToListAsync(ct);
        return solicitudes.Select(s => new AvisoUsuario(
            ClaveAviso.Crear(contexto.EmpresaId, Nombre, "solicitud-catalogo", s.Id, "autorizar", "1"),
            Nombre, "solicitud-catalogo", s.Id, s.CodigoAlta ?? s.Operacion,
            "Autorizar solicitud del catálogo de cuentas",
            $"{s.Operacion} preparada por {s.PreparadaPor}. Abre Autorizaciones para revisar los cambios antes de resolver.",
            s.PreparadaEn, "Accion", "/contabilidad/catalogo")).ToList();
    }
}
