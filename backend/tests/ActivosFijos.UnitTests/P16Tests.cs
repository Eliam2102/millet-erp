using ClosedXML.Excel;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class P16Tests
{
    private static DatosActivo Datos(Guid clase, decimal? contable = null, decimal? fiscal = null) => new("Prueba P16", clase, null, null, null,
        new(2026,1,1), new(2026,1,1), 28594.44m, "MXN", 1, new(Guid.NewGuid(),Guid.NewGuid(),"Prueba",Guid.NewGuid(),null),
        TipoImportacionActivo.No,null,false,"Prueba", TasaContableAnual:contable,TasaFiscalAnual:fiscal);
    [Theory] [InlineData(false,238.29)] [InlineData(true,595.75)]
    public void Tasa_propia_independiente_de_fiscal(bool propia, decimal mensual)
    {
        var c = new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"153","Maquinaria",10);
        var a = new Activo(Guid.NewGuid(),c.EmpresaId,"ACF99575",Datos(c.Id,propia ? 25.0014m : null)); a.Capitalizar(Guid.NewGuid());
        Assert.Equal(mensual,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);
        Assert.Equal(10,TasasActivo.Resolver(a,c).Fiscal);
    }
    [Theory] [InlineData(-1)] [InlineData(101)] [InlineData(25.00145)]
    public void Tasa_propia_invalida(decimal tasa) => Assert.Throws<BusinessRuleException>(() => new Activo(Guid.NewGuid(),Guid.NewGuid(),"P16",Datos(Guid.NewGuid(),tasa)));
    [Fact] public void SAP_alias_sin_acumulada_y_subtotal()
    {
        var t = ArchivoLibro.LeerCsv("Clase AF,Origen,Estatus,Núm.AF,Descripción de activo,Fecha de adquisición (poner día 1º del mes),Monto original de\\nla inversión,Depreciación normal\nMAQ Y EQUI,,,,,,,100\nMAQ Y EQUI,Importado,Propio,ACF99575,FORKLIFT,2025-07-01,28594.44,595.75");
        Assert.Empty(t.Errores); Assert.Single(t.Filas); Assert.Equal("595.75",t.Filas[0].Valores[15]);
    }
    [Fact] public void Corrida_sin_tasa_nombra_activo()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => CorridaDepreciacion.ValidarTasas([new() { Numero="SIN-TASA",Tasa=null }]));
        Assert.Equal("AF_CORRIDA_SIN_TASA",ex.Code); Assert.Contains("SIN-TASA",ex.Message);
        CorridaDepreciacion.ValidarTasas([new() { Numero="AVISO",Tasa=10,Observacion="Tasa por confirmar con Millet" }]);
    }
    [Fact] public void Recorte_real_lee_Base_y_los_cuatro_activos()
    {
        using var stream=typeof(P16Tests).Assembly.GetManifestResourceStream("Millet.ActivosFijos.UnitTests.Recursos.SapMilletP16.xlsx")!;
        using var memoria=new MemoryStream(); stream.CopyTo(memoria);
        var tabla=ArchivoLibro.Leer(memoria.ToArray(),"sap.xlsx");
        Assert.Empty(tabla.Errores); Assert.Equal("Base",tabla.Hoja);
        foreach (var numero in new[] { "ACF99575","ACF99620","ACF98840","ACF99215" }) Assert.Single(tabla.Filas,f => f.Valores[0] == numero);
    }
    [Fact] public void Version_autorizada_desde_marzo_y_rechazo_conserva_anterior()
    {
        var c=new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"153","Maquinaria",10);
        var inicial=VersionClaseActivo.Desde(c,1,DateOnly.MinValue,Guid.NewGuid(),true);c.Versiones.Add(inicial);
        var propuesta=VersionClaseActivo.Desde(c,2,new(2026,3,1),Guid.NewGuid());propuesta.TasaContableAnual=25;
        c.Versiones.Add(propuesta);
        Assert.Throws<BusinessRuleException>(() => propuesta.Resolver(propuesta.PropuestoPor,true,null,DateTimeOffset.UtcNow));
        propuesta.Resolver(Guid.NewGuid(),true,null,DateTimeOffset.UtcNow);
        Assert.Equal(10,c.EnMes(new(2026,2,1)).TasaContableAnual);Assert.Equal(25,c.EnMes(new(2026,3,1)).TasaContableAnual);
        var rechazada=VersionClaseActivo.Desde(c,3,new(2026,4,1),Guid.NewGuid()); rechazada.TasaContableAnual=50;c.Versiones.Add(rechazada);
        Assert.Throws<BusinessRuleException>(() => rechazada.Resolver(Guid.NewGuid(),false,"",DateTimeOffset.UtcNow));
        rechazada.Resolver(Guid.NewGuid(),false,"No procede",DateTimeOffset.UtcNow);Assert.Equal(25,c.EnMes(new(2026,5,1)).TasaContableAnual);
        Assert.Throws<BusinessRuleException>(() => propuesta.ValidarVigencia(new(2026,3,1)));
        var a=new Activo(Guid.NewGuid(),c.EmpresaId,"P16",Datos(c.Id));a.Capitalizar(Guid.NewGuid());
        Assert.Equal(238.29m,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);Assert.Equal(595.72m,CalculoDepreciacion.Calcular(a,c,new(2026,3,1)).Importe);
    }
    [Fact] public void E4a_E4d_propuesta_muestra_vigencia_y_exige_motivo()
    {
        var c = new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"153","Maquinaria",10);
        var v = VersionClaseActivo.Desde(c,2,new(2026,3,1),Guid.NewGuid()); c.Versiones.Add(v);
        Assert.Throws<BusinessRuleException>(() => v.CapturarMotivo(" "));
        v.CapturarMotivo("Cambio aprobado en presupuesto");
        var lectura = c.EnMes(new(2026,2,1));
        Assert.True(lectura.PropuestaPendiente); Assert.Equal(v.Vigencia,lectura.ProximaVersionDesde);
        Assert.Equal(10,lectura.TasaContableAnual); Assert.Equal("Cambio aprobado en presupuesto",v.MotivoCambio);
        Assert.Equal("Marzo 2026 ya está depreciado",Assert.Throws<BusinessRuleException>(() => v.ValidarVigencia(new(2026,3,1))).Message);
        v.Vigencia = default;
        Assert.Equal("AF_VIGENCIA_INVALIDA",Assert.Throws<BusinessRuleException>(() => v.ValidarVigencia(new(2026,3,1))).Code);
    }
    [Fact] public void Version_activa_la_depreciacion_desde_su_mes()
    {
        var c=new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"153","Maquinaria",10);
        c.Editar(c.Nombre,10,null,null,null,null,true,false,true,10);
        c.Versiones.Add(VersionClaseActivo.Desde(c,1,DateOnly.MinValue,Guid.NewGuid(),true));
        var v=VersionClaseActivo.Desde(c,2,new(2026,3,1),Guid.NewGuid());v.Depreciable=true;v.Resolver(Guid.NewGuid(),true,null,DateTimeOffset.UtcNow);c.Versiones.Add(v);
        var a=new Activo(Guid.NewGuid(),c.EmpresaId,"P16",Datos(c.Id));a.Capitalizar(Guid.NewGuid());
        Assert.Equal(0,CalculoDepreciacion.Calcular(a,c,new(2026,2,1)).Importe);Assert.Equal(238.29m,CalculoDepreciacion.Calcular(a,c,new(2026,3,1)).Importe);
    }
    [Fact] public void Prefiere_Base_a_Tabla_Resumen()
    {
        using var w=new XLWorkbook();w.AddWorksheet("Tabla Resumen").Cell(1,1).Value="Resumen";var b=w.AddWorksheet("Base");
        string[] encabezados=["Clase AF","Núm.AF","Descripción de activo","Fecha de adquisición (poner día 1º del mes)","Monto original de la inversión"];
        string[] datos=["MAQ Y EQUI","P16-BASE","Equipo","2026-01-01","100"];
        for(var i=0;i<encabezados.Length;i++) {b.Cell(1,i+1).Value=encabezados[i];b.Cell(2,i+1).Value=datos[i];}
        using var stream=new MemoryStream();w.SaveAs(stream);var t=ArchivoLibro.Leer(stream.ToArray(),"sap.xlsx");
        Assert.Empty(t.Errores);Assert.Equal("Base",t.Hoja);Assert.Equal("P16-BASE",Assert.Single(t.Filas).Valores[0]);
    }
}
