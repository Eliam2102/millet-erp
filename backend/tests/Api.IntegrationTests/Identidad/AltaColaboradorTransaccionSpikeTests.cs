using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Spike F0 del alta unificada Usuario + Empleado (F1-ADM-01, plan 15).
/// Valida que <see cref="IdentidadDbContext"/> y
/// <see cref="CompartidoDbContext"/> pueden compartir una sola
/// transacción de Npgsql (misma cadena de conexión) y que las filas que
/// agregan los interceptores (auditoría) siguen el commit/rollback de esa
/// transacción.
/// </summary>
public class AltaColaboradorTransaccionSpikeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AltaColaboradorTransaccionSpikeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Commit_Persiste_Usuario_Empleado_Y_Auditoria_De_Ambos()
    {
        var usuarioId = Guid.CreateVersion7();
        var empleadoId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);
            var empresaId = await PrimeraEmpresaAsync(compartido);

            await using var tx = await UnirEnTransaccionAsync(identidad, compartido);

            identidad.Usuarios.Add(NuevoUsuario(usuarioId));
            await identidad.SaveChangesAsync();

            compartido.Empleados.Add(NuevoEmpleado(empleadoId, empresaId, usuarioId));
            await compartido.SaveChangesAsync();

            await tx.CommitAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);

            Assert.True(await identidad.Usuarios.AnyAsync(u => u.Id == usuarioId));
            var empleado = await compartido.Empleados.SingleAsync(e => e.Id == empleadoId);
            Assert.Equal(usuarioId, empleado.UsuarioId);

            var auditados = await compartido.Set<AuditLogEntry>()
                .Where(a => a.EntidadId == usuarioId || a.EntidadId == empleadoId)
                .Select(a => a.Entidad)
                .ToListAsync();
            Assert.Contains(nameof(Usuario), auditados);
            Assert.Contains(nameof(Empleado), auditados);
        }
    }

    [Fact]
    public async Task Falla_Del_Empleado_Revierte_Usuario_Y_Su_Auditoria()
    {
        var usuarioId = Guid.CreateVersion7();
        var empleadoDuplicadoId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);
            var empresaId = await PrimeraEmpresaAsync(compartido);

            // Empleado previo con la misma clave: el segundo viola
            // UNIQUE(empresa_id, clave) y simula la falla del alta.
            var clave = ClaveUnica();
            compartido.Empleados.Add(NuevoEmpleado(Guid.CreateVersion7(), empresaId, null, clave));
            await compartido.SaveChangesAsync();
            compartido.ChangeTracker.Clear();

            await using var tx = await UnirEnTransaccionAsync(identidad, compartido);

            identidad.Usuarios.Add(NuevoUsuario(usuarioId));
            await identidad.SaveChangesAsync();

            compartido.Empleados.Add(NuevoEmpleado(empleadoDuplicadoId, empresaId, usuarioId, clave));
            await Assert.ThrowsAsync<DbUpdateException>(() => compartido.SaveChangesAsync());

            await tx.RollbackAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);

            Assert.False(await identidad.Usuarios.AnyAsync(u => u.Id == usuarioId));
            Assert.False(await compartido.Empleados.AnyAsync(e => e.Id == empleadoDuplicadoId));
            Assert.False(await compartido.Set<AuditLogEntry>()
                .AnyAsync(a => a.EntidadId == usuarioId || a.EntidadId == empleadoDuplicadoId));
        }
    }

    /// <summary>
    /// Prueba discriminante: si Compartido no estuviera enlistado en la
    /// transacción de Identidad, su SaveChanges haría autocommit y el
    /// empleado sobreviviría al rollback.
    /// </summary>
    [Fact]
    public async Task Rollback_Revierte_Tambien_Lo_Guardado_Por_Compartido()
    {
        var usuarioId = Guid.CreateVersion7();
        var empleadoId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);
            var empresaId = await PrimeraEmpresaAsync(compartido);

            await using var tx = await UnirEnTransaccionAsync(identidad, compartido);

            identidad.Usuarios.Add(NuevoUsuario(usuarioId));
            await identidad.SaveChangesAsync();
            compartido.Empleados.Add(NuevoEmpleado(empleadoId, empresaId, usuarioId));
            await compartido.SaveChangesAsync();

            await tx.RollbackAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var (identidad, compartido) = Contextos(sp);

            Assert.False(await identidad.Usuarios.AnyAsync(u => u.Id == usuarioId));
            Assert.False(await compartido.Empleados.AnyAsync(e => e.Id == empleadoId));
            Assert.False(await compartido.Set<AuditLogEntry>()
                .AnyAsync(a => a.EntidadId == usuarioId || a.EntidadId == empleadoId));
        }
    }

    /// <summary>
    /// Mecanismo bajo prueba: Compartido reutiliza la conexión de Identidad
    /// y se enlista en su transacción. Debe llamarse antes de usar
    /// <paramref name="compartido"/> dentro de la unidad de trabajo.
    /// </summary>
    private static async Task<IDbContextTransaction> UnirEnTransaccionAsync(
        IdentidadDbContext identidad, CompartidoDbContext compartido)
    {
        var tx = await identidad.Database.BeginTransactionAsync();
        await compartido.Database.CloseConnectionAsync();
        compartido.Database.SetDbConnection(identidad.Database.GetDbConnection());
        await compartido.Database.UseTransactionAsync(tx.GetDbTransaction());
        return tx;
    }

    private static (IdentidadDbContext, CompartidoDbContext) Contextos(IServiceProvider sp) =>
        (sp.GetRequiredService<IdentidadDbContext>(), sp.GetRequiredService<CompartidoDbContext>());

    private static Task<Guid> PrimeraEmpresaAsync(CompartidoDbContext db) =>
        db.Empresas.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id).FirstAsync();

    private static Usuario NuevoUsuario(Guid id)
    {
        var sufijo = id.ToString("N")[..12];
        return new Usuario(id, $"pending:spike-{sufijo}", $"spike-{sufijo}@millet.test", "Spike Alta Colaborador");
    }

    private static Empleado NuevoEmpleado(Guid id, Guid empresaId, Guid? usuarioId, string? clave = null) =>
        new(id, empresaId, clave ?? ClaveUnica(), "Spike Alta Colaborador", usuarioId: usuarioId);

    private static string ClaveUnica() => "SPK" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
