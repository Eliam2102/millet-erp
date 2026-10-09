using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Stubs;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class ToleranciaProveedorCapturaTests
{
    [Theory]
    [InlineData(5d, 3, false)] // CA2.3
    [InlineData(5d, 6, true)]
    [InlineData(5d, 8, true)] // CA8.6: OC de $10,000
    [InlineData(null, 0.50, false)] // G1.13-a
    [InlineData(null, 1.50, true)]
    [InlineData(0d, 0.01, true)]
    [InlineData(5d, -6, true)]
    [InlineData(5d, 5, false)]
    public async Task Captura_AplicaMontoYRechazoSinForzado(double? monto, double diferencia, bool rechazada)
    {
        using var e = new Entorno();
        await e.PrepararAsync(monto.HasValue ? (decimal)monto.Value : null);
        var respuesta = await e.Handler.Handle(e.Comando(10000m + (decimal)diferencia), default);
        var factura = await e.Cxp.FacturasProveedor.SingleAsync();
        Assert.Equal(rechazada ? EstadoPasivo.Cancelada : EstadoPasivo.Capturada, respuesta.Estado);
        Assert.Equal(monto.HasValue ? (decimal)monto.Value : 0.99m, factura.ToleranciaValor);
        Assert.Equal(ToleranciaTipo.MontoAbsoluto, factura.ToleranciaTipo);
        Assert.True(e.Eventos.AntesDeGuardar);
        if (rechazada)
        {
            Assert.Equal(MotivoCancelacion.RechazadaPorTolerancia, factura.MotivoDeCancelacion);
            Assert.Single(e.Eventos.Rechazos);
            Assert.Empty(e.Eventos.Registradas);
            var ex = Assert.Throws<BusinessRuleException>(() => factura.Autorizar(Guid.NewGuid(), e.Clock.UtcNow));
            Assert.Equal("FACTURA_NO_AUTORIZABLE", ex.Code);
        }
        else
        {
            Assert.Single(e.Eventos.Registradas);
            Assert.Empty(e.Eventos.Rechazos);
        }
    }

    [Fact]
    public async Task G113d_CambioProveedor_NoRecalculaFacturaAnterior()
    {
        using var e = new Entorno();
        await e.PrepararAsync(5m);
        var primera = await e.Handler.Handle(e.Comando(10003), default);
        e.Proveedor.ActualizarToleranciaFacturaContraOc(1m);
        await e.Maestros.SaveChangesAsync();
        var segunda = await e.Handler.Handle(e.Comando(10003), default);
        e.Cxp.ChangeTracker.Clear();
        var original = await e.Cxp.FacturasProveedor.SingleAsync(f => f.Id == primera.Id);
        Assert.Equal(5m, original.ToleranciaValor);
        Assert.Equal(EstadoPasivo.Capturada, original.Estado);
        Assert.Equal(EstadoPasivo.Cancelada, segunda.Estado);
    }

    [Fact]
    public async Task CambioGeneral_A150_AplicaEnSiguienteFactura_SinCambiarFotoAnterior()
    {
        using var e = new Entorno();
        await e.PrepararAsync(null);
        var primera = await e.Handler.Handle(e.Comando(10001.50m), default);
        var parametro = await e.Maestros.ParametrosGlobales.SingleAsync();
        parametro.ActualizarValor("1.50");
        await e.Maestros.SaveChangesAsync();
        var segunda = await e.Handler.Handle(e.Comando(10001.50m), default);
        Assert.Equal(EstadoPasivo.Cancelada, primera.Estado);
        Assert.Equal(EstadoPasivo.Capturada, segunda.Estado);
        e.Cxp.ChangeTracker.Clear();
        Assert.Equal(0.99m, (await e.Cxp.FacturasProveedor.SingleAsync(f => f.Id == primera.Id)).ToleranciaValor);
        Assert.Equal(1.50m, (await e.Cxp.FacturasProveedor.SingleAsync(f => f.Id == segunda.Id)).ToleranciaValor);
    }

    [Fact]
    public async Task Adaptador_LeeMontoPorIdYRfc_YNullUsaGeneral()
    {
        using var e = new Entorno();
        await e.PrepararAsync(5m);
        var adapter = new ProveedorReadPortAdapter(e.Maestros, e.Empresa);
        var porId = await adapter.ObtenerAsync(e.Proveedor.Id, default);
        var porRfc = await adapter.ObtenerPorRfcAsync(" demo010101aa1 ", default);
        Assert.Equal(porId, porRfc);
        Assert.Equal(5m, porId!.Tolerancia!.Valor);
        Assert.Equal(ToleranciaTipo.MontoAbsoluto, porId.Tolerancia.Tipo);
        e.Proveedor.ActualizarToleranciaFacturaContraOc(null);
        await e.Maestros.SaveChangesAsync();
        Assert.Null((await adapter.ObtenerAsync(e.Proveedor.Id, default))!.Tolerancia);
        Assert.Null(await adapter.ObtenerAsync(Guid.NewGuid(), default));
    }

    private sealed class Entorno : IDisposable
    {
        public EmpresaContext Empresa { get; } = new();
        public Reloj Clock { get; } = new();
        public CompartidoDbContext Maestros { get; }
        public CuentasPorPagarDbContext Cxp { get; }
        public Proveedor Proveedor { get; } = new(Guid.NewGuid(), "DEMO-TOL", "Proveedor DEMO tolerancia", "DEMO010101AA1", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        public CapturarFacturaConOcHandler Handler { get; }
        public EventosGrabador Eventos { get; }
        private readonly OcPort _oc;
        private readonly ServiceProvider _services;

        public Entorno()
        {
            Maestros = new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Empresa);
            Cxp = new(new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Empresa);
            _oc = new(new(Guid.NewGuid(), "OC-DEMO-TOL", Proveedor.Id, Empresa.Current!.Value, Guid.NewGuid(), 10000, "Autorizada", []));
            Eventos = new(Cxp);
            _services = new ServiceCollection()
                .AddSingleton<INotificationHandler<FacturaProveedorRegistradaDomainEvent>>(Eventos)
                .AddSingleton<INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>>(Eventos)
                .BuildServiceProvider();
            Handler = new(Cxp, _oc, new ProveedorReadPortAdapter(Maestros, Empresa), new SinBlobs(), new Millet.CuentasPorPagar.Infrastructure.Parsing.XmlCfdiParser(),
                Empresa, new Mediator(_services), Clock, new ToleranciaGeneralReadPortAdapter(Maestros));
        }

        public async Task PrepararAsync(decimal? monto)
        {
            Proveedor.ActualizarToleranciaFacturaContraOc(monto);
            Maestros.Proveedores.Add(Proveedor);
            Maestros.ParametrosGlobales.Add(ParametroGlobal.CrearDeModulo(Guid.NewGuid(), ToleranciaFacturaContraOcParametro.Clave,
                "0.99", TipoParametro.Numero, "cxp", "Tolerancia DEMO"));
            await Maestros.SaveChangesAsync();
        }

        public CapturarFacturaConOcCommand Comando(decimal total) => new(
            _oc.Oc.Id, Proveedor.Id, _oc.Oc.SucursalId, null, null, "DEMO", null,
            Clock.UtcNow, Clock.UtcNow, new DateOnly(2026, 10, 31), "MXN", null,
            total, 0, 0, 0, total, [new(null, null, "Material DEMO", 1, "H87", "Pieza", total, total, null, null, null)]);

        public void Dispose() { Cxp.Dispose(); Maestros.Dispose(); _services.Dispose(); }
    }

    private sealed class EventosGrabador(CuentasPorPagarDbContext db) :
        INotificationHandler<FacturaProveedorRegistradaDomainEvent>, INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>
    {
        public List<FacturaProveedorRegistradaDomainEvent> Registradas { get; } = [];
        public List<FacturaProveedorRechazadaPorToleranciaDomainEvent> Rechazos { get; } = [];
        public bool AntesDeGuardar { get; private set; }
        public Task Handle(FacturaProveedorRegistradaDomainEvent notification, CancellationToken cancellationToken)
        { Registradas.Add(notification); AntesDeGuardar = db.ChangeTracker.Entries().Any(e => e.State == EntityState.Added); return Task.CompletedTask; }
        public Task Handle(FacturaProveedorRechazadaPorToleranciaDomainEvent notification, CancellationToken cancellationToken)
        { Rechazos.Add(notification); AntesDeGuardar = db.ChangeTracker.Entries().Any(e => e.State == EntityState.Added); return Task.CompletedTask; }
    }

    private sealed class OcPort(OrdenCompraDto oc) : IComprasOcReadPort
    {
        public OrdenCompraDto Oc => oc;
        public Task<OrdenCompraDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(oc);
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrdenCompraDto>>([oc]);
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero); }
    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Alcance();
        private sealed class Alcance : IDisposable { public void Dispose() { } }
    }

    private sealed class SinBlobs : Millet.CuentasPorPagar.Domain.Cfdi.ICfdiBlobStorage
    {
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fechaCfdi, Stream contenido, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fechaCfdi, Stream? contenido, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
    }
}
