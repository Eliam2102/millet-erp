using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// 86e3tabdp: la migración más reciente (por fecha) es la que debe dejar el modelo igual a la foto. Si una migración nueva queda
/// fechada ANTES de otra ya integrada, o su Designer se generó sobre un modelo viejo, la última migración ya no coincide con la foto.
/// </summary>
public sealed class UltimaMigracionTests
{
    [Fact]
    public void El_Designer_de_la_ultima_migracion_coincide_con_la_foto_del_modelo()
    {
        using var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only").UseSnakeCaseNamingConvention().Options, new Empresa());
        var migraciones = db.GetService<IMigrationsAssembly>();
        var ultima = migraciones.Migrations.OrderBy(m => m.Key, StringComparer.Ordinal).Last();
        var destino = Preparar(db, migraciones.CreateMigration(ultima.Value, db.Database.ProviderName!).TargetModel);
        var foto = Preparar(db, migraciones.ModelSnapshot!.Model);

        db.GetService<IMigrationsModelDiffer>().HasDifferences(destino.GetRelationalModel(), foto.GetRelationalModel())
            .Should().BeFalse($"el Designer de {ultima.Key} debe ser idéntico a AlmacenDbContextModelSnapshot");
    }

    private static IModel Preparar(DbContext db, IModel modelo)
    {
        if (modelo is IMutableModel mutable) modelo = mutable.FinalizeModel();
        return db.GetService<IModelRuntimeInitializer>().Initialize(modelo);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
