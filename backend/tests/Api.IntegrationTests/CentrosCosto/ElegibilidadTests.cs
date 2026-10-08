using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Asignaciones;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.CentrosCosto.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Xunit;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Pruebas de integración de <see cref="IDim3ElegibilidadPort"/> y <see cref="Dim3ElegibilidadAdapter"/>
/// (G1.11 / ADR-0050). Verifica los 7 casos clave de elegibilidad: existencia, estatus activo,
/// alcance por asignación, bypass por permiso corporativo y captura proxy.
/// </summary>
public class ElegibilidadTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ElegibilidadTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private sealed class FakeUserCtx(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "test-elegibilidad";
    }

    private sealed class FakePerms(params string[] permisos) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permisos.Contains(permiso, StringComparer.Ordinal));
    }

    private sealed record Fixture(
        Guid Grupo2, Guid Grupo3,
        Guid Dim1, Guid Dim2,
        Guid Dim3Activa, Guid Dim3Inactiva);

    private static async Task<Fixture> CrearFixtureAsync(IMediator mediator, CentrosCostoDbContext db, string sufijo)
    {
        var grupo2 = await mediator.Send(new CrearGrupoDim2Command($"G2-ELG {sufijo}"));
        var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-ELG {sufijo}"));
        var dim1 = await mediator.Send(new CrearDim1Command($"8{sufijo[..3]}", $"PLANTA ELG {sufijo}"));
        var dim2 = await mediator.Send(new CrearDim2Command(dim1.Id, $"D2{sufijo[..4]}", "Corte elg", grupo2.Id));

        var dim3Activa = await mediator.Send(new CrearDim3Command(dim2.Id, $"D3A{sufijo[..3]}", "Activa elg", grupo3.Id));
        var dim3Inactiva = await mediator.Send(new CrearDim3Command(dim2.Id, $"D3I{sufijo[..3]}", "Inactiva elg", grupo3.Id));

        var versionInactiva = (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3Inactiva.Id)).Version;
        await mediator.Send(new CambiarEstatusDim3Command(dim3Inactiva.Id, versionInactiva, Activar: false));

        return new Fixture(grupo2.Id, grupo3.Id, dim1.Id, dim2.Id, dim3Activa.Id, dim3Inactiva.Id);
    }

    private static async Task LimpiarAsync(CentrosCostoDbContext db, Guid usuarioId, Fixture? f)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.asignaciones WHERE usuario_id = {usuarioId}");
        if (f is null)
            return;

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM centros_costo.dim3 WHERE dim2_id IN
                (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {f.Dim1})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.dim2 WHERE dim1_id = {f.Dim1}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.dim1 WHERE id = {f.Dim1}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.grupos_dim2 WHERE id = {f.Grupo2}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.grupos_dim3 WHERE id = {f.Grupo3}");
    }

    [Fact]
    public async Task Dim3_Activa_Asignada_Al_Usuario_Con_Alcance_Retorna_Valida()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);
            await mediator.Send(new MarcarAlcanceCommand(usuario, NivelAlcance.Dim3, f.Dim3Activa, null, Asignar: true));

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms();
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Activa, aplicarAlcance: true, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.Valida, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Id_Inexistente_Retorna_NoExiste()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var userCtx = new FakeUserCtx(Guid.NewGuid());
        var perms = new FakePerms();
        var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
        var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

        var resultado = await adapter.EvaluarAsync(Guid.NewGuid(), aplicarAlcance: true, CancellationToken.None);
        Assert.Equal(Dim3Elegibilidad.NoExiste, resultado);
    }

    [Fact]
    public async Task Dim3_Inactiva_Retorna_Inactiva_Incluso_Con_Alcance_Total()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms("centros_costo.dim3.leer-todos");
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Inactiva, aplicarAlcance: true, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.Inactiva, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Dim3_Activa_No_Asignada_Sin_Permiso_Retorna_FueraDeAlcance()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms();
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Activa, aplicarAlcance: true, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.FueraDeAlcance, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Dim3_Activa_No_Asignada_Con_Permiso_Bypass_Retorna_Valida()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms("centros_costo.dim3.leer-todos");
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Activa, aplicarAlcance: true, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.Valida, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Usuario_Sin_Filas_De_Asignacion_Retorna_FueraDeAlcance()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms();
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Activa, aplicarAlcance: true, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.FueraDeAlcance, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Dim3_Activa_No_Asignada_Con_AplicarAlcance_False_Retorna_Valida()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            var userCtx = new FakeUserCtx(usuario);
            var perms = new FakePerms();
            var evaluator = new AlcanceDim3Evaluator(db, userCtx, perms);
            var adapter = new Dim3ElegibilidadAdapter(db, evaluator);

            var resultado = await adapter.EvaluarAsync(f.Dim3Activa, aplicarAlcance: false, CancellationToken.None);
            Assert.Equal(Dim3Elegibilidad.Valida, resultado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }
}
