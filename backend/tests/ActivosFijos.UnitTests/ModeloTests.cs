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
        foreach(var type in new[]{typeof(Activo),typeof(ClaseActivo),typeof(TrasladoActivo),typeof(MovimientoValorActivo),typeof(CorridaDepreciacion),typeof(OperacionLibro),typeof(ConfiguracionLibro),typeof(CargaLibro),typeof(DocumentoActivo),typeof(MantenimientoActivo),typeof(ConteoActivo),typeof(PartidaConteoActivo),typeof(AvisoActivo)})
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
    private sealed class Empresa:ICurrentEmpresaContext {public Guid? Current=>Guid.Empty; public bool IsBypassed=>false; public IDisposable Bypass()=>throw new NotSupportedException();}
}
