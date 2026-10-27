using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.ActivosFijos.UnitTests;
public sealed class ModeloTests
{
    [Fact]
    public void Modelo_aisla_empresa_controla_version_y_rechaza_traslados_simultaneos()
    {
        using var db=new ActivosFijosDbContext(new DbContextOptionsBuilder<ActivosFijosDbContext>().UseNpgsql("Host=invalid;Database=af_test").UseSnakeCaseNamingConvention().Options,new Empresa());
        foreach(var type in new[]{typeof(Activo),typeof(ClaseActivo),typeof(TrasladoActivo),typeof(MovimientoValorActivo),typeof(CorridaDepreciacion),typeof(OperacionLibro),typeof(ConfiguracionLibro),typeof(CargaLibro),typeof(DocumentoActivo),typeof(MantenimientoActivo),typeof(ConteoActivo),typeof(PartidaConteoActivo),typeof(AvisoActivo),typeof(InpcMensual)})
        { var entity=db.Model.FindEntityType(type)!;Assert.NotEmpty(entity.GetDeclaredQueryFilters());Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);Assert.Equal("activos_fijos",entity.GetSchema()); }
        Assert.Contains(db.Model.FindEntityType(typeof(AvisoActivo))!.GetIndexes(),x=>x.IsUnique && x.Properties.Any(p=>p.Name=="FuenteId") && x.Properties.Any(p=>p.Name=="Tipo"));
        Assert.Contains(db.Model.FindEntityType(typeof(TrasladoActivo))!.GetIndexes(),x=>x.IsUnique && x.GetFilter()=="aceptado_en IS NULL");
        Assert.Contains(db.Model.FindEntityType(typeof(Activo))!.GetIndexes(),x=>x.IsUnique && x.Properties.Any(p=>p.Name=="UnidadRecepcion"));
    }
    [Fact]
    public void Bandeja_de_vencimientos_se_traduce_a_SQL_con_union_y_paginacion()
    {
        using var db = new ActivosFijosDbContext(new DbContextOptionsBuilder<ActivosFijosDbContext>().UseNpgsql("Host=invalid;Database=af_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        var sql = Millet.ActivosFijos.Application.ConservacionConsultas.PorVencer(db, [Guid.NewGuid()], new DateOnly(2026, 11, 20)).Skip(10).Take(10).ToQueryString();
        Assert.Contains("UNION ALL", sql); Assert.Contains("LIMIT", sql); Assert.Contains("OFFSET", sql);
    }
    [Fact]
    public void P7_Semilla_contiene_las_13_cuentas_de_detalle_y_tasas_pendientes()
    {
        using var db = new ActivosFijosDbContext(new DbContextOptionsBuilder<ActivosFijosDbContext>().UseNpgsql("Host=invalid;Database=af_test").UseSnakeCaseNamingConvention().Options, new Empresa());
        var modelo = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var semillas = modelo.FindEntityType(typeof(ClaseActivo))!.GetSeedData().ToArray();
        Assert.Equal(13, semillas.Length);
        Assert.Equal("151,152,153,154,155,156,157,159,160,165,166,169,170", string.Join(",", semillas.Select(x => (string)x["Clave"]!).Order()));
        Assert.All(semillas, x => Assert.Equal(true, x["TasaConfirmada"]));
        Assert.Equal(33m, semillas.Single(x => (string)x["Clave"]! == "156")["TasaFiscalAnual"]);
        Assert.Equal(10m, semillas.Single(x => (string)x["Clave"]! == "169")["TasaFiscalAnual"]);
        Assert.Equal("171.18.00.000", CatalogoClasesMillet.Clases.Single(x => x.Clave == "169").Depreciacion);
        Assert.Equal("613.17.00.000.000", CatalogoClasesMillet.Clases.Single(x => x.Clave == "170").Gasto);
        Assert.Null(CatalogoClasesMillet.Clases.Single(x => x.Clave == "165").Gasto);
        Assert.Null(CatalogoClasesMillet.Clases.Single(x => x.Clave == "166").Gasto);
    }
    [Fact]
    public void P7_Migracion_no_borra_clases_ni_activos_y_conserva_depreciacion_anterior()
    {
        var migracion = new Millet.ActivosFijos.Infrastructure.Persistence.Migrations.AF07ClasesCatalogoMillet();
        Assert.DoesNotContain(migracion.UpOperations, x => x is Microsoft.EntityFrameworkCore.Migrations.Operations.DeleteDataOperation
            or Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation);
        var columna = migracion.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().Single(x => x.Name == "depreciable");
        Assert.Equal(true, columna.DefaultValue);
        var sql = string.Join("\n", migracion.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(x => x.Sql));
        Assert.Contains("requiere_revision = EXISTS", sql);
        Assert.Contains("activa = c.activa AND EXISTS", sql);
        Assert.Contains("ct.empresa_id = c.empresa_id", sql);
        Assert.Contains("HAVING count(*) = 1", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() => migracion.DownOperations);
    }
    private sealed class Empresa:ICurrentEmpresaContext {public Guid? Current=>Guid.Empty; public bool IsBypassed=>false; public IDisposable Bypass()=>throw new NotSupportedException();}
}
