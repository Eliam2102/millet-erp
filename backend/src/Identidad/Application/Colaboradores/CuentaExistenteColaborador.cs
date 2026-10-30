using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Cuenta Microsoft que ya existe para ligarla a un empleado («Ya tiene
/// cuenta Microsoft»). Fix PR #270: la lista de dominios permitidos protege
/// sólo la creación de cuentas nuevas. Una cuenta que ya existe en el
/// directorio, o con la que la persona ya entró al ERP (por ejemplo una
/// cuenta invitada de otro dominio), se liga tal cual.
/// </summary>
internal static class CuentaExistenteColaborador
{
    public static async Task<CuentaEntra> ResolverAsync(
        string correo,
        IEntraDirectorioPort directorio,
        IdentidadDbContext identidad,
        EntraDirectorioOptions options,
        CancellationToken ct)
    {
        var cuenta = await directorio.BuscarPorCorreoAsync(correo, ct);
        if (cuenta is not null)
        {
            if (!cuenta.Habilitada)
                throw new BusinessRuleException(
                    "ENTRA_CUENTA_DESHABILITADA", $"La cuenta Microsoft '{correo}' está deshabilitada.");
            return cuenta;
        }

        // El directorio no la devuelve (p. ej. invitada con UPN #EXT#), pero ya
        // inició sesión en el ERP: su OID quedó validado por Entra en el login.
        var usuario = await identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Email == correo)
            .Select(u => new { u.EntraOid, u.Email, u.Nombre })
            .ToListAsync(ct);
        var conOid = usuario.Where(u => !Usuario.EsOidPendiente(u.EntraOid)).ToList();
        if (conOid.Count == 1)
            return new CuentaEntra(conOid[0].EntraOid, conOid[0].Email, conOid[0].Nombre, Habilitada: true);

        if (!options.PermiteCorreo(correo))
            throw new BusinessRuleException(
                "ENTRA_DOMINIO_NO_PERMITIDO",
                $"El dominio de '{correo}' no está permitido para cuentas corporativas.");
        throw new BusinessRuleException(
            "ENTRA_CUENTA_NO_ENCONTRADA", $"No existe una cuenta Microsoft con el correo '{correo}'.");
    }
}
