using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Api.Auth.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;

namespace Millet.Api.Auth;

/// <summary>
/// 86e3yxe9m: con <c>Identidad:ExigirEmpleadoParaAcceso=true</c>, al arrancar
/// avisa en el log cuántas cuentas activas que no son técnicas no tienen un
/// empleado activo: esas cuentas recibirán 403 <c>USUARIO_SIN_EMPLEADO</c> al
/// entrar. Solo registra ids (sin correos). Nunca detiene el arranque.
/// </summary>
public sealed class CuentasSinEmpleadoAvisoAlArranque(
    IServiceScopeFactory scopes,
    IOptions<IdentidadAccesoOptions> acceso,
    ILogger<CuentasSinEmpleadoAvisoAlArranque> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!acceso.Value.ExigirEmpleadoParaAcceso) return;
        try
        {
            using var scope = scopes.CreateScope();
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var conEmpleado = (await compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
                    .Where(e => e.UsuarioId != null && e.Estatus == EstatusCatalogo.Activo)
                    .Select(e => e.UsuarioId!.Value)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var sinEmpleado = (await identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Activo && !u.EsCuentaTecnica)
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken))
                .Where(id => !conEmpleado.Contains(id))
                .ToList();
            if (sinEmpleado.Count > 0)
                logger.LogWarning(
                    "Identidad:ExigirEmpleadoParaAcceso está encendida y {Total} cuenta(s) activa(s) que no son técnicas " +
                    "no tienen empleado activo; recibirán 403 USUARIO_SIN_EMPLEADO al entrar. Usuarios: {UsuarioIds}",
                    sinEmpleado.Count, string.Join(", ", sinEmpleado));
        }
#pragma warning disable CA1031 // Un aviso no debe impedir que la API arranque.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "No se pudo revisar qué cuentas quedan sin acceso por la regla de empleado.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
