using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Persistence;

/// <summary>
/// F1-ADM-11 G1.2 (B2): mapeo EF de <see cref="Adjunto"/> /
/// <see cref="AdjuntoTipoDocumento"/> contra PostgreSQL real: seed de los 5
/// tipos de proveedor, roundtrip, constraints, y auditoría agrupada por la
/// entidad dueña (<c>aggregate_root_id</c> = <c>EntidadId</c>).
/// </summary>
public class AdjuntosPersistenciaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TipoContrato = Guid.Parse("00000011-0001-0000-0000-000000000002");
    private readonly WebApplicationFactory<Program> _factory;

    public AdjuntosPersistenciaTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static Adjunto Nuevo(Guid entidadId, DateOnly? vigencia = null) =>
        new(Guid.CreateVersion7(), "proveedor", entidadId, null, TipoContrato, "contrato.pdf",
            "application/pdf", 2048, new string('b', 64), $"proveedor/{entidadId}/x.pdf", vigencia,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Seed_Siembra_Los_5_Tipos_De_Proveedor_Con_Ids_Deterministas()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();

        var tipos = await db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == "proveedor").OrderBy(t => t.Orden).ToListAsync();

        Assert.Equal(
            "constancia_situacion_fiscal,contrato,acta_constitutiva,identificacion_representante_legal,comprobante_domicilio",
            string.Join(',', tipos.Select(t => t.Codigo)));
        Assert.All(tipos, t => Assert.True(t.Obligatorio && t.Activo));
        Assert.Equal("3,,,,3", string.Join(',', tipos.Select(t => t.VigenciaMeses)));
        Assert.Equal("acta_constitutiva", tipos.Single(t => t.SoloPersonaMoral).Codigo);
        Assert.Equal(TipoContrato, tipos[1].Id);
    }

    [Fact]
    public async Task Roundtrip_Y_Auditoria_Alta_Y_Baja_Agrupan_Por_Entidad_Duena()
    {
        var entidadId = Guid.CreateVersion7();
        var adjunto = Nuevo(entidadId, new DateOnly(2026, 10, 31));

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            db.Adjuntos.Add(adjunto);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var leido = await db.Adjuntos.SingleAsync(a => a.Id == adjunto.Id);
            Assert.Equal(new DateOnly(2026, 10, 31), leido.VigenteHasta);
            Assert.Equal(new string('b', 64), leido.HashSha256);
            Assert.Null(leido.EmpresaId);
            Assert.Equal(EstadoAdjunto.Vencido, leido.EstadoEn(new DateOnly(2026, 11, 1)));

            leido.DarDeBaja("Documento equivocado", Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var baja = await db.Adjuntos.AsNoTracking().SingleAsync(a => a.Id == adjunto.Id);
            Assert.True(baja.EstaDeBaja);
            Assert.Equal("Documento equivocado", baja.BajaMotivo);

            var bitacora = await db.Set<AuditLogEntry>().AsNoTracking()
                .Where(a => a.AggregateRootId == entidadId && a.EntidadId == adjunto.Id)
                .ToListAsync();
            Assert.True(bitacora.Count >= 2, $"Se esperaban alta y baja en bitácora; hay {bitacora.Count}.");
            Assert.All(bitacora, a => Assert.Equal(entidadId, a.AggregateRootId));
        }
    }

    [Fact]
    public async Task Constraint_Rechaza_Baja_Incoherente()
    {
        var entidadId = Guid.CreateVersion7();
        var adjunto = Nuevo(entidadId);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        db.Adjuntos.Add(adjunto);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE compartido.adjuntos SET baja_en = now() WHERE id = {adjunto.Id}"));

        Assert.Contains("ck_adjuntos_baja_coherente", (ex.InnerException ?? ex).Message);
    }
}
