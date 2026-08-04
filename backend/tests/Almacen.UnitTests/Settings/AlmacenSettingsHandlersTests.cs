using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Settings;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Settings;

/// <summary>
/// Tests de los settings del módulo Almacén (molde ComprasSettings):
/// lectura default-sin-persistir, upsert por empresa sin duplicar fila,
/// idempotencia del PATCH y aislamiento por empresa del JWT.
/// </summary>
public class AlmacenSettingsHandlersTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Get_sin_fila_devuelve_default_false_y_no_persiste()
    {
        await using var db = await NuevaDbAsync();
        var handler = new ObtenerAlmacenSettingsHandler(db, new EmpresaFija(Empresa));

        var r = await handler.Handle(new ObtenerAlmacenSettingsQuery(), CancellationToken.None);

        r.EmpresaId.Should().Be(Empresa);
        r.ReabastoAutomaticoActivo.Should().BeFalse();
        (await db.AlmacenSettings.CountAsync()).Should().Be(0); // default no persistido
    }

    [Fact]
    public async Task Put_crea_la_fila_y_un_segundo_put_actualiza_la_misma_sin_duplicar()
    {
        await using var db = await NuevaDbAsync();
        var handler = new ActualizarAlmacenSettingsHandler(db, new EmpresaFija(Empresa));

        var r1 = await handler.Handle(
            new ActualizarAlmacenSettingsCommand(ReabastoAutomaticoActivo: true), CancellationToken.None);
        r1.ReabastoAutomaticoActivo.Should().BeTrue();

        var r2 = await handler.Handle(
            new ActualizarAlmacenSettingsCommand(ReabastoAutomaticoActivo: false), CancellationToken.None);
        r2.ReabastoAutomaticoActivo.Should().BeFalse();

        var filas = await db.AlmacenSettings.IgnoreQueryFilters().ToListAsync();
        filas.Should().HaveCount(1); // upsert: misma fila, no duplica
        filas[0].EmpresaId.Should().Be(Empresa);
        filas[0].ReabastoAutomaticoActivo.Should().BeFalse();
    }

    [Fact]
    public async Task Put_idempotente_y_patch_null_no_toca_el_flag()
    {
        await using var db = await NuevaDbAsync();
        var handler = new ActualizarAlmacenSettingsHandler(db, new EmpresaFija(Empresa));

        await handler.Handle(new ActualizarAlmacenSettingsCommand(true), CancellationToken.None);
        // Mismo valor otra vez: idempotente.
        var r = await handler.Handle(new ActualizarAlmacenSettingsCommand(true), CancellationToken.None);
        r.ReabastoAutomaticoActivo.Should().BeTrue();
        // PATCH parcial con null: no tocar.
        var r2 = await handler.Handle(new ActualizarAlmacenSettingsCommand(null), CancellationToken.None);
        r2.ReabastoAutomaticoActivo.Should().BeTrue();
    }

    [Fact]
    public async Task Get_lee_el_flag_de_la_empresa_del_contexto_no_de_otra()
    {
        // Dos contextos sobre el MISMO store, cada uno con su empresa en
        // contexto (como dos requests de usuarios de empresas distintas).
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-settings-{Guid.NewGuid():N}")
            .Options;
        var otraEmpresa = Guid.NewGuid();

        await using (var dbOtra = new AlmacenDbContext(opts, new EmpresaFija(otraEmpresa)))
        {
            await dbOtra.Database.EnsureCreatedAsync();
            await new ActualizarAlmacenSettingsHandler(dbOtra, new EmpresaFija(otraEmpresa))
                .Handle(new ActualizarAlmacenSettingsCommand(true), CancellationToken.None);
        }

        await using var db = new AlmacenDbContext(opts, new EmpresaFija(Empresa));
        var r = await new ObtenerAlmacenSettingsHandler(db, new EmpresaFija(Empresa))
            .Handle(new ObtenerAlmacenSettingsQuery(), CancellationToken.None);

        r.ReabastoAutomaticoActivo.Should().BeFalse(); // el ON de la otra empresa no se filtra
    }

    [Fact]
    public async Task Sin_empresa_en_contexto_lanza_forbidden()
    {
        await using var db = await NuevaDbAsync();
        var handler = new ObtenerAlmacenSettingsHandler(db, new EmpresaFija(null));

        var act = () => handler.Handle(new ObtenerAlmacenSettingsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ─── Infra de test ───

    private static async Task<AlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-settings-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new EmpresaFija(Empresa));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class EmpresaFija : ICurrentEmpresaContext
    {
        public EmpresaFija(Guid? empresa) => Current = empresa;
        public Guid? Current { get; }
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
