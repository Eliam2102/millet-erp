using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Avisos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.Notificaciones;

/// <summary>
/// Avisos de Almacén (86e3tac2y): primero dentro del ERP (campana del destinatario, una sola vez por vale o ajuste) y, solo cuando
/// se creó algún aviso nuevo, el correo por <see cref="CorreoNotificacionService"/>. Los destinatarios son roles: quien tenga el
/// permiso <c>almacen.avisos.*</c> (y, en el vale, esté en la sucursal del vale).
/// </summary>
public sealed class AvisosInternosNotificacionService(
    AlmacenDbContext db,
    IDestinatariosAvisoReadPort destinatarios,
    IUsuarioReadPort usuarios,
    CorreoNotificacionService correo) : INotificacionService
{
    public const string PermisoValeVencido = "almacen.avisos.vale-vencido";
    public const string PermisoAjusteMayor = "almacen.avisos.ajuste-mayor";
    public const string PermisoRevisarActa = "almacen.cierre.revisar-acta";
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>El conteo no tiene folio propio: se muestra una clave corta estable derivada de su id.</summary>
    public static string FolioConteo(Guid conteoId) => "CNT-" + conteoId.ToString("N")[^8..].ToUpperInvariant();

    public async Task NotificarAjusteNivel3AprobadoAsync(AvisoAjusteNivel3 aviso, CancellationToken cancellationToken)
    {
        var para = await destinatarios.UsuariosConPermisoAsync(aviso.EmpresaId, PermisoAjusteMayor, null, cancellationToken);
        var nombres = await usuarios.ObtenerNombresAsync([aviso.AprobadorId], cancellationToken);
        var aprobador = nombres.GetValueOrDefault(aviso.AprobadorId, "un usuario");
        var folio = FolioConteo(aviso.ConteoId);
        var nuevos = await CrearAsync(aviso.EmpresaId, AvisoAlmacen.TipoAjusteMayor, aviso.ConteoId, para,
            $"Ajuste de inventario mayor aprobado: {folio}",
            $"Conteo {folio} con ajuste neto de {aviso.MontoNetoMxn.ToString("C2", Mx)} MXN, aprobado por {aprobador}.",
            "/almacen/inventarios", cancellationToken);
        if (nuevos) await correo.NotificarAjusteNivel3AprobadoAsync(aviso, cancellationToken);
    }

    public async Task NotificarValeSinRegularizarAsync(Guid movimientoValeId, string folioVale, Guid? personaDestinatariaId,
        DateTimeOffset fechaLimite, int diaDelSla, CancellationToken cancellationToken)
    {
        var vale = await db.Movimientos.AsNoTracking().Where(m => m.Id == movimientoValeId)
            .Select(m => new { m.EmpresaId, m.RegistradoPor }).FirstOrDefaultAsync(cancellationToken);
        if (vale is null) return;
        var subAlmacenId = await db.MovimientosSubAlmacen.AsNoTracking().Where(ms => ms.MovimientoId == movimientoValeId)
            .Select(ms => (Guid?)ms.SubAlmacenId).FirstOrDefaultAsync(cancellationToken);
        var sucursalId = subAlmacenId is null ? null : await db.SubAlmacenes.AsNoTracking().Where(s => s.Id == subAlmacenId)
            .Join(db.Almacenes, s => s.AlmacenId, a => a.Id, (_, a) => (Guid?)a.SucursalId).FirstOrDefaultAsync(cancellationToken);
        var para = (await destinatarios.UsuariosConPermisoAsync(vale.EmpresaId, PermisoValeVencido, sucursalId, cancellationToken)).ToList();
        if (vale.RegistradoPor is { } responsable) para.Add(responsable);
        var nuevos = await CrearAsync(vale.EmpresaId, AvisoAlmacen.TipoValeVencido, movimientoValeId, para,
            $"Vale urgente {folioVale} sin regularizar",
            $"El vale {folioVale} venció el {fechaLimite.ToString("dd/MM/yyyy HH:mm", Mx)} sin requisición que lo regularice.",
            "/almacen/salidas", cancellationToken);
        if (nuevos) await correo.NotificarValeSinRegularizarAsync(movimientoValeId, folioVale, personaDestinatariaId, fechaLimite, diaDelSla, cancellationToken);
    }

    /// <summary>Avisa a los revisores del acta recién generada (permiso <c>almacen.cierre.revisar-acta</c>).</summary>
    public async Task NotificarActaPorRevisarAsync(Guid empresaId, Guid actaId, int anio, int mes, CancellationToken cancellationToken)
    {
        var para = await destinatarios.UsuariosConPermisoAsync(empresaId, PermisoRevisarActa, null, cancellationToken);
        await CrearAsync(empresaId, AvisoAlmacen.TipoActaPorRevisar, actaId, para,
            $"Acta de cierre {anio}-{mes:00} por revisar",
            $"Se cerró el inventario de {anio}-{mes:00}; el acta espera tu revisión.", "/almacen/cierre-mes", cancellationToken);
    }

    /// <summary>Crea un aviso por destinatario que aún no lo tenga; devuelve si hubo alguno nuevo.</summary>
    private async Task<bool> CrearAsync(Guid empresaId, string tipo, Guid referenciaId, IEnumerable<Guid> para,
        string titulo, string mensaje, string ruta, CancellationToken ct)
    {
        var ids = para.Distinct().ToArray();
        var existentes = await db.AvisosAlmacen.AsNoTracking()
            .Where(a => a.Tipo == tipo && a.ReferenciaId == referenciaId && ids.Contains(a.UsuarioDestinoId))
            .Select(a => a.UsuarioDestinoId).ToListAsync(ct);
        var faltan = ids.Except(existentes).ToList();
        if (faltan.Count == 0) return false;
        db.AvisosAlmacen.AddRange(faltan.Select(u => new AvisoAlmacen(empresaId, u, tipo, referenciaId, titulo, mensaje, ruta)));
        await db.SaveChangesAsync(ct);
        return true;
    }
}
