using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarNotaCredito;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.NotaCargo;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.Exceptions;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
namespace Millet.CuentasPorPagar.UnitTests.P8;
public sealed class AplicacionesP8Tests
{
    private sealed class Publicador(CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext db) : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default) {
            // El evento se publica mientras el movimiento aún no se ha persistido (ADR-0009).
            db.MovimientosPasivo.Count().Should().Be(0);
            Eventos.Add(integrationEvent); return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Cargo_descuenta_pasivo_autorizado_y_publica_elegible_antes_de_SaveChanges()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); f.Autorizar(null, f.FechaDocumento);
        var cargo = Cargo.Crear(a.Current!.Value, FolioInternoNotaCargo.FromAnioSecuencial(2026, 1), a.Proveedor, a.Sucursal,
            "FIX cargo", null, 100, "MXN", null, f.Id, null, null, a.UtcNow); cargo.Autorizar(null, a.UtcNow);
        a.Db.AddRange(f, cargo); await a.Db.SaveChangesAsync();
        var pub = new Publicador(a.Db); var mapper = new PasivoAutorizadoParaPagoMapper(pub, a.Db, new ElegibilidadFacturaService(a.Db, a));
        await new AplicarNotaCargoHandler(a.Db, a, a, mapper).Handle(new(cargo.Id, cargo.Version), default);
        f.SaldoPendiente.Should().Be(900); f.NcAplicadasTotal.Should().Be(100);
        (await a.Db.MovimientosPasivo.SingleAsync()).Tipo.Should().Be(TipoMovimientoPasivo.NotaCargo);
        pub.Eventos.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(900);
    }
    [Fact]
    public async Task Anticipo_aplicado_actualiza_elegible_autorizado_antes_de_guardar()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); f.Autorizar(null, f.FechaDocumento);
        var anticipo = Domain.AnticipoProveedor.AnticipoProveedor.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(),
            a.Proveedor, "FANT", "FIX-P8", a.UtcNow, "MXN", null, 100, null, null, a.UtcNow);
        a.Db.AddRange(f, anticipo); await a.Db.SaveChangesAsync();
        var pub = new Publicador(a.Db); var mapper = new PasivoAutorizadoParaPagoMapper(pub, a.Db, new ElegibilidadFacturaService(a.Db, a));
        await new AplicarAnticipoAFacturaHandler(a.Db, a, mapper).Handle(new(f.Id, f.Version, anticipo.Id, anticipo.Version, 100), default);
        pub.Eventos.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(900);
        (await a.Db.MovimientosPasivo.SingleAsync()).DocumentoId.Should().Be(anticipo.Id);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NC_formalizada_no_duplica_cargo_solo_si_el_movimiento_consta_aplicado(bool historialCargo)
    {
        await using var a = new P8Fixture(); var f = a.Nueva();
        var nc = Nc.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, "FIX", null, a.UtcNow,
            "MXN", null, 100, 0, 0, 100, TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), f.Id, null, a.UtcNow);
        var cargo = Cargo.Crear(a.Current.Value, FolioInternoNotaCargo.FromAnioSecuencial(2026, 1), a.Proveedor, a.Sucursal,
            "FIX cargo", null, 50, "MXN", null, f.Id, null, null, a.UtcNow);
        cargo.Autorizar(null, a.UtcNow); cargo.Aplicar(null, a.UtcNow); cargo.Formalizar(nc.Id, a.UtcNow);
        if (historialCargo) f.AplicarNotaCargo(50, cargo.Id, new(2026, 10, 9));
        a.Db.AddRange(f, nc, cargo); await a.Db.SaveChangesAsync();
        var mapper = new PasivoAutorizadoParaPagoMapper(new Publicador(a.Db), a.Db, new ElegibilidadFacturaService(a.Db, a));
        var handler = new AplicarNotaCreditoAFacturaHandler(a.Db, mapper, a);
        await handler.Handle(new(f.Id, f.Version, nc.Id, nc.Version, 50), default);
        f.NcAplicadasTotal.Should().Be(50); f.SaldoPendiente.Should().Be(950);
        await handler.Handle(new(f.Id, f.Version, nc.Id, nc.Version, 50), default);
        f.NcAplicadasTotal.Should().Be(100); f.SaldoPendiente.Should().Be(900);
    }
    [Fact]
    public async Task Historial_anterior_sin_fecha_no_inventa_saldos_del_corte()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); f.AplicarNotaCredito(100, new(2026, 10, 9));
        a.Db.Add(f); await a.Db.SaveChangesAsync(); a.Db.MovimientosPasivo.RemoveRange(a.Db.MovimientosPasivo);
        await a.Db.SaveChangesAsync(); a.Db.ChangeTracker.Clear();
        Func<Task> leer = async () => await a.Lector.LeerAsync(new(2026, 9, 30), null, null, null, default);
        await leer.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_HISTORICO_POR_CONFIRMAR");
    }
}
