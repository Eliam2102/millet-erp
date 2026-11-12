using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador productivo de <see cref="ICuentaContableReadPort"/> (lo hospeda el owner, ADR-0050).
/// Reglas R6/R7/R10 + P14/P19/P23/P24: orden de rechazo NoExiste → Rubro → Inactiva → PendienteValidacion → Titulo (acumula)
/// → ControlSoloAuxiliar. Un tipo nulo (dato previo a P19) se trata como acumula: nunca se acepta un movimiento por omisión.
/// El filtro global por empresa hace que una cuenta de otra empresa no exista.
/// </summary>
public sealed class CuentaContableReadAdapter(ContabilidadDbContext db, FormatoCatalogo formato) : ICuentaContableReadPort
{
    public async Task<CuentaContableValidacion> ValidarParaMovimientoAsync(string codigoCuenta, OrigenMovimiento origen, CancellationToken ct)
    {
        var codigo = FormatoCatalogo.Codigo(codigoCuenta) ?? string.Empty;
        return Validar(await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Codigo == codigo, ct), origen, formato);
    }

    public async Task<CuentaContableValidacion> ValidarParaMovimientoAsync(Guid cuentaId, OrigenMovimiento origen, CancellationToken ct) =>
        Validar(await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cuentaId, ct), origen, formato);

    public async Task<CuentaContableLectura?> ObtenerAsync(Guid cuentaId, CancellationToken ct)
    {
        var c = await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cuentaId, ct);
        return c is null ? null : Lectura(c);
    }

    public async Task<bool> EsDeRolAsync(string codigoCuenta, string rol, CancellationToken ct)
    {
        var codigo = FormatoCatalogo.Codigo(codigoCuenta) ?? string.Empty;
        var prefijo = rol + "_";
        var deRoles = await db.RolesCuenta.AsNoTracking()
            .Where(r => (r.Codigo == rol || r.Codigo.StartsWith(prefijo)) && r.DeletedAt == null && r.CuentaId != null)
            .Select(r => r.CuentaId!.Value).Distinct().ToListAsync(ct);
        if (deRoles.Count == 0) return false;
        var candidata = await db.Cuentas.AsNoTracking().Where(c => c.Codigo == codigo).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (candidata is not Guid id) return false;
        if (deRoles.Contains(id)) return true;
        // El motor solo asigna roles a cuentas afectables: «de bancos» = misma rama raíz (p. ej. 102 Bancos) que una cuenta del rol.
        var raices = new HashSet<Guid>();
        foreach (var r in deRoles) raices.Add(await RaizAsync(r, ct));
        return raices.Contains(await RaizAsync(id, ct));
    }

    // ponytail: sube por el árbol una consulta por nivel; el catálogo tiene 4 o 5 niveles.
    private async Task<Guid> RaizAsync(Guid id, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var padre = await db.Cuentas.AsNoTracking().Where(c => c.Id == id).Select(c => c.PadreId).FirstOrDefaultAsync(ct);
            if (padre is not Guid p) return id;
            id = p;
        }
        return id;
    }

    internal static CuentaContableValidacion Validar(CuentaContable? c, OrigenMovimiento origen, FormatoCatalogo formato)
    {
        if (c is null) return new(false, MotivoRechazoCuenta.NoExiste, null);
        var l = Lectura(c);
        MotivoRechazoCuenta? motivo =
            c.EsRubro ? MotivoRechazoCuenta.Rubro
            : !c.Activa ? MotivoRechazoCuenta.Inactiva
            : c.PendienteValidacion ? MotivoRechazoCuenta.PendienteValidacion
            : c.Tipo != TipoCuenta.Afectable ? MotivoRechazoCuenta.Titulo
            : c.NoAfectableManual && origen == OrigenMovimiento.Manual ? MotivoRechazoCuenta.NoAfectableManual
            : c.CuentaControl != CuentaControl.Ninguna && !OrigenPermitido(c.CuentaControl, origen, formato) ? MotivoRechazoCuenta.ControlSoloAuxiliar
            : null;
        return new(motivo is null, motivo, l);
    }

    /// <summary>P23: la captura manual nunca afecta una cuenta colectiva, diga lo que diga la configuración.</summary>
    private static bool OrigenPermitido(CuentaControl control, OrigenMovimiento origen, FormatoCatalogo formato) =>
        origen != OrigenMovimiento.Manual
        && (formato.Opciones.OrigenesControl.GetValueOrDefault(control.ToString())?.Contains(origen.ToString()) ?? false);

    private static CuentaContableLectura Lectura(CuentaContable c) =>
        new(c.Id, c.Codigo, c.Nombre, c.Naturaleza, c.Tipo, c.Activa, c.CuentaControl, c.PendienteValidacion, c.NoAfectableManual);
}
