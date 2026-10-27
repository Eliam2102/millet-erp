using Millet.ActivosFijos.Domain;
namespace Millet.ActivosFijos.UnitTests;
public sealed class TasasFiscalTests
{
    private static (Activo, ClaseActivo) Crear(string clave = "154", decimal valor = 500000, int? vida = null, Guid? mejorado = null, int mesUso = 1)
    {
        var c = new ClaseActivo(Guid.NewGuid(), Guid.NewGuid(), clave, clave, 25);
        c.Editar(clave, 25, null, null, null, null, true, depreciable: true, tasaConfirmada: false, tasaContable: 10);
        var a = new Activo(Guid.NewGuid(), c.EmpresaId, "FICTICIO-P12", new("FICTICIO", c.Id, null, null, null, new(2026,1,1), new(2026,mesUso,15), valor, "MXN", 1,
            new(Guid.NewGuid(), Guid.NewGuid(), "FICTICIO", Guid.NewGuid(), null), TipoImportacionActivo.No, null, false, "FICTICIO", mejorado, vida));
        a.Capitalizar(Guid.NewGuid()); return (a,c);
    }
    [Fact] public void Tasas_separadas_e_INPC_ficticio()
    {
        var (a,c) = Crear();
        Assert.Equal(4166.67m, CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);
        var f = CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal> { [new(2026,1,1)] = 100, [new(2026,6,1)] = 103 });
        Assert.Equal(11,f.MesesUso); Assert.Equal(114583.33m,f.DeduccionHistorica); Assert.Equal(1.0300m,f.Factor); Assert.Equal(118020.83m,f.DeduccionActualizada);
    }
    [Fact] public void Baja_junio_hasta_mayo_y_sin_INPC()
    {
        var (a,c) = Crear(); a.DarDeBaja(new(2026,6,1));
        var f = CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal>());
        Assert.Equal(4,f.MesesUso); Assert.Null(f.Factor); Assert.Null(f.DeduccionActualizada); Assert.Contains("INPC faltante",f.Observacion);
    }
    [Fact] public void Periodo_par_marzo_diciembre_usa_julio_y_factor_minimo_uno()
    {
        var (a,c) = Crear(mesUso:2);
        var f = CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal> { [new(2026,1,1)] = 100, [new(2026,7,1)] = 99 });
        Assert.Equal(10,f.MesesUso); Assert.Equal(1m,f.Factor);
    }
    [Fact] public void Computo_contable_33_fiscal_30()
    {
        var (a,c) = Crear("156",36000); c.Editar(c.Nombre,30,null,null,null,null,true,depreciable:true,tasaConfirmada:false,tasaContable:33);
        Assert.Equal(990,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);
        Assert.Equal(9900,CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal>()).DeduccionHistorica);
    }
    [Fact] public void Actualizacion_multiplica_la_deduccion_historica_redondeada()
    {
        var (a,c) = Crear(valor: 0.67m);
        var f = CalculoFiscal.Calcular(a,c,2026,2,false,new Dictionary<DateOnly,decimal> { [new(2026,1,1)] = 100, [new(2026,2,1)] = 110 });
        Assert.Equal(0.01m,f.DeduccionHistorica); Assert.Equal(1.1m,f.Factor); Assert.Equal(0.01m,f.DeduccionActualizada);
    }
    [Fact] public void Junio_faltante_deja_sin_factor_y_sin_deduccion_actualizada()
    {
        var (a,c) = Crear();
        var f = CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal> { [new(2026,1,1)] = 100 });
        Assert.Null(f.Factor); Assert.Null(f.DeduccionActualizada); Assert.Contains("INPC faltante", f.Observacion);
    }
    [Theory] [InlineData("151")] [InlineData("159")]
    public void No_depreciables_en_ambos_libros(string clave)
    {
        var (a,c) = Crear(clave); c.Editar(clave,25,null,null,null,null,true,depreciable:false,tasaConfirmada:false,tasaContable:10);
        Assert.Equal(0,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);
        Assert.Equal(0,CalculoFiscal.Calcular(a,c,2026,12,false,new Dictionary<DateOnly,decimal>()).DeduccionHistorica);
    }
    [Fact] public void Fiscal_se_limita_al_valor_original_sin_usar_la_acumulada_contable()
    {
        var (a,c) = Crear(); a.Depreciar(499999);
        var f = CalculoFiscal.Calcular(a,c,2035,12,false,new Dictionary<DateOnly,decimal>());
        Assert.Equal(500000,f.DeduccionAcumuladaHistorica); Assert.Equal(0,f.DeduccionHistorica);
    }
    [Fact] public void Vida_util_y_mejora_resuelven_tasas()
    {
        var (a,c) = Crear("160",12000,60);
        Assert.Equal(200,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);
        var (sin,cs) = Crear("160",12000);
        Assert.Equal("Capture la vida útil",CalculoDepreciacion.Calcular(sin,cs,new(2026,2,1)).Observacion);
        var (baseActivo,baseClase) = Crear(); baseClase.Editar("Base",25,null,null,null,null,true,depreciable:true,tasaConfirmada:false,tasaContable:25);
        var (mejora,cm) = Crear("170",60000,mejorado:baseActivo.Id);
        Assert.Equal(1250,CalculoDepreciacion.Calcular(mejora,cm,new(2026,2,1),claseMejorada:baseClase).Importe);
        Assert.Equal(0,CalculoDepreciacion.Calcular(mejora,cm,new(2026,2,1)).Importe);
        Assert.Equal("Indique el activo que mejora",CalculoDepreciacion.Calcular(mejora,cm,new(2026,2,1)).Observacion);
    }
}
