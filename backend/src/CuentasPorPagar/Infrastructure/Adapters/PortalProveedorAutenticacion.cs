using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.CuentasPorPagar.Domain.Portal;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Código por correo del portal de proveedores. Bypass del filtro de empresa porque quien
/// llama aún no tiene sesión: la empresa sale del acceso autorizado.
/// </summary>
public sealed class PortalProveedorAutenticacion(
    CuentasPorPagarDbContext db,
    ICurrentEmpresaContext empresa,
    IProveedorReadPort proveedores,
    IPortalCorreoPort correo,
    IPortalTokenEmisor tokens,
    IClock clock,
    ILogger<PortalProveedorAutenticacion> logger) : IPortalProveedorAutenticacion
{
    private async Task<AccesoPortalProveedor?> AccesoActivoAsync(string email, CancellationToken ct)
    {
        var acceso = await db.AccesosPortalProveedor.FirstOrDefaultAsync(a => a.Email == email && a.Activo, ct);
        if (acceso is null) return null;
        var proveedor = await proveedores.ObtenerAsync(acceso.ProveedorId, ct);
        return proveedor is { Activo: true } ? acceso : null;
    }

    public async Task SolicitarCodigoAsync(string? email, CancellationToken cancellationToken)
    {
        using var _ = empresa.Bypass();
        var correoNormal = AccesoPortalProveedor.Normalizar(email);
        var acceso = await AccesoActivoAsync(correoNormal, cancellationToken);
        if (acceso is null) return;

        var ahora = clock.UtcNow;
        var desde = ahora.AddHours(-1);
        var recientes = await db.CodigosPortal.CountAsync(c => c.AccesoId == acceso.Id && c.CreatedAt > desde, cancellationToken);
        if (recientes >= CodigoPortal.MaxPorHora) return;

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.CodigosPortal.Add(new CodigoPortal(acceso.EmpresaId, acceso.Id, codigo, ahora));
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await correo.EnviarCodigoAsync(acceso.Email, codigo, cancellationToken);
        }
        catch (Exception ex)
        {
            // La respuesta sigue siendo la misma para no revelar si el correo existe.
            logger.LogError(ex, "[PortalProveedor] No se pudo enviar el código de acceso al correo autorizado {AccesoId}.", acceso.Id);
        }
    }

    public async Task<PortalToken?> IniciarSesionAsync(string? email, string? codigo, CancellationToken cancellationToken)
    {
        using var _ = empresa.Bypass();
        var acceso = await AccesoActivoAsync(AccesoPortalProveedor.Normalizar(email), cancellationToken);
        if (acceso is null || string.IsNullOrWhiteSpace(codigo)) return null;

        var ahora = clock.UtcNow;
        var vigente = await db.CodigosPortal.Where(c => c.AccesoId == acceso.Id && !c.Consumido)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (vigente is null) return null;

        var ok = vigente.Verificar(codigo.Trim(), ahora);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // intentos en paralelo sobre el mismo código: no se acepta ninguno
        }
        return ok ? tokens.Emitir(acceso.ProveedorId, acceso.EmpresaId, acceso.Email) : null;
    }
}
