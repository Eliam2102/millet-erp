using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Application.Cajas;
using Millet.Facturacion.Infrastructure.Catalogos;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.Api.UnitTests;

public sealed class FormasPagoP6Tests
{
    [Fact]
    public async Task Alta_y_cambio_de_catalogo_SAT_quedan_en_bitacora_CA1_9()
    {
        var actor = new Actor(); var empresa = new Empresa(); var reloj = new Reloj();
        var interceptor = new AuditSaveChangesInterceptor(reloj, actor, empresa, new AuditOriginContext(), new AuditCorrelationContext());
        using var db = new CatalogoAuditado(new DbContextOptionsBuilder<CatalogoAuditado>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor).Options);
        // Catálogo aislado: se usa una clave SAT existente sin tocar el seed compartido.
        var forma = new FormaPago(Guid.NewGuid(), "02", "Cheque nominativo");
        db.Formas.Add(forma); await db.SaveChangesAsync();
        forma.CambiarEstado(false); await db.SaveChangesAsync();
        var registros = await db.AuditLog.Where(x => x.EntidadId == forma.Id).ToListAsync();
        Assert.Equal(2, registros.Count);
        Assert.Contains(registros, x => x.Entidad == "FormaPago" && x.Operacion == "crear" && x.Cambios.Contains("02", StringComparison.Ordinal));
        Assert.Contains(registros, x => x.Operacion == "actualizar" && x.Cambios.Contains("Activa", StringComparison.Ordinal));
        Assert.All(registros, x => { Assert.Equal(actor.UserId, x.UsuarioId); Assert.Equal(empresa.Current, x.EmpresaId); });
    }

    [Fact]
    public async Task Catalogo_real_rechaza_02_desactivada_y_permite_reactivarla_CA1_10()
    {
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var forma = new FormaPago(Guid.NewGuid(), "02", "Cheque nominativo");
        db.FormasPago.Add(forma); await db.SaveChangesAsync();
        var catalogos = new CompartidoCatalogosSatReadAdapter(db);
        await FormaPagoActivaGuard.VerificarAsync("02", catalogos, CancellationToken.None);
        forma.CambiarEstado(false); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => FormaPagoActivaGuard.VerificarAsync("02", catalogos, CancellationToken.None));
        Assert.Equal("FORMA_PAGO_INVALIDA", error.Code);
        Assert.Contains("desactivada", error.Message, StringComparison.Ordinal);
        forma.CambiarEstado(true); await db.SaveChangesAsync();
        await FormaPagoActivaGuard.VerificarAsync("02", catalogos, CancellationToken.None);
    }

    private sealed class CatalogoAuditado(DbContextOptions<CatalogoAuditado> options) : DbContext(options)
    {
        public DbSet<FormaPago> Formas => Set<FormaPago>();
        public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        { modelBuilder.Entity<AuditLogEntry>().Ignore(x => x.Ip); }
    }
    private sealed class Actor : ICurrentUserContext
    { public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "Usuario de prueba P6"; }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid(); public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
