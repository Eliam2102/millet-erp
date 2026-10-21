using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.UnitTests;
public sealed class ModeloPersistenceTests
{
    private static readonly string[] ClaveAviso = ["HitoId", "FechaCompromiso"];

    [Fact]
    public void Modelo_tiene_tenant_auditoria_concurrencia_y_oc_unica()
    {
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>().UseNpgsql("Host=invalid;Database=ce_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        foreach (var type in new[]
        {
            typeof(Millet.ComercioExterior.Domain.Expediente),
            typeof(Millet.ComercioExterior.Domain.GastoExpediente),
            typeof(Millet.ComercioExterior.Domain.PartidaCosto),
            typeof(Millet.ComercioExterior.Domain.DistribucionGasto),
            typeof(Millet.ComercioExterior.Domain.AnticipoAgente),
            typeof(Millet.ComercioExterior.Domain.ConceptoGasto),
            typeof(Millet.ComercioExterior.Domain.DocumentoExpediente),
            typeof(Millet.ComercioExterior.Domain.ParticipanteExpediente),
            typeof(Millet.ComercioExterior.Domain.HitoExpediente),
            typeof(Millet.ComercioExterior.Domain.ContenedorExpediente),
            typeof(Millet.ComercioExterior.Domain.PartidaEmbarcada),
            typeof(Millet.ComercioExterior.Domain.AvisoHitoVencido)
        }

        )
        {
            Assert.True(typeof(IAuditable).IsAssignableFrom(type));
            Assert.True(typeof(IPerteneceAEmpresa).IsAssignableFrom(type));
            var entity = db.Model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        }

        var vinculo = db.Model.FindEntityType(typeof(Millet.ComercioExterior.Domain.VinculoPedidoExpediente))!;
        Assert.Contains(vinculo.GetIndexes(), i => i.IsUnique && i.GetDatabaseName() == "ux_ce_pedido_expediente_abierto" && i.GetFilter() == "activo");
        Assert.Contains(db.Model.FindEntityType(typeof(Millet.ComercioExterior.Domain.Expediente))!.GetIndexes(), i => i.GetDatabaseName() == "ux_ce_oc_abierta" && i.IsUnique);
    }

    [Fact]
    public void Hitos_usan_date_y_avisos_unicos_por_hito_y_compromiso()
    {
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>().UseNpgsql("Host=invalid;Database=ce_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        var h = db.Model.FindEntityType(typeof(Millet.ComercioExterior.Domain.HitoExpediente))!;
        Assert.Equal("date", h.FindProperty("FechaCompromiso")!.GetColumnType());
        Assert.Equal("date", h.FindProperty("FechaReal")!.GetColumnType());
        var aviso = db.Model.FindEntityType(typeof(Millet.ComercioExterior.Domain.AvisoHitoVencido))!;
        Assert.Contains(aviso.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(ClaveAviso));
    }

    [Fact]
    public void Busqueda_oc_traduce_folio_convertido_sin_materializar_el_catalogo()
    {
        using var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>().UseNpgsql("Host=invalid;Database=ce_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        var q = "OC-DEMO2026";
        var sql = Millet.Compras.Infrastructure.PublicAdapters.OcImportacionReadAdapter.CrearConsulta(db, [Guid.NewGuid()], q).ToQueryString();
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => Guid.Empty;
        public bool IsBypassed => false;

        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
