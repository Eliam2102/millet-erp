using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.AppAlmacenes;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>M-335: origen vigente de cada evento de materia prima, su guardia y el inventario de pendientes antes de cambiarlo.</summary>
public static class OrigenCapturaMpReglas
{
    public sealed record Vigente(string Evento, string Origen, DateTimeOffset? Desde);

    public sealed record Pendientes(int MensajesEnRevision, int? MensajesSinAcuse)
    {
        public bool Hay => MensajesEnRevision > 0 || MensajesSinAcuse > 0;
    }

    public static async Task<Vigente> VigenteAsync(AlmacenDbContext db, string evento, CancellationToken ct)
    {
        var ultimo = await db.OrigenesCapturaMp.AsNoTracking().Where(o => o.Evento == evento)
            .OrderByDescending(o => o.CambiadoAt).FirstOrDefaultAsync(ct);
        return new Vigente(evento, ultimo?.Origen ?? OrigenCapturaMp.Ambos, ultimo?.CambiadoAt);
    }

    /// <summary>La pantalla del ERP no registra un evento que ya se captura en la app.</summary>
    public static async Task ExigirErpAsync(AlmacenDbContext db, string evento, CancellationToken ct)
    {
        var v = await VigenteAsync(db, evento, ct);
        if (v.Origen == OrigenCapturaMp.App)
            throw new BusinessRuleException("ORIGEN_CAPTURA_APP",
                $"La {OrigenCapturaMp.NombreEvento(evento)} de materia prima se captura en la app de Almacenes desde el "
                + $"{v.Desde!.Value.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm}; el ERP ya no la registra.");
    }

    /// <summary>El mensaje de la app de un evento que hoy captura el ERP: texto del ERROR; null = se acepta.</summary>
    public static async Task<string?> RechazoDeMensajeAsync(AlmacenDbContext db, string tipo, CancellationToken ct)
    {
        if (OrigenCapturaMp.EventoDeMensaje(tipo) is not { } evento) return null;
        var v = await VigenteAsync(db, evento, ct);
        return v.Origen == OrigenCapturaMp.Erp
            ? $"La {OrigenCapturaMp.NombreEvento(evento)} de materia prima hoy se captura en el ERP; {tipo} no se acepta hasta que el ERP la pase a la app."
            : null;
    }

    /// <summary>Mensajes de la app de ese evento en ERROR o en revisión (los del ERP) y, si se puede leer, los sin acuse de la tabla.</summary>
    public static async Task<Pendientes> PendientesAsync(AlmacenDbContext db, string evento, int? sinAcuse, CancellationToken ct)
    {
        var tipos = OrigenCapturaMp.TiposDeMensaje(evento);
        var enRevision = await db.MensajesApp.AsNoTracking()
            .CountAsync(m => tipos.Contains(m.Tipo) && (m.Estado == "ERROR" || m.Estado == SalidaAppAlmacenes.EnRevision), ct);
        return new Pendientes(enRevision, sinAcuse);
    }

    /// <summary>Cambia el origen: solo sin pendientes. Queda en la bitácora; revertir es otro cambio.</summary>
    public static async Task<OrigenCapturaMp> CambiarAsync(AlmacenDbContext db, string evento, string origen, string motivo,
        Guid usuarioId, int? sinAcuse, CancellationToken ct)
    {
        var vigente = await VigenteAsync(db, evento, ct);
        var p = await PendientesAsync(db, evento, sinAcuse, ct);
        if (p.Hay)
            throw new BusinessRuleException("ORIGEN_CAPTURA_CON_PENDIENTES",
                $"Antes de cambiar el origen hay que conciliar {p.MensajesEnRevision} mensaje(s) en revisión"
                + (p.MensajesSinAcuse > 0 ? $" y {p.MensajesSinAcuse} sin acuse" : "") + ".");
        var cambio = new OrigenCapturaMp(evento, origen, vigente.Origen, motivo, usuarioId);
        db.OrigenesCapturaMp.Add(cambio);
        await db.SaveChangesAsync(ct);
        return cambio;
    }
}
