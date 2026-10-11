using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.Tests.PruA;

public sealed class PruAMigracionTests
{
    [Fact]
    public void IngresoSucursal_tiene_migracion_y_snapshot_igual_al_modelo_actual()
    {
        // Solo compara metadata. Nunca abre una conexión.
        using var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseNpgsql("Host=localhost;Database=prua_metadata;Username=prua_metadata")
            .UseSnakeCaseNamingConvention().Options, new FakeEmpresaContext(Guid.NewGuid()));
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Contains("20261011120000_IngresoSucursal", assembly.Migrations.Keys);
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(assembly.ModelSnapshot);
        var esperado = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
        var actual = db.GetService<IDesignTimeModel>().Model;
        var diferencias = db.GetService<IMigrationsModelDiffer>().GetDifferences(esperado.GetRelationalModel(), actual.GetRelationalModel());
        Assert.Empty(diferencias);
    }
}
