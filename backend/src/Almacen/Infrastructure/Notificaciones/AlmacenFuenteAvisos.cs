using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Avisos;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Notificaciones;

namespace Millet.Almacen.Infrastructure.Notificaciones;

public sealed class AlmacenFuenteAvisos(
    AlmacenDbContext db,
    ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa,
    ICurrentUserPermissions permisos,
    IDestinatariosAvisoReadPort destinatarios,
    IAlmacenSucursalReadPort documentos,
    Func<Guid?, string, CancellationToken, Task> verificarSucursal,
    IClock clock) : IFuenteAvisosUsuario
{
    public string Nombre => "almacen";

    public async Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
    {
        ValidarContexto(contexto);
        var filas = await db.AvisosAlmacen.AsNoTracking()
            .Where(a => a.EmpresaId == contexto.EmpresaId && a.UsuarioDestinoId == contexto.UsuarioId)
            .OrderBy(a => a.CreadoAt).ThenBy(a => a.Id).ToListAsync(ct);
        if (filas.Count == 0) return [];
        var resultado = new List<AvisoUsuario>();
        var permisosVigentes = new Dictionary<string, bool>();
        var alcancesVigentes = new Dictionary<(Guid?, string), bool>();
        var sucursalesDocumentos = new Dictionary<string, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>();

        async Task<bool> PuedeAsync(string permiso)
        {
            if (!permisosVigentes.TryGetValue(permiso, out var puede))
            {
                puede = await permisos.TieneAsync(permiso, ct)
                    && (await destinatarios.UsuariosConPermisoAsync(contexto.EmpresaId, permiso, null, ct)).Contains(contexto.UsuarioId);
                permisosVigentes[permiso] = puede;
            }
            return puede;
        }

        async Task<(bool Permitido, Guid? Sucursal)> AlcanceAsync(string tipo, Guid id, string permisoTodas)
        {
            if (!sucursalesDocumentos.TryGetValue(tipo, out var mapa))
            {
                mapa = (await documentos.ListarAsync(tipo, ct)).ToDictionary(d => d.Id, d => d.Sucursales);
                sucursalesDocumentos[tipo] = mapa;
            }
            if (!mapa.TryGetValue(id, out var sucursales)) return (false, null);
            if (contexto.SucursalId is Guid seleccionada && !sucursales.Contains(seleccionada)) return (false, null);
            if (sucursales.Count == 0 && !await SucursalPermitidaAsync(null, permisoTodas)) return (false, null);
            foreach (var sucursal in sucursales)
                if (!await SucursalPermitidaAsync(sucursal, permisoTodas)) return (false, null);
            return (true, contexto.SucursalId ?? (sucursales.Count == 1 ? sucursales[0] : null));
        }

        async Task<bool> SucursalPermitidaAsync(Guid? sucursal, string permisoTodas)
        {
            if (alcancesVigentes.TryGetValue((sucursal, permisoTodas), out var permitido)) return permitido;
            try { await verificarSucursal(sucursal, permisoTodas, ct); permitido = true; }
            catch (ForbiddenException) { permitido = false; }
            alcancesVigentes[(sucursal, permisoTodas)] = permitido;
            return permitido;
        }

        var referencias = filas.Select(f => f.ReferenciaId).Distinct().ToArray();
        var ahora = clock.UtcNow;
        var vales = await db.Movimientos.AsNoTracking().Where(m => referencias.Contains(m.Id)
            && m.EmpresaId == contexto.EmpresaId && m.Tipo == TipoMovimiento.SalidaPorVale
            && m.Estado == EstadoMovimiento.Registrado && m.PendienteRegularizacion
            && m.FechaLimiteRegularizacion < ahora).ToDictionaryAsync(m => m.Id, ct);
        var conteos = await db.Conteos.AsNoTracking().Where(c => referencias.Contains(c.Id)
            && c.EmpresaId == contexto.EmpresaId && (c.Estado == EstadoConteo.Aprobado || c.Estado == EstadoConteo.Aplicado))
            .ToDictionaryAsync(c => c.Id, ct);
        var actas = await db.ActasCierre.AsNoTracking().Where(a => referencias.Contains(a.Id)
            && a.EmpresaId == contexto.EmpresaId && a.Estado == ActaCierreMes.PorRevisar).ToDictionaryAsync(a => a.Id, ct);

        foreach (var fila in filas)
        {
            string documento, paso, categoria, folio, destino;
            Guid? sucursal = null;
            switch (fila.Tipo)
            {
                case AvisoAlmacen.TipoValeVencido:
                    if (!vales.TryGetValue(fila.ReferenciaId, out var vale)) continue;
                    if (!await PuedeAsync("almacen.salidas.leer-todas")) continue;
                    if (!await PuedeAsync(AvisosInternosNotificacionService.PermisoValeVencido)
                        && vale.RegistradoPor != contexto.UsuarioId) continue;
                    var alcanceVale = await AlcanceAsync("salida_almacen", vale.Id, "almacen.salidas.leer-todas-sucursales");
                    if (!alcanceVale.Permitido) continue;
                    categoria = await PuedeAsync("almacen.salidas.por-vale")
                        && (await AlcanceAsync("salida_almacen", vale.Id, "almacen.salidas.gestionar-todas-sucursales")).Permitido
                            ? "Accion" : "Informativo";
                    sucursal = alcanceVale.Sucursal;
                    documento = "movimiento"; paso = "regularizar-vale"; folio = vale.Folio ?? "(sin folio)";
                    destino = $"/almacen/salidas/{vale.Id}";
                    break;
                case AvisoAlmacen.TipoAjusteMayor:
                    if (!await PuedeAsync(AvisosInternosNotificacionService.PermisoAjusteMayor)
                        || !await PuedeAsync("almacen.inventarios.leer")) continue;
                    if (!conteos.TryGetValue(fila.ReferenciaId, out var conteo)) continue;
                    var alcanceConteo = await AlcanceAsync("conteo", conteo.Id, "almacen.inventarios.leer-todas-sucursales");
                    if (!alcanceConteo.Permitido) continue;
                    sucursal = alcanceConteo.Sucursal;
                    documento = "conteo"; paso = "ajuste-mayor-aprobado"; categoria = "Informativo";
                    folio = AvisosInternosNotificacionService.FolioConteo(conteo.Id); destino = $"/almacen/inventarios/{conteo.Id}";
                    break;
                case AvisoAlmacen.TipoActaPorRevisar:
                    if (!await PuedeAsync(AvisosInternosNotificacionService.PermisoRevisarActa)) continue;
                    if (!actas.TryGetValue(fila.ReferenciaId, out var acta)) continue;
                    documento = "acta_cierre"; paso = "revisar-acta"; categoria = "Accion";
                    folio = $"{acta.Anio}-{acta.Mes:00}"; destino = "/almacen/cierre-mes";
                    break;
                default: continue;
            }
            resultado.Add(new AvisoUsuario(ClaveAviso.Crear(contexto.EmpresaId, Nombre, documento, fila.ReferenciaId, paso, "1"),
                Nombre, documento, fila.ReferenciaId, folio, fila.Titulo, fila.Mensaje, fila.CreadoAt,
                categoria, destino, sucursal, fila.LeidoAt));
        }
        return resultado;
    }

    public async Task MarcarLeidoAsync(ContextoAvisos contexto, AvisoUsuario aviso, CancellationToken ct)
    {
        var vigente = (await ConsultarAsync(contexto, ct)).SingleOrDefault(a => a.ClaveOcurrencia == aviso.ClaveOcurrencia);
        if (vigente is null) return;
        var tipo = vigente.TipoDocumento switch
        {
            "movimiento" => AvisoAlmacen.TipoValeVencido,
            "conteo" => AvisoAlmacen.TipoAjusteMayor,
            "acta_cierre" => AvisoAlmacen.TipoActaPorRevisar,
            _ => null,
        };
        var fila = await db.AvisosAlmacen.SingleOrDefaultAsync(a => a.EmpresaId == contexto.EmpresaId
            && a.UsuarioDestinoId == contexto.UsuarioId && a.Tipo == tipo && a.ReferenciaId == vigente.DocumentoId, ct);
        if (fila is null || fila.LeidoAt is not null) return;
        fila.MarcarLeido();
        await db.SaveChangesAsync(ct);
    }

    private void ValidarContexto(ContextoAvisos contexto)
    {
        if (contexto.UsuarioId == Guid.Empty || contexto.EmpresaId == Guid.Empty
            || usuario.UserId != contexto.UsuarioId || empresa.Current != contexto.EmpresaId)
            throw new ForbiddenException("AVISOS_CONTEXTO_INVALIDO", "El contexto de avisos no corresponde a la sesión activa.");
    }
}
