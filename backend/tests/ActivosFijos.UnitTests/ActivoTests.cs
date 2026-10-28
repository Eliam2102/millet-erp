using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class ActivoTests
{
    private static readonly Guid Clase = Guid.NewGuid();
    private static readonly Guid Factura = Guid.NewGuid();
    private static DatosActivo Datos(TipoImportacionActivo importacion = TipoImportacionActivo.No, string? pedimento = null) => new("Montacargas DEMO", Clase, "DEMO", "DEMO", "001",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 500000, "MXN", 1,
        new CustodiaActivo(Guid.NewGuid(), Guid.NewGuid(), "Conkal", Guid.NewGuid(), Guid.NewGuid()), importacion, pedimento, false, "Soporte DEMO");
    private static Activo Nuevo() => new(Guid.NewGuid(), Guid.NewGuid(), "AF-MAQ-000001", Datos(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
    private static Activo Capitalizado()
    { var a = Nuevo(); a.ConciliarFactura(Factura, 500000, "MXN", 1); a.Capitalizar(Guid.NewGuid()); return a; }
    [Fact]
    public void Factura_conciliada_asigna_500000_y_repeticion_no_cambia_valor()
    { var a = Nuevo(); a.ConciliarFactura(Factura, 500000, "MXN", 1); a.ConciliarFactura(Factura, 500000, "MXN", 1); Assert.Equal(500000, a.ValorOriginal); Assert.Equal(Factura,a.FacturaProveedorId); }
    [Fact]
    public void Capitalizacion_requiere_factura_y_es_idempotente_con_misma_poliza()
    {
        var a = Nuevo(); Assert.Throws<BusinessRuleException>(a.ValidarCapitalizacion);
        a.ConciliarFactura(Factura, 500000, "MXN", 1); var poliza = Guid.NewGuid(); a.Capitalizar(poliza); a.Capitalizar(poliza);
        Assert.Equal(EstadoActivo.Activo,a.Estado); Assert.Equal(poliza,a.PolizaAltaId);
    }
    [Fact]
    public void Alta_manual_requiere_soporte_y_pedimento_para_importacion()
    {
        Assert.Throws<BusinessRuleException>(() => new Activo(Guid.NewGuid(),Guid.NewGuid(),"SAP-123",Datos() with {DocumentoSoporte=null}));
        Assert.Throws<BusinessRuleException>(() => new Activo(Guid.NewGuid(),Guid.NewGuid(),"SAP-123",Datos(TipoImportacionActivo.Definitivo)));
        var a = new Activo(Guid.NewGuid(),Guid.NewGuid(),"SAP-123",Datos(TipoImportacionActivo.Definitivo,"DEMO-PEDIMENTO"));
        Assert.Equal("SAP-123",a.Numero); Assert.Equal(TipoImportacionActivo.Definitivo,a.Importacion); Assert.Equal("DEMO-PEDIMENTO",a.Pedimento);
    }
    [Fact]
    public void Traslado_directo_aplica_custodia_sin_aceptacion()
    {
        var a = Capitalizado();
        var destino = a.Custodia() with { Ubicacion = "Cancún" };
        a.Trasladar(Guid.NewGuid(), destino, "Reubicación", new(2026, 2, 15), Guid.NewGuid(), new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(destino, a.Custodia());
        Assert.False(a.EnTraslado);
        Assert.Equal("Reubicación", a.Traslados.Single().Motivo);
        Assert.Equal(new DateOnly(2026, 2, 15), a.Traslados.Single().FechaEfectiva);
        var segundo = destino with { Ubicacion = "Mérida" };
        a.Trasladar(Guid.NewGuid(), segundo, "Regreso", new(2026, 2, 16), Guid.NewGuid(), new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(segundo, a.Custodia());
        Assert.Equal(destino, a.Traslados.Last().Origen);
        Assert.Equal(2, a.Traslados.Count);
        Assert.Equal(a.Traslados.Last().RegistradoEn, a.Traslados.Last().AceptadoEn);
    }
    [Theory]
    [InlineData("motivo")] [InlineData("largo")] [InlineData("capitalizar")] [InlineData("baja")]
    [InlineData("igual")] [InlineData("futura")] [InlineData("inicio")] [InlineData("ultimo")]
    [InlineData("responsable")] [InlineData("sucursal")] [InlineData("centro")] [InlineData("ubicacion")]
    public void Traslado_invalido_no_modifica_custodia(string caso)
    {
        var a = caso == "capitalizar" ? Nuevo() : Capitalizado();
        if (caso == "baja") a.DarDeBaja(new(2026, 2, 1));
        var ahora = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        if (caso == "ultimo") a.Trasladar(Guid.NewGuid(), a.Custodia() with { Ubicacion = "Intermedia" }, "Primero", new(2026, 2, 20), Guid.NewGuid(), ahora);
        var origen = a.Custodia();
        var destino = origen with { Ubicacion = "Destino" };
        destino = caso switch {
            "igual" => origen, "responsable" => destino with { ResponsableId = Guid.Empty },
            "sucursal" => destino with { SucursalId = Guid.Empty }, "centro" => destino with { CentroCostoId = Guid.Empty },
            "ubicacion" => destino with { Ubicacion = " " }, _ => destino };
        var motivo = caso == "motivo" ? " " : caso == "largo" ? new string('x', 301) : "Reubicación";
        var fecha = caso == "futura" ? new DateOnly(2026, 3, 2) : caso == "inicio" ? new(2026, 1, 14) : new(2026, 2, 15);
        var error = Assert.Throws<BusinessRuleException>(() => a.Trasladar(Guid.NewGuid(), destino, motivo, fecha, Guid.NewGuid(), ahora));
        Assert.Contains("AF_TRASLADO_INVALIDO", error.Code);
        Assert.Equal(origen, a.Custodia());
    }
    [Fact]
    public void Nota_credito_20000_deja_480000_e_historial_idempotente()
    {
        var a=Capitalizado(); var nc=Guid.NewGuid(); var poliza=Guid.NewGuid();
        a.Ajustar(nc,Factura,new DateOnly(2026,2,1),20000,true,"NC DEMO",poliza);
        a.Ajustar(nc,Factura,new DateOnly(2026,2,1),20000,true,"NC DEMO",poliza);
        Assert.Equal(480000,a.ValorEnLibros); Assert.Equal(-20000,a.Historial.Single().Importe); Assert.Equal(poliza,a.Historial.Single().PolizaId);
        Assert.Throws<BusinessRuleException>(()=>a.Ajustar(Guid.NewGuid(),Guid.NewGuid(),new DateOnly(2026,2,1),1,true,"Factura ajena",poliza));
        Assert.Throws<BusinessRuleException>(()=>a.Ajustar(Guid.NewGuid(),Factura,new DateOnly(2026,2,1),500001,true,"Exceso",poliza));
    }
    [Fact]
    public void Mejora_aumenta_valor_sin_calcular_depreciacion()
    { var a=Capitalizado();a.Ajustar(Guid.NewGuid(),Guid.NewGuid(),new DateOnly(2026,2,1),10000,false,"Mejora",Guid.NewGuid());Assert.Equal(510000,a.ValorActual);Assert.Equal(0,a.DepreciacionAcumulada); }
    [Fact]
    public void Clase_valida_tasa_y_clave_y_permite_cuentas_demo_vacias()
    { var c=new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"maq","Maquinaria",10);Assert.Equal("MAQ",c.Clave);Assert.Null(c.CuentaActivoId);Assert.Throws<BusinessRuleException>(()=>c.Editar("Maquinaria",101,null,null,null,null,true)); }
}
