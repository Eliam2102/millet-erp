using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.CuentasPorPagar.Application.NotaCargo;
using Millet.CuentasPorPagar.Application.FacturaProveedor.RevisionRepp;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
using Anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor;
namespace Millet.CuentasPorPagar.UnitTests.P4;
public sealed class SaldosP4Tests
{
    [Fact]
    public async Task Cargo_de_10000_en_factura_de_50000_deja_elegible_40000_y_formaliza_NC_tardia_sin_duplicar()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(total: 50000); f.Autorizar(null, a.UtcNow);
        var cargo = NuevoCargo(a, f.Id, 10000); a.Db.AddRange(f, cargo); await a.Db.SaveChangesAsync();
        var eventos = new Eventos(); var mapper = new PasivoAutorizadoParaPagoMapper(eventos, a.Db, new ElegibilidadFacturaService(a.Db, a));
        await new AplicarNotaCargoHandler(a.Db, a, a, mapper).Handle(new(cargo.Id, cargo.Version), default);
        f.SaldoPendiente.Should().Be(40000); cargo.Estado.Should().Be(EstadoNotaCargo.Aplicada);
        eventos.Items.OfType<Application.Integration.PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(40000);
        var nc = NuevaNc(a, f.Id, 10000); a.Db.Add(nc); await a.Db.SaveChangesAsync();
        await new FormalizacionNotaCargoService(a.Db, new Notificaciones()).IntentarAsync(nc, a.UtcNow, default);
        cargo.Estado.Should().Be(EstadoNotaCargo.Formalizada); f.SaldoPendiente.Should().Be(40000);
    }
    [Fact]
    public async Task Anticipo_USD_en_factura_MXN_se_rechaza_sin_amortizar()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); var anticipo = NuevoAnticipo(a, "USD");
        a.Db.AddRange(f, anticipo); await a.Db.SaveChangesAsync();
        var mapper = new PasivoAutorizadoParaPagoMapper(new Eventos(), a.Db, new ElegibilidadFacturaService(a.Db, a));
        Func<Task> aplicar = async () => await new AplicarAnticipoAFacturaHandler(a.Db, a, mapper).Handle(new(f.Id, f.Version, anticipo.Id, anticipo.Version, 10), default);
        await aplicar.Should().ThrowAsync<BusinessRuleException>(); anticipo.MontoAmortizado.Should().Be(0); f.SaldoPendiente.Should().Be(1000);
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task UUID_no_puede_reutilizarse_entre_tipos(int tipo)
    {
        await using var a = new P8Fixture(); var anticipo = NuevoAnticipo(a); a.Db.Add(anticipo); await a.Db.SaveChangesAsync();
        if (tipo == 1) a.Db.Add(Nc.Capturar(a.Current!.Value, null, anticipo.UuidCfdi.ToLowerInvariant(), a.Proveedor, "FIX", null, a.UtcNow, "MXN", null, 100, 0, 0, 100, TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), null, null, a.UtcNow));
        else { var f = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(a.Current!.Value,null,anticipo.UuidCfdi,a.Proveedor,a.Sucursal,"FIX",null,a.UtcNow,a.UtcNow,new(2026,11,1),"MXN",null,100,0,0,0,100,"FIX",a.UtcNow); a.Db.Add(f); }
        Func<Task> guardar = async () => await a.Db.SaveChangesAsync();
        await guardar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_UUID_DUPLICADO");
    }
    [Theory] [InlineData(TipoDocumentoP4.Anticipo)] [InlineData(TipoDocumentoP4.NotaCredito)] [InlineData(TipoDocumentoP4.NotaCargo)]
    public async Task Cancelar_documento_exige_motivo_y_persiste(TipoDocumentoP4 tipo)
    {
        await using var a = new P8Fixture(); Millet.SharedKernel.Domain.BaseEntity documento = tipo switch {
            TipoDocumentoP4.Anticipo => NuevoAnticipo(a), TipoDocumentoP4.NotaCredito => NuevaNc(a, null,100), _ => NuevoCargo(a,Guid.NewGuid(),100) };
        a.Db.Add(documento); await a.Db.SaveChangesAsync();
        new CancelarDocumentoP4Validator().Validate(new CancelarDocumentoP4Command(tipo,documento.Id,documento.Version, "")).IsValid.Should().BeFalse();
        await new CancelarDocumentoP4Handler(a.Db,a).Handle(new(tipo,documento.Id,documento.Version,"FIX captura incorrecta"),default);
        a.Db.ChangeTracker.Clear();
        if(tipo==TipoDocumentoP4.Anticipo) (await a.Db.AnticiposProveedor.FindAsync(documento.Id))!.MotivoCancelacion.Should().Be("FIX captura incorrecta");
        if(tipo==TipoDocumentoP4.NotaCredito) (await a.Db.NotasCreditoProveedor.FindAsync(documento.Id))!.MotivoCancelacion.Should().Be("FIX captura incorrecta");
        if(tipo==TipoDocumentoP4.NotaCargo) (await a.Db.NotasCargo.FindAsync(documento.Id))!.MotivoCancelacion.Should().Be("FIX captura incorrecta");
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Falta_REPP_al_sexto_habil_con_fin_de_semana_y_festivo_se_libera_con_cobertura_total(bool pagada)
    {
        await using var a = new P8Fixture(); var f = a.Nueva(total: pagada ? 100 : 1000); f.AsignarMetodoPago("PPD"); f.Autorizar(null,a.UtcNow);
        f.RegistrarPago(100, a.UtcNow,"FIX"); var pago = new PagoProveedorLocal(a.Current!.Value,f.Id,Guid.NewGuid(),100,new(2026,9,11));
        a.Db.AddRange(f,pago); await a.Db.SaveChangesAsync(); var calendario = new Calendario();
        a.UtcNow=new(2026,9,21,12,0,0,TimeSpan.Zero);
        (await new RevisarFaltaReppHandler(a.Db,a,calendario).Handle(new(),default)).Should().Be(0);
        a.UtcNow=a.UtcNow.AddDays(1);
        (await new RevisarFaltaReppHandler(a.Db,a,calendario).Handle(new(),default)).Should().Be(1); f.Estado.Should().Be(EstadoPasivo.EnRevision);
        var handler = new ReppProveedorRecibidoHandler(a.Db,a,NullLogger<ReppProveedorRecibidoHandler>.Instance);
        await handler.Handle(new(Guid.NewGuid(),new(a.Current!.Value,a.UtcNow,f.Id,Guid.NewGuid().ToString(),a.UtcNow,[new(pago.PagoId,40)])),default);
        f.Estado.Should().Be(EstadoPasivo.EnRevision); f.ReppRecibido.Should().BeFalse();
        await handler.Handle(new(Guid.NewGuid(),new(a.Current!.Value,a.UtcNow,f.Id,Guid.NewGuid().ToString(),a.UtcNow,[new(pago.PagoId,60)])),default);
        f.Estado.Should().Be(pagada ? EstadoPasivo.Pagada : EstadoPasivo.Autorizada); f.ReppRecibido.Should().BeTrue(); f.MotivoRevisionId.Should().BeNull();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NC_07_amortiza_anticipo_sin_duplicar_la_aplicacion_interna(bool aplicadaInternamente)
    {
        await using var a=new P8Fixture(); var anticipo=NuevoAnticipo(a);
        var nc=Nc.Capturar(a.Current!.Value,null,Guid.NewGuid().ToString(),a.Proveedor,"FIX",null,a.UtcNow,"MXN",null,100,0,0,100,TipoNotaCredito.Descuento,TipoRelacionCfdi.AmortizacionAnticipo,anticipo.UuidCfdi,null,null,a.UtcNow);
        if (aplicadaInternamente) anticipo.Amortizar(100,a.UtcNow);
        a.Db.AddRange(anticipo,nc); await a.Db.SaveChangesAsync();
        await new AmortizarAnticipoConNcHandler(a.Db,a).Handle(new(anticipo.Id,anticipo.Version,nc.Id,nc.Version,100),default);
        anticipo.SaldoAmortizable.Should().Be(0); nc.SaldoPorAplicar.Should().Be(0); nc.AnticipoOrigenId.Should().Be(anticipo.Id);
    }
    [Fact]
    public async Task Serie_anticipo_por_proveedor_usa_FANT_por_omision_y_normaliza_configuracion()
    {
        await using var a=new P8Fixture();var lector=new Application.AnticipoProveedor.ObtenerSerieAnticipoHandler(a.Db);
        (await lector.Handle(new(a.Proveedor),default)).Should().Be("FANT");
        await new Application.AnticipoProveedor.ConfigurarSerieAnticipoHandler(a.Db,a,a).Handle(new(a.Proveedor,"fant-p4"),default);
        (await lector.Handle(new(a.Proveedor),default)).Should().Be("FANT-P4");
        (await lector.Handle(new(Guid.NewGuid()),default)).Should().Be("FANT");
    }
    private static Anticipo NuevoAnticipo(P8Fixture a,string moneda="MXN")=>Anticipo.Capturar(a.Current!.Value,null,Guid.NewGuid().ToString(),a.Proveedor,"FANT","FIX",a.UtcNow,moneda,null,100,null,null,a.UtcNow);
    private static Cargo NuevoCargo(P8Fixture a,Guid f,decimal monto) { var n=Cargo.Crear(a.Current!.Value,FolioInternoNotaCargo.FromAnioSecuencial(2026,1),a.Proveedor,a.Sucursal,"FIX",null,monto,"MXN",null,f,null,null,a.UtcNow); n.Autorizar(null,a.UtcNow); return n; }
    private static Nc NuevaNc(P8Fixture a,Guid? f,decimal monto)=>Nc.Capturar(a.Current!.Value,null,Guid.NewGuid().ToString(),a.Proveedor,"FIX",null,a.UtcNow,"MXN",null,monto,0,0,monto,TipoNotaCredito.Devolucion,TipoRelacionCfdi.Devolucion,Guid.NewGuid().ToString(),f,null,a.UtcNow);
    private sealed class Eventos:IIntegrationEventPublisher { public List<object> Items{get;}=[]; public Task PublishAsync(object e,CancellationToken ct=default){Items.Add(e);return Task.CompletedTask;} }
    private sealed class Notificaciones:IPublisher { public Task Publish(object e,CancellationToken ct=default)=>Task.CompletedTask; public Task Publish<T>(T e,CancellationToken ct=default) where T:INotification=>Task.CompletedTask; }
    private sealed class Calendario:ICalendarioHabil {
        public Task<int> ContarDiasAsync(DateOnly desde,DateOnly hasta,CancellationToken ct)=>Task.FromResult(CalendarioHabil.ContarDias(desde,hasta,new HashSet<DateOnly>{new(2026,9,16)}));
        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha,int horas,CancellationToken ct)=>throw new NotSupportedException(); }
}
