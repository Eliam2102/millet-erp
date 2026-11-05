using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class P17Tests
{
    private static Activo Activo()
    {
        var a = new Activo(Guid.NewGuid(), Guid.NewGuid(), "P17", new("Equipo", Guid.NewGuid(), null, null, null,
            new(2026,1,1), new(2026,1,1), 12000, "MXN", 1, new(Guid.NewGuid(), Guid.NewGuid(), "Origen", Guid.NewGuid(), null), TipoImportacionActivo.No, null, false, "Factura"));
        a.Capitalizar(Guid.NewGuid()); return a;
    }
    [Fact] public void Reversa_capitalizacion_devuelve_a_por_capitalizar()
    { var a=Activo(); a.RevertirCapitalizacion(false,false); Assert.Equal(EstadoActivo.PorCapitalizar,a.Estado); Assert.Null(a.PolizaAltaId); }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public void Reversa_capitalizacion_rechaza_dependencias(bool depreciacion,bool disposicion)
    { Assert.Throws<BusinessRuleException>(()=>Activo().RevertirCapitalizacion(depreciacion,disposicion)); }
    [Fact] public void Reversa_capitalizacion_rechaza_depreciacion_y_mejoras()
    { var a=Activo(); a.Depreciar(100); Assert.Contains("depreciación",Assert.Throws<BusinessRuleException>(()=>a.RevertirCapitalizacion(false,false)).Message); }
    [Fact] public void Autorizacion_requiere_otra_persona_y_rechazo_motivo()
    {
        var op=new OperacionLibro { Tipo="ReversaBaja", Motivo="Corrección",RegistradoPor=Guid.NewGuid() };
        Assert.Throws<BusinessRuleException>(()=>op.Validar(op.RegistradoPor,false));
        Assert.Throws<BusinessRuleException>(()=>op.Rechazar(Guid.NewGuid(),"",DateTimeOffset.UtcNow));
        op.Rechazar(Guid.NewGuid(),"No procede",DateTimeOffset.UtcNow); Assert.Equal("No procede",op.MotivoRechazo); Assert.True(op.Anulada);
    }
    [Fact] public void Venta_cancelada_espera_reversa_y_al_reintegrar_respeta_tope()
    {
        var a=Activo(); var c=new ClaseActivo(a.ClaseId,a.EmpresaId,"153","Equipo",10);
        a.DarDeBaja(new(2026,3,1)); a.MarcarVentaCancelada(); Assert.Equal(EstadoActivo.VentaCanceladaPendienteReversa,a.Estado);
        a.Reintegrar(); Assert.Equal(100,CalculoDepreciacion.Calcular(a,c,new(2026,5,1)).Importe);
        a.Depreciar(11950); Assert.Equal(50,CalculoDepreciacion.Calcular(a,c,new(2026,5,1)).Importe);
    }
    [Fact] public void Correccion_liga_original_y_restituye_custodia()
    {
        var a=Activo(); var origen=a.Custodia(); var id=Guid.NewGuid(); var ahora=new DateTimeOffset(2026,5,20,12,0,0,TimeSpan.Zero);
        a.Trasladar(id,origen with { Ubicacion="Destino" },"Traslado",new(2026,5,1),Guid.NewGuid(),ahora);
        a.CorregirTraslado(id,origen,"Destino erróneo",Guid.NewGuid(),ahora);
        Assert.Equal(origen,a.Custodia()); Assert.True(a.Traslados[0].Corregido); Assert.Equal(id,a.Traslados[1].CorrigeA);
        Assert.Equal(a.Traslados[0].Origen,a.Traslados[1].Origen); Assert.NotSame(a.Traslados[0].Origen,a.Traslados[1].Origen);
        Assert.Equal(origen,CalculoDepreciacion.CustodiaAFecha(a,new(2026,4,30)));
        Assert.Throws<BusinessRuleException>(()=>a.CorregirTraslado(id,origen,"Otra",Guid.NewGuid(),ahora));
    }
    [Fact] public void Impresiones_conservan_historial_y_exigen_motivo_reposicion()
    {
        var a=Activo(); var actor=Guid.NewGuid(); a.RegistrarImpresion(DateTimeOffset.UtcNow,actor,null);
        Assert.Throws<BusinessRuleException>(()=>a.RegistrarImpresion(DateTimeOffset.UtcNow,actor,null));
        a.RegistrarImpresion(DateTimeOffset.UtcNow,actor,"Reposición por daño"); Assert.Equal(2,a.Impresiones.Count);
    }
    [Fact] public void Recaptura_exige_motivo_y_conserva_captura_anterior()
    {
        var a=Activo(); var actor=Guid.NewGuid(); var c=new ConteoActivo(a.EmpresaId,a.SucursalId,null,actor,[a]);
        c.Capturar(a.Numero,ResultadoConteo.Encontrado,null,"Foto 1",actor);
        Assert.Throws<BusinessRuleException>(()=>c.Capturar(a.Numero,ResultadoConteo.NoEncontrado,null,"Foto 2",actor));
        c.Capturar(a.Numero,ResultadoConteo.NoEncontrado,null,"Foto 2",actor,"Error de lectura");
        var previa=Assert.Single(c.Partidas[0].CapturasAnteriores); Assert.Equal("Foto 1",previa.Evidencia); Assert.Equal(actor,previa.CapturadoPor); Assert.Equal(ResultadoConteo.Encontrado,previa.Resultado);
    }
    [Fact] public void Aviso_cerrado_requiere_motivo_y_renovado_vigencia_posterior()
    {
        var a=Activo(); var vence=new DateOnly(2026,5,1); var d=new DocumentoActivo(a,new(TipoDocumentoActivo.Seguro,Guid.NewGuid(),"Póliza","Seguro",new(2026,1,1),new(2026,1,1),vence,null));
        var aviso=new AvisoActivo(a,d.Id,false,TipoAvisoActivo.PorVencer,vence,new(2026,4,1)); var actor=Guid.NewGuid();
        Assert.Throws<BusinessRuleException>(()=>aviso.Resolver("Cerrado","",null,actor,DateTimeOffset.UtcNow));
        Assert.Throws<BusinessRuleException>(()=>aviso.Resolver("Renovado",null,d,actor,DateTimeOffset.UtcNow));
        var nuevo=new DocumentoActivo(a,new(TipoDocumentoActivo.Seguro,Guid.NewGuid(),"Nueva","Seguro",new(2026,1,1),new(2026,1,1),new(2027,5,1),null,d.Id),d);
        aviso.Resolver("Renovado",null,nuevo,actor,DateTimeOffset.UtcNow); Assert.Equal(actor,aviso.ResueltoPor); Assert.NotNull(aviso.ResueltoEn);
    }
}
