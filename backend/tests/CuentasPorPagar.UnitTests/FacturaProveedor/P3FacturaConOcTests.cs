using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Elegibilidad;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarNotaCredito;

namespace Millet.CuentasPorPagar.Tests.P3;

/// <summary>Se ejecuta en memoria como unitaria y, enlazada por Api.IntegrationTests, contra PostgreSQL desechable.</summary>
public sealed class P3FacturaConOcTests
{
#if P3_POSTGRES
    [Fact]
    public async Task Capturas_concurrentes_de_la_misma_OC_no_exceden_la_cantidad_pactada()
    {
        await using var primera = new Ambiente();
        await using var segunda = new Ambiente(compartir: primera);
        var resultados = await Task.WhenAll(
            primera.Handler.Handle(primera.Command(6, 20), default),
            segunda.Handler.Handle(segunda.Command(6, 20), default));
        resultados.Count(r => r.Estado == EstadoPasivo.Capturada).Should().Be(1);
        resultados.Count(r => r.Estado == EstadoPasivo.Cancelada).Should().Be(1);
    }
#endif
    [Theory]
    [InlineData(10, 20, 0, 200, EstadoPasivo.Capturada)]
    [InlineData(10, 20.05, 0, 200.5, EstadoPasivo.Capturada)]
    [InlineData(10, 21, 0, 210, EstadoPasivo.Cancelada)]
    [InlineData(4, 20, 0, 80, EstadoPasivo.Capturada)]
    [InlineData(10, 20, 0, 300, EstadoPasivo.Cancelada)]
    public async Task Captura_concilia_lineas_y_neto(decimal cantidad, decimal precio, decimal retencion, decimal total, EstadoPasivo esperado)
    {
        await using var a = new Ambiente();
        var resultado = await a.Handler.Handle(a.Command(cantidad, precio, total: total, retenciones: retencion), default);
        resultado.Estado.Should().Be(esperado);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == resultado.Id);
        if (esperado == EstadoPasivo.Cancelada)
        {
            factura.MotivoCancelacionTexto.Should().NotBeNullOrEmpty();
            var forzar = () => factura.Autorizar(null, a.Ahora);
            forzar.Should().Throw<BusinessRuleException>();
            a.Eventos.Notificaciones.Should().ContainSingle(n => n is FacturaProveedorRechazadaPorToleranciaDomainEvent);
        }
        a.Eventos.Notificaciones.Should().NotContain(n => n is DiferenciaPrecioFacturaDetectadaDomainEvent);
        var outbox = await a.Db.OutboxEntries.Where(e => e.IntegrationEmpresaId == a.Current).ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].EventType.Should().Be(esperado == EstadoPasivo.Cancelada
            ? "cuentas_por_pagar.factura.rechazada-por-tolerancia.v1" : "cuentas_por_pagar.factura.registrada.v1");
    }

    [Theory]
    [InlineData(true, "OC-P3-FICTICIA")]
    [InlineData(false, null)]
    public async Task Detalle_resuelve_folio_por_puerto_y_tolera_OC_que_no_resuelve(bool disponible, string? folio)
    {
        await using var a = new Ambiente();
        var captura = await a.Handler.Handle(a.Command(10, 20), default);
        a.OcDisponible = disponible;
        var detalle = await new GetFacturaPorIdHandler(a.Db, a, new SucursalDetalleStub(), a.Elegibilidad, a)
            .Handle(new GetFacturaPorIdQuery(captura.Id), default);
        detalle.OrdenCompraFolio.Should().Be(folio);
        detalle.OrdenCompraId.Should().NotBeNull();
        detalle.ProveedorNombre.Should().Be("Proveedor ficticio P3");
        var listado = await new ListarFacturasHandler(a.Db, a, a).Handle(new ListarFacturasQuery(), default);
        listado.Items.Should().ContainSingle().Which.OrdenCompraFolio.Should().Be(folio);
    }

    [Fact]
    public async Task Segunda_factura_no_excede_pendiente_aunque_Compras_aun_no_proyecte_facturacion()
    {
        await using var a = new Ambiente();
        (await a.Handler.Handle(a.Command(6, 20), default)).Estado.Should().Be(EstadoPasivo.Capturada);
        var segunda = await a.Handler.Handle(a.Command(5, 20), default);
        segunda.Estado.Should().Be(EstadoPasivo.Cancelada);
        segunda.MotivoCancelacionTexto.Should().Contain("cantidad facturada").And.Contain("pendiente");
        (await a.Handler.Handle(a.Command(4, 20), default)).Estado.Should().Be(EstadoPasivo.Capturada);
    }

    [Theory]
    [InlineData(32, 33.33, 198.67)] // ISR 10% + 2/3 del IVA
    [InlineData(16, 0, 216)]
    [InlineData(0, 0, 200)]
    public async Task Impuestos_se_validan_con_XML_no_con_el_16_por_ciento_de_OC(decimal iva, decimal retenido, decimal total)
    {
        await using var a = new Ambiente();
        var cfdi = await a.CfdiAsync(200, iva, retenido, total);
        var cmd = a.Command(10, 20, total, iva, retenido) with { CfdiRecibidoId = cfdi.Id, UuidCfdi = cfdi.UuidCfdi.Valor };
        var resultado = await a.Handler.Handle(cmd, default);
        resultado.Estado.Should().Be(EstadoPasivo.Capturada);
        var f = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == resultado.Id);
        if (retenido > 0)
        {
            f.RetencionesDetalle.Should().Contain(r => r.Impuesto == "001" && r.Importe == 20m);
            f.RetencionesDetalle.Should().Contain(r => r.Impuesto == "002" && r.Importe == 13.33m);
        }
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.ConvertidoEnPasivo);
    }

    [Fact]
    public async Task Factura_50_sola_se_cancela_y_CFDi_se_reutiliza_con_NC_30_contra_OC_20()
    {
        await using var a = new Ambiente(cantidadOc: 1, precioOc: 20);
        var cfdi = await a.CfdiAsync(50, 0, 0, 50, cantidad: 1);
        var cmd = a.Command(1, 50) with { CfdiRecibidoId = cfdi.Id, UuidCfdi = cfdi.UuidCfdi.Valor };
        var sola = await a.Handler.Handle(cmd, default);
        sola.Estado.Should().Be(EstadoPasivo.Cancelada);
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
        cfdi.DocumentoDestinoId.Should().BeNull();
        var nc = await a.CfdiAsync(30, 0, 0, 30, egreso: true, relacionado: cfdi.UuidCfdi.Valor, cantidad: 1);
        var juntas = await a.Handler.Handle(cmd with { NotasCredito = [new(nc.Id, [new(a.LineaId, 30)])] }, default);
        juntas.Estado.Should().Be(EstadoPasivo.Capturada);
        juntas.SaldoPendiente.Should().Be(20);
        var aplicada = await a.Db.NotasCreditoProveedor.SingleAsync(n => n.FacturaOrigenId == juntas.Id);
        aplicada.MontoAplicado.Should().Be(30);
        nc.DocumentoDestinoId.Should().Be(aplicada.Id);
        cfdi.DocumentoDestinoId.Should().Be(juntas.Id);
        (await a.Db.OutboxEntries.CountAsync(e => e.IntegrationEmpresaId == a.Current)).Should().Be(3);
    }

    [Fact]
    public async Task NC_de_otra_factura_se_rechaza_sin_consumir_documentos()
    {
        await using var a = new Ambiente(cantidadOc: 1, precioOc: 20);
        var cfdi = await a.CfdiAsync(50, 0, 0, 50, cantidad: 1);
        var nc = await a.CfdiAsync(30, 0, 0, 30, egreso: true, relacionado: Guid.NewGuid().ToString(), cantidad: 1);
        var capturar = () => a.Handler.Handle(a.Command(1, 50) with {
            CfdiRecibidoId = cfdi.Id, UuidCfdi = cfdi.UuidCfdi.Valor,
            NotasCredito = [new(nc.Id, [new(a.LineaId, 30)])] }, default);
        await capturar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_RELACION_INVALIDA");
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
        nc.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
        (await a.Db.FacturasProveedor.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task NC_aplicada_recalcula_elegible_y_persiste_el_evento_en_outbox()
    {
        await using var a = new Ambiente(recibido: 8);
        var captura = await a.Handler.Handle(a.Command(10, 20), default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        factura.Autorizar(null, a.Ahora);
        var nc = Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(a.Current!.Value, null,
            Guid.NewGuid().ToString(), factura.ProveedorId, "NC-P3-FICTICIA", null, a.Ahora, "MXN", null,
            20, 0, 0, 20, Domain.NotaCreditoProveedor.TipoNotaCredito.Descuento,
            Domain.NotaCreditoProveedor.TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), factura.Id, null, a.Ahora);
        a.Db.NotasCreditoProveedor.Add(nc);
        await a.Db.SaveChangesAsync();
        var handler = new AplicarNotaCreditoAFacturaHandler(a.Db, new(a.Eventos, a.Db, a.Elegibilidad), a);
        await handler.Handle(new(factura.Id, factura.Version, nc.Id, nc.Version, 20), default);
        var actualizado = a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single();
        actualizado.SaldoPendiente.Should().Be(144);
        var resultado = await a.Elegibilidad.CalcularAsync(factura, default);
        resultado.Retenido.Should().Be(36);
        (await a.Db.OutboxEntries.AnyAsync(e => e.IntegrationEmpresaId == a.Current &&
            e.EventType == actualizado.EventType)).Should().BeTrue();
    }

    [Fact]
    public async Task No_permite_alterar_impuestos_del_XML_aunque_el_neto_capturado_cuadre()
    {
        await using var a = new Ambiente();
        var cfdi = await a.CfdiAsync(200, 16, 0, 216);
        var resultado = await a.Handler.Handle(a.Command(10, 20, 232, 32) with { CfdiRecibidoId = cfdi.Id, UuidCfdi = cfdi.UuidCfdi.Valor }, default);
        resultado.Estado.Should().Be(EstadoPasivo.Cancelada);
        resultado.MotivoCancelacionTexto.Should().Contain("traslados");
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
    }

    [Fact]
    public async Task G14_oc10_recibido8_facturado10_y_luego_recepcion2_actualiza_Tesoreria_sin_duplicar()
    {
        await using var a = new Ambiente(recibido: 8);
        var respuesta = await a.Handler.Handle(a.Command(10, 20), default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == respuesta.Id);
        var e = await a.Elegibilidad.CalcularAsync(factura, default);
        e.ElegiblePendiente.Should().Be(160); e.Retenido.Should().Be(40);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        // Historial local incluye las primeras 8; Compras se queda temporalmente en 8.
        await a.RecibirAsync(8);
        a.Eventos.Integraciones.Clear();
        var recepcion = a.Recepcion(2);
        await a.Recepciones.Handle(recepcion, default);
        var actualizado = a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single();
        actualizado.SaldoPendiente.Should().Be(200);
        await a.Recepciones.Handle(recepcion, default);
        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Should().ContainSingle();
        a.ActualizarRecibido(10); // Compras consume la recepción; lectura y pago usan esta autoridad.
        (await a.Elegibilidad.CalcularAsync(factura, default)).Retenido.Should().Be(0);
    }

    [Fact]
    public async Task G14_factura_antes_de_mercancia_todo_retenido_y_recepcion_no_se_comparte_entre_facturas()
    {
        await using var a = new Ambiente(recibido: 0);
        var primera = await a.Handler.Handle(a.Command(6, 20), default);
        var segunda = await a.Handler.Handle(a.Command(4, 20), default);
        var f1 = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == primera.Id);
        var f2 = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == segunda.Id);
        (await a.Elegibilidad.CalcularAsync(f1, default)).Retenido.Should().Be(120);
        (await a.Elegibilidad.CalcularAsync(f2, default)).ElegiblePendiente.Should().Be(0);
        await a.RecibirAsync(8);
        a.ActualizarRecibido(8);
        (await a.Elegibilidad.CalcularAsync(f1, default)).ElegiblePendiente.Should().Be(120);
        (await a.Elegibilidad.CalcularAsync(f2, default)).ElegiblePendiente.Should().Be(40);
    }

    private sealed class Ambiente : IAsyncDisposable, ICurrentEmpresaContext, IClock, IComprasOcReadPort, IProveedorReadPort, ICfdiBlobStorage, Millet.CuentasPorPagar.Domain.Ports.Administracion.IToleranciaGeneralReadPort
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
        public DateTimeOffset Ahora { get; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Ahora;
        public Guid LineaId { get; } = Guid.NewGuid();
        public CuentasPorPagarDbContext Db { get; }
        public CapturarFacturaConOcHandler Handler { get; }
        public ElegibilidadFacturaService Elegibilidad { get; }
        public OcRecepcionRegistradaHandler Recepciones { get; }
        public CapturaEventos Eventos { get; } = new();
        public bool OcDisponible { get; set; } = true;
        private OrdenCompraDto _oc;
        private readonly Dictionary<string, string> _xmls = [];
        private readonly ServiceProvider _services;

        public Ambiente(decimal cantidadOc = 10, decimal precioOc = 20, decimal recibido = 10, Ambiente? compartir = null)
        {
            if (compartir is not null) { Current = compartir.Current; LineaId = compartir.LineaId; }
            var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>();
            options.AddInterceptors(new OutboxSaveChangesInterceptor(Eventos.Buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance));
#if P3_POSTGRES
            options.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                ?? throw new InvalidOperationException("Usa tools/validate-integration-isolated.sh"))
                .UseSnakeCaseNamingConvention();
#else
            options.UseInMemoryDatabase($"p3-{Guid.NewGuid()}");
#endif
            Db = new(options.Options, this, new PeriodoPruebaAbierto(), this);
            _oc = new(Guid.NewGuid(), "OC-P3-FICTICIA", Guid.NewGuid(), Current!.Value, Guid.NewGuid(), cantidadOc * precioOc * 1.16m,
                "Autorizada", [new(LineaId, Guid.NewGuid(), cantidadOc, precioOc, 0, recibido)]);
            if (compartir is not null) _oc = compartir._oc;
            _services = new ServiceCollection()
                .AddSingleton<INotificationHandler<FacturaProveedorRegistradaDomainEvent>>(Eventos)
                .AddSingleton<INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>>(Eventos)
                .AddSingleton<INotificationHandler<DiferenciaPrecioFacturaDetectadaDomainEvent>>(Eventos)
                .AddSingleton<INotificationHandler<FacturaProveedorRegistradaDomainEvent>>(new FacturaProveedorRegistradaMapper(Eventos))
                .AddSingleton<INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>>(new FacturaProveedorRechazadaPorToleranciaMapper(Eventos))
                .AddSingleton<INotificationHandler<NotaCreditoProveedorRegistradaDomainEvent>>(new NotaCreditoProveedorRegistradaMapper(Eventos))
                .BuildServiceProvider();
            Handler = new(Db, this, this, this, new XmlCfdiParser(), this, new Mediator(_services), this, this);
            Elegibilidad = new(Db, this);
            Recepciones = new(Db, this, NullLogger<OcRecepcionRegistradaHandler>.Instance, new(Eventos, Db, Elegibilidad));
        }
        public CapturarFacturaConOcCommand Command(decimal cantidad, decimal precio, decimal? total = null, decimal iva = 0, decimal retenciones = 0) =>
            new(_oc.Id, _oc.ProveedorId, _oc.SucursalId, null, null, "P3-FICTICIA", null, Ahora, Ahora, new(2026, 11, 9), "MXN", null,
                cantidad * precio, 0, iva, retenciones, total ?? cantidad * precio + iva - retenciones,
                [new(_oc.Lineas[0].ArticuloId, "30102400", "Material ficticio P3", cantidad, "H87", null, precio, cantidad * precio, null, LineaId, null)]);
        public OcRecepcionRegistradaCommand Recepcion(decimal cantidad) => new(Guid.NewGuid(), new(Current!.Value, Ahora, Guid.NewGuid(), "REC-P3-FICTICIA", _oc.Id,
            new(2026, 10, 9), true, null, null, [new(Guid.NewGuid(), LineaId, _oc.Lineas[0].ArticuloId, "H87", cantidad, 20, cantidad * 20)]));
        public void ActualizarRecibido(decimal cantidad) => _oc = _oc with { Lineas = [_oc.Lineas[0] with { CantidadRecibida = cantidad }] };
        public Task RecibirAsync(decimal cantidad) => Recepciones.Handle(Recepcion(cantidad), default);
        public Task<decimal> ObtenerMontoMxnAsync(CancellationToken cancellationToken) => Task.FromResult(0.99m);
        public async Task<CfdiRecibido> CfdiAsync(decimal subtotal, decimal iva, decimal retenido, decimal total, bool egreso = false, string? relacionado = null, decimal cantidad = 10)
        {
            var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
            var path = $"p3/{uuid}.xml";
            var relaciones = relacionado is null ? "" : $"<cfdi:CfdiRelacionados TipoRelacion=\"01\"><cfdi:CfdiRelacionado UUID=\"{relacionado}\"/></cfdi:CfdiRelacionados>";
            var retenciones = retenido == 0 ? "" : "<cfdi:Retenciones><cfdi:Retencion Impuesto=\"001\" Importe=\"20\"/><cfdi:Retencion Impuesto=\"002\" Importe=\"13.33\"/></cfdi:Retenciones>";
            _xmls[path] = FormattableString.Invariant($"""
                <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="{(egreso ? "E" : "I")}" Fecha="2026-10-09T12:00:00" Moneda="MXN" SubTotal="{subtotal}" Total="{total}">
                {relaciones}<cfdi:Emisor Rfc="AAA010101AAA" Nombre="Proveedor ficticio"/><cfdi:Receptor Rfc="BBB010101BBB" Nombre="Empresa ficticia"/>
                <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="30102400" Cantidad="{cantidad}" ClaveUnidad="H87" Descripcion="Material ficticio P3" ValorUnitario="{subtotal / cantidad}" Importe="{subtotal}"/></cfdi:Conceptos>
                <cfdi:Impuestos TotalImpuestosTrasladados="{iva}" TotalImpuestosRetenidos="{retenido}">{retenciones}</cfdi:Impuestos>
                <cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>
                """);
            var cfdi = CfdiRecibido.Ingresar(Current!.Value, UuidCfdi.Parse(uuid), RfcMexicano.Parse("AAA010101AAA"), RfcMexicano.Parse("BBB010101BBB"),
                egreso ? TipoCfdi.Egreso : TipoCfdi.Ingreso, null, null, Ahora, total, subtotal, iva, retenido, "MXN", null,
                CanalOrigenCfdi.CargaManual, Ahora, path, null, new string('a', 64));
            Db.CfdisRecibidos.Add(cfdi); await Db.SaveChangesAsync(); return cfdi;
        }
        public Task<OrdenCompraDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(OcDisponible ? _oc : null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(OcDisponible && ids.Contains(_oc.Id)
                ? new Dictionary<Guid, string> { [_oc.Id] = _oc.Folio } : new Dictionary<Guid, string>());
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrdenCompraDto>>([_oc]);
        Task<ProveedorDto?> IProveedorReadPort.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorDto?>(new(_oc.ProveedorId, "AAA010101AAA", "Proveedor ficticio P3", null, false, true));
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => ((IProveedorReadPort)this).ObtenerAsync(_oc.ProveedorId, ct);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken ct) => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(_xmls[blobRef])));
        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken ct) => Task.FromResult<Stream?>(null);
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fecha, Stream contenido, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fecha, Stream? contenido, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
#if P3_POSTGRES
            var eventos = Db.EventosProcesados.Local.Select(e => e.Id).ToArray();
            await Db.EventosProcesados.Where(e => eventos.Contains(e.Id)).ExecuteDeleteAsync();
            await Db.NotasCreditoProveedor.ExecuteDeleteAsync();
            await Db.FacturasProveedor.ExecuteDeleteAsync();
            await Db.CfdisRecibidos.ExecuteDeleteAsync();
            await Db.RecepcionesOcLocal.ExecuteDeleteAsync();
            await Db.OutboxEntries.Where(e => e.IntegrationEmpresaId == Current).ExecuteDeleteAsync();
