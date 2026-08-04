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
/// (F9-PR2): valida que el servicio busca el aprobador vigente con la
/// semántica polimórfica del campo <c>departamento_id</c> (scope_id):
/// JefeDpto → depto, JefeAlmacen → almacen, AutorizadorN2 → sucursal.
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
    public async Task ResolverN1_Con_Naturaleza_Estandar_Busca_JefeAlmacen_Por_AlmacenDestino()
    {
        var deptoId = Guid.CreateVersion7();
        var almacenId = Guid.CreateVersion7();
        var jefeAlmacenUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId);
        await SeedAprobadorAsync(scopeId: almacenId, RolJefeAlmacen, jefeAlmacenUserId);

        try
        {
            var resolved = await ResolverN1Async(rq, Naturaleza.Estandar);
            Assert.Equal(jefeAlmacenUserId, resolved);
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: almacenId);
        }
    }

    [Fact]
    public async Task ResolverN1_Con_Naturaleza_Servicio_Busca_JefeDpto_Por_Departamento()
    {
        var deptoId = Guid.CreateVersion7();
        var almacenId = Guid.CreateVersion7();
        var jefeDptoUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId);
        await SeedAprobadorAsync(scopeId: deptoId, RolJefeDpto, jefeDptoUserId);

        try
        {
            var resolved = await ResolverN1Async(rq, Naturaleza.Servicio);
            Assert.Equal(jefeDptoUserId, resolved);
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: deptoId);
        }
    }

    [Fact]
    public async Task ResolverN1_Con_Naturaleza_Critico_Busca_JefeDpto_Igual_Que_Servicio()
    {
        var deptoId = Guid.CreateVersion7();
        var almacenId = Guid.CreateVersion7();
        var jefeDptoUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId);
        await SeedAprobadorAsync(scopeId: deptoId, RolJefeDpto, jefeDptoUserId);

        try
        {
            var resolved = await ResolverN1Async(rq, Naturaleza.Critico);
            Assert.Equal(jefeDptoUserId, resolved);
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: deptoId);
        }
    }

    [Fact]
    public async Task ResolverN2_Busca_AutorizadorN2_Por_Sucursal()
    {
        var deptoId = Guid.CreateVersion7();
        var sucursalId = Guid.CreateVersion7();
        var autorizadorUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId: Guid.CreateVersion7(), sucursalId: sucursalId);
        await SeedAprobadorAsync(scopeId: sucursalId, RolAutorizadorN2, autorizadorUserId);

        try
        {
            var resolved = await ResolverN2Async(rq, Naturaleza.Critico);
            Assert.Equal(autorizadorUserId, resolved);
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

        var resolved = await ResolverN1Async(rq, Naturaleza.Estandar);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolverN2_Sin_Asignacion_Devuelve_Null()
    {
        var rq = BuildRequisicion(Guid.CreateVersion7(), Guid.CreateVersion7());

        var resolved = await ResolverN2Async(rq, Naturaleza.Riesgo);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolverN1_Aprobador_Cerrado_No_Aparece()
    {
        var deptoId = Guid.CreateVersion7();
        var almacenId = Guid.CreateVersion7();
        var oldUserId = Guid.CreateVersion7();

        var rq = BuildRequisicion(deptoId, almacenId);
        // Sembrar y cerrar inmediatamente.
        var aprobadorId = await SeedAprobadorAsync(scopeId: almacenId, RolJefeAlmacen, oldUserId);
        await CerrarAprobadorAsync(aprobadorId);

        try
        {
            var resolved = await ResolverN1Async(rq, Naturaleza.Estandar);
            Assert.Null(resolved);
        }
        finally
        {
            await CleanupAprobadoresAsync(scopeId: almacenId);
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

    private async Task<Guid?> ResolverN1Async(Requisicion rq, Naturaleza naturaleza)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IResolverAutorizadorPort>();
        return await port.ResolverN1Async(rq, naturaleza);
    }

    private async Task<Guid?> ResolverN2Async(Requisicion rq, Naturaleza naturaleza)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IResolverAutorizadorPort>();
        return await port.ResolverN2Async(rq, naturaleza);
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
