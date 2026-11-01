using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Matriz;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.IntegrationTests.Matriz;

/// <summary>
/// Tests integration de <see cref="ResolverAutorizadorService"/>
/// (C3.1 / F-09): valida que el servicio busca el aprobador vigente con la
/// semántica polimórfica del campo <c>departamento_id</c> (scope_id):
/// N1 = JefeDpto por departamento; N2 = JefeAlmacen por sucursal.
/// </summary>
public class ResolverAutorizadorServiceTests : IClassFixture<StubsWebApplicationFactory>
{
    private static readonly Guid EmpresaInicial = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // RolAprobador como smallint (mismo enum del dominio):
    private const short RolJefeDpto = 0;
    private const short RolJefeAlmacen = 1;
    private const short RolAutorizadorN2 = 2;

    private readonly StubsWebApplicationFactory _factory;

    public ResolverAutorizadorServiceTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResolverN1_Busca_JefeDpto_Por_Departamento()
    {
        var deptoId = Guid.CreateVersion7();
        var jefeDptoUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId: Guid.CreateVersion7());
        await SeedAprobadorAsync(scopeId: deptoId, RolJefeDpto, jefeDptoUserId);

        try
        {
            Assert.Equal(jefeDptoUserId, await ResolverN1Async(rq));
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: deptoId);
        }
    }

    [Fact]
    public async Task ResolverN1_Ignora_JefeAlmacen_Del_Almacen_Destino()
    {
        var almacenId = Guid.CreateVersion7();
        var rq = BuildRequisicion(Guid.CreateVersion7(), almacenId);
        await SeedAprobadorAsync(scopeId: almacenId, RolJefeAlmacen, Guid.CreateVersion7());

        try
        {
            Assert.Null(await ResolverN1Async(rq));
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: almacenId);
        }
    }

    [Fact]
    public async Task ResolverN2_Busca_JefeAlmacen_Por_Sucursal()
    {
        var sucursalId = Guid.CreateVersion7();
        var jefeAlmacenUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(Guid.CreateVersion7(), almacenId: Guid.CreateVersion7(), sucursalId: sucursalId);
        await SeedAprobadorAsync(scopeId: sucursalId, RolJefeAlmacen, jefeAlmacenUserId);

        try
        {
            Assert.Equal(jefeAlmacenUserId, await ResolverN2Async(rq));
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: sucursalId);
        }
    }

    [Fact]
    public async Task ResolverN2_Ignora_AutorizadorN2()
    {
        var sucursalId = Guid.CreateVersion7();
        var rq = BuildRequisicion(Guid.CreateVersion7(), almacenId: Guid.CreateVersion7(), sucursalId: sucursalId);
        await SeedAprobadorAsync(scopeId: sucursalId, RolAutorizadorN2, Guid.CreateVersion7());

        try
        {
            Assert.Null(await ResolverN2Async(rq));
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: sucursalId);
        }
    }

    [Fact]
    public async Task ResolverN1_Sin_Asignacion_Devuelve_Null()
    {
        var rq = BuildRequisicion(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Null(await ResolverN1Async(rq));
    }

    [Fact]
    public async Task ResolverN2_Sin_Asignacion_Devuelve_Null()
    {
        var rq = BuildRequisicion(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Null(await ResolverN2Async(rq));
    }

    [Fact]
    public async Task ResolverN1_Aprobador_Cerrado_No_Aparece()
    {
        var deptoId = Guid.CreateVersion7();
        var oldUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, Guid.CreateVersion7());
        // Sembrar y cerrar inmediatamente.
        var aprobadorId = await SeedAprobadorAsync(scopeId: deptoId, RolJefeDpto, oldUserId);
        await CerrarAprobadorAsync(aprobadorId);

        try
        {
            Assert.Null(await ResolverN1Async(rq));
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: deptoId);
        }
    }

    // --- Helpers ---

    private static Requisicion BuildRequisicion(
        Guid deptoId, Guid almacenId, Guid? sucursalId = null)
    {
        return new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaInicial,
            folio: Folio.Parse("MID2026-000001"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: sucursalId ?? Guid.CreateVersion7(),
            departamentoId: deptoId,
            almacenDestinoId: almacenId,
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: "Test resolver");
    }

    private async Task<Guid?> ResolverN1Async(Requisicion rq)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IResolverAutorizadorPort>();
        return await port.ResolverN1Async(rq);
    }

    private async Task<Guid?> ResolverN2Async(Requisicion rq)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IResolverAutorizadorPort>();
        return await port.ResolverN2Async(rq);
    }

    private async Task<Guid> SeedAprobadorAsync(Guid scopeId, short rol, Guid usuarioId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var aprobador = new AprobadorDepartamento(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaInicial,
            departamentoId: scopeId,   // Polymorphic — actúa como scope_id.
            rol: (RolAprobador)rol,
            usuarioId: usuarioId,
            vigenteDesde: DateTimeOffset.UtcNow,
            designadoPor: usuarioId,
            motivo: "test seed",
            createdAt: DateTimeOffset.UtcNow);
        db.AprobadoresDepartamento.Add(aprobador);
        await db.SaveChangesAsync();
        return aprobador.Id;
    }

    private async Task CerrarAprobadorAsync(Guid aprobadorId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var aprobador = await db.AprobadoresDepartamento.FirstAsync(a => a.Id == aprobadorId);
        aprobador.Cerrar(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task CleanupAprobadoresAsync(Guid scopeId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var rows = await db.AprobadoresDepartamento
            .Where(a => a.EmpresaId == EmpresaInicial && a.DepartamentoId == scopeId)
            .ToListAsync();
        db.AprobadoresDepartamento.RemoveRange(rows);
        await db.SaveChangesAsync();
    }
}
