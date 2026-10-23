using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class CapitalizacionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Poliza_balanceada_idempotente_y_periodo_rechazado_no_capitaliza(bool cerrado)
    {
        var clase = new ClaseActivo(Guid.NewGuid(),Guid.NewGuid(),"MAQ","Maquinaria",10);
        var cuenta=Guid.NewGuid();var puente=Guid.NewGuid();clase.Editar(clase.Nombre,10,cuenta,Guid.NewGuid(),Guid.NewGuid(),puente,true);
        var a=new Activo(Guid.NewGuid(),clase.EmpresaId,"AF-MAQ-000001",new DatosActivo("Montacargas",clase.Id,null,null,null,new DateOnly(2026,1,1),new DateOnly(2026,1,15),500000,"MXN",1,
            new CustodiaActivo(Guid.NewGuid(),Guid.NewGuid(),"Conkal",Guid.NewGuid(),null),TipoImportacionActivo.No,null,false,"Soporte"));
        var polizas=new Polizas(cerrado);var servicio=new ActivoServicio(null!,null!,null!,null!,null!,polizas,null!,new Centros());
        async Task Capitalizar()
        { a.ValidarCapitalizacion();var id=await servicio.PolizaAsync(a,clase,a.Id,a.InicioUso!.Value,a.ValorOriginal,$"AF-CAP-{a.Id}","Alta",CancellationToken.None);a.Capitalizar(id); }
        if(cerrado)
        { await Assert.ThrowsAsync<BusinessRuleException>(Capitalizar);Assert.Equal(EstadoActivo.PorCapitalizar,a.Estado);Assert.Null(a.PolizaAltaId);return; }
        await Capitalizar();
        var repetida=await servicio.PolizaAsync(a,clase,a.Id,a.InicioUso!.Value,a.ValorOriginal,$"AF-CAP-{a.Id}","Alta",CancellationToken.None);
        Assert.Equal(a.PolizaAltaId,repetida);Assert.Single(polizas.Registradas);var solicitud=polizas.Registradas.Values.Single();
        Assert.Equal($"AF-CAP-{a.Id}",solicitud.LlaveIdempotencia);Assert.Equal(500000,solicitud.Partidas.Sum(x=>x.Cargo));Assert.Equal(500000,solicitud.Partidas.Sum(x=>x.Abono));
        Assert.Equal(cuenta,solicitud.Partidas.Single(x=>x.Cargo>0).CuentaId);Assert.Equal(puente,solicitud.Partidas.Single(x=>x.Abono>0).CuentaId);
        Assert.All(solicitud.Partidas, p => { Assert.Equal(a.CentroCostoId, p.Dim2Id); Assert.Null(p.Dim3Id); });
        await servicio.PolizaAsync(a,clase,Guid.NewGuid(),new DateOnly(2026,2,1),-20000,"AF-NC-prueba","Nota",CancellationToken.None);
        var nc=polizas.Registradas["AF-NC-prueba"];Assert.Equal(cuenta,nc.Partidas.Single(x=>x.Abono==20000).CuentaId);Assert.Equal(puente,nc.Partidas.Single(x=>x.Cargo==20000).CuentaId);
    }
    private sealed class Centros : ICentroCostoContabilidadPort
    {
        public Task<IReadOnlyDictionary<Guid, CentroCostoNodo>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, CentroCostoNodo>>(ids.ToDictionary(id => id, id => new CentroCostoNodo(id, DimensionContable.Dim2, "CE", "Centro", true, true, Guid.NewGuid(), id, "UB", "CE")));
        public Task<IReadOnlyList<CentroCostoNodo>> BuscarAsync(DimensionContable nivel, string? q, AlcanceCentros? alcance, bool incluirInactivos, int limite, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> EnAlcanceDelUsuarioAsync(Guid centroId, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Polizas(bool cerrado):IPolizaContablePort
    {
        private readonly Guid _id=Guid.NewGuid();
        public Dictionary<string,SolicitudPolizaAutomatica> Registradas {get;}=[];
        public Task<PolizaRegistrada> RegistrarAutomaticaAsync(SolicitudPolizaAutomatica solicitud,CancellationToken ct)
        { if(cerrado) throw new BusinessRuleException("PERIODO_CERRADO","El periodo no admite la póliza.");var repetida=Registradas.ContainsKey(solicitud.LlaveIdempotencia);Registradas.TryAdd(solicitud.LlaveIdempotencia,solicitud);return Task.FromResult(new PolizaRegistrada(_id,"AF-1",EstadoPoliza.Autorizada,repetida)); }
        public Task<IReadOnlyList<PolizaRegistrada>> AnularPorDocumentoOrigenAsync(string modulo,string tipo,Guid documentoId,string motivo,CancellationToken ct)=>throw new NotSupportedException();
    }
}