#endif
            await Db.DisposeAsync(); await _services.DisposeAsync();
        }
    }
    private sealed class SucursalDetalleStub : ISucursalReadPort
    {
        public Task<SucursalDto?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<SucursalDto?>(new(id, Guid.NewGuid(), "MID", "Mérida", true));
        public Task<IReadOnlyList<SucursalDto>> ListarPorEmpresaAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SucursalDto>>([]);
    }

    private sealed class CapturaEventos : IIntegrationEventPublisher,
        INotificationHandler<FacturaProveedorRegistradaDomainEvent>, INotificationHandler<FacturaProveedorRechazadaPorToleranciaDomainEvent>,
        INotificationHandler<DiferenciaPrecioFacturaDetectadaDomainEvent>
    {
        public InMemoryIntegrationEventBuffer Buffer { get; } = new();
        public List<object> Integraciones { get; } = [];
        public List<INotification> Notificaciones { get; } = [];
        public Task PublishAsync(object e, CancellationToken ct) { Integraciones.Add(e); Buffer.Enqueue((IntegrationEvent)e); return Task.CompletedTask; }
        public Task Handle(FacturaProveedorRegistradaDomainEvent e, CancellationToken ct) { Notificaciones.Add(e); return Task.CompletedTask; }
        public Task Handle(FacturaProveedorRechazadaPorToleranciaDomainEvent e, CancellationToken ct) { Notificaciones.Add(e); return Task.CompletedTask; }
        public Task Handle(DiferenciaPrecioFacturaDetectadaDomainEvent e, CancellationToken ct) { Notificaciones.Add(e); return Task.CompletedTask; }
    }
    private sealed class PeriodoPruebaAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
