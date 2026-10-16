using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.UnitTests;
public sealed class ModeloPersistenceTests
{
    [Fact]
    public void Modelo_tiene_tenant_auditoria_concurrencia_y_oc_unica()
    {
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>().UseNpgsql("Host=invalid;Database=ce_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        foreach (var type in new[]
        {
            typeof(Millet.ComercioExterior.Domain.Expediente),
            typeof(Millet.ComercioExterior.Domain.DocumentoExpediente),
            typeof(Millet.ComercioExterior.Domain.ParticipanteExpediente)
        }

        )
        {
            Assert.True(typeof(IAuditable).IsAssignableFrom(type));
            Assert.True(typeof(IPerteneceAEmpresa).IsAssignableFrom(type));
            var entity = db.Model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        }

        Assert.Contains(db.Model.FindEntityType(typeof(Millet.ComercioExterior.Domain.Expediente))!.GetIndexes(), i => i.GetDatabaseName() == "ux_ce_oc_abierta" && i.IsUnique);
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
