using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Millet.Administracion.Domain;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.GeneradorPrueba;

public sealed class GenerarEIngresarCfdiPruebaHandlerTests
{
    private readonly BypassedEmpresaContext _empresaContext = new();
    private readonly PeriodoAbiertoStub _periodos = new();
    private readonly PeriodoAbiertoStub _clock = new();

    private ComprasDbContext CrearComprasDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ComprasDbContext(options, _empresaContext);
    }

    private CuentasPorPagarDbContext CrearCxpDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new CuentasPorPagarDbContext(options, _empresaContext, _periodos, _clock);
    }

    private CompartidoDbContext CrearCompartidoDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new CompartidoDbContext(options, _empresaContext);
    }

    private static OrdenCompra CrearOc(Guid empresaId, Guid proveedorId, string folioStr = "OC-MID2026-000101")
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: Millet.Compras.Domain.Oc.Folio.Parse(folioStr),
            folioAnio: 2026,
            proveedorId: proveedorId,
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Prueba CFDI simulación");

        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        var ahora = DateTimeOffset.UtcNow;
        oc.ActualizarDatosFactura("G03", "PPD", "99");
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }

    private static Empresa CrearEmpresa(Guid id, string rfc = "MIL010101AAA", string razonSocial = "Vidrios Millet SA de CV")
    {
        return new Empresa(
            id: id,
            clave: "MILLET",
            rfc: rfc,
            razonSocial: razonSocial,
            regimenFiscal: "601",
            calle: "Calle 60",
            numeroExterior: "100",
            colonia: "Centro",
            ciudad: "Mérida",
            municipio: "Mérida",
            estado: "Yucatán",
            pais: "MEX",
            codigoPostal: "97000");
    }

    private static ProveedorDto CrearProveedor(Guid id, string rfc = "PRV010101AAA", string razonSocial = "Proveedor de Prueba SA")
    {
        return new ProveedorDto(
            Id: id,
            Rfc: rfc,
            RazonSocial: razonSocial,
            Tolerancia: null,
            EnRevision: false,
            Activo: true);
    }

    [Fact]
    public async Task Handle_en_ambiente_produccion_lanza_InvalidOperationException()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var env = new TestHostEnvironment("Production");
        var proveedores = new FakeProveedorReadPort();
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var act = () => handler.Handle(new GenerarEIngresarCfdiPruebaCommand(Guid.NewGuid()), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("La simulación de CFDI no está permitida en entorno de producción.");
    }

    [Fact]
    public async Task Handle_con_orden_compra_inexistente_lanza_EntityNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var env = new TestHostEnvironment("Development");
        var proveedores = new FakeProveedorReadPort();
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var ocId = Guid.NewGuid();
        var act = () => handler.Handle(new GenerarEIngresarCfdiPruebaCommand(ocId), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntityNotFoundException>();
        ex.Which.Code.Should().Be("ORDEN_COMPRA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Handle_con_proveedor_inexistente_lanza_EntityNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId);
        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var env = new TestHostEnvironment("Development");
        var proveedores = new FakeProveedorReadPort(proveedor: null);
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var act = () => handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntityNotFoundException>();
        ex.Which.Code.Should().Be("PROVEEDOR_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Handle_con_empresa_inexistente_lanza_EntityNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId);
        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var env = new TestHostEnvironment("Development");
        var proveedor = CrearProveedor(proveedorId);
        var proveedores = new FakeProveedorReadPort(proveedor);
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var act = () => handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntityNotFoundException>();
        ex.Which.Code.Should().Be("EMPRESA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Handle_idempotencia_si_ya_existe_cfdi_por_procesar_retorna_existente_sin_reingresar()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId, "OC-MID2026-000102");
        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var empresa = CrearEmpresa(empresaId);
        compartidoDb.Empresas.Add(empresa);
        await compartidoDb.SaveChangesAsync();

        var proveedor = CrearProveedor(proveedorId);
        var proveedores = new FakeProveedorReadPort(proveedor);

        // Ya existe un CFDI en estado PorProcesar para esa OC con el mismo emisor y folio
        var uuidExistente = Guid.NewGuid();
        var cfdiExistente = CfdiRecibido.Ingresar(
            empresaId: empresaId,
            uuid: UuidCfdi.Parse(uuidExistente.ToString()),
            rfcEmisor: RfcMexicano.Parse(proveedor.Rfc),
            rfcReceptor: RfcMexicano.Parse(empresa.Rfc),
            tipo: TipoCfdi.Ingreso,
            folio: oc.Folio.Valor,
            serie: "PRUEBA",
            fechaCfdi: DateTimeOffset.UtcNow,
            total: 1160.00m,
            subtotal: 1000.00m,
            impuestosTrasladados: 160.00m,
            retenciones: 0m,
            moneda: "MXN",
            tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.Mailbox,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: $"cxp/2026/11/{uuidExistente}.xml",
            pdfBlobRef: null,
            xmlHashSha256: "hashficticio1234567890abcdef1234567890abcdef1234567890abcdef1234",
            metodoPago: "PPD");

        cxpDb.CfdisRecibidos.Add(cfdiExistente);
        await cxpDb.SaveChangesAsync();

        var env = new TestHostEnvironment("Development");
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var resultado = await handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id), CancellationToken.None);

        resultado.Should().NotBeNull();
        resultado.CfdiRecibidoId.Should().Be(cfdiExistente.Id);
        resultado.UuidCfdi.Should().Be(uuidExistente.ToString().ToUpperInvariant());
        resultado.Folio.Should().Be(oc.Folio.Valor);
        resultado.Total.Should().Be(1160.00m);

        generador.UltimosDatos.Should().BeNull("no debió invocar al generador porque ya existía el CFDI PorProcesar");
        sender.UltimoComandoEnviado.Should().BeNull("no debió despachar IngresarCfdiCommand por idempotencia");
    }

    [Fact]
    public async Task Handle_flujo_exitoso_genera_xml_y_despacha_IngresarCfdiCommand()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId, "OC-MID2026-000103");
        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var empresa = CrearEmpresa(empresaId);
        compartidoDb.Empresas.Add(empresa);

        var articuloId = oc.Lineas.First().ArticuloId;
        var articulo = new Articulo(Guid.CreateVersion7(), "VID-6MM", "Vidrio Claro 6mm", "M2");
        typeof(Articulo).GetProperty(nameof(Articulo.Id))!.SetValue(articulo, articuloId);
        compartidoDb.Articulos.Add(articulo);
        await compartidoDb.SaveChangesAsync();

        var proveedor = CrearProveedor(proveedorId);
        var proveedores = new FakeProveedorReadPort(proveedor);

        var env = new TestHostEnvironment("Development");
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var nuevoCfdiId = Guid.NewGuid();
        var nuevoUuid = Guid.NewGuid();

        sender.OnSend = (cmd, ct) =>
        {
            if (cmd is IngresarCfdiCommand c)
            {
                c.Canal.Should().Be(CanalOrigenCfdi.Mailbox);
                c.EmpresaIdOverride.Should().Be(empresaId);
                c.Xml.Should().NotBeNull();
                c.Xml.Length.Should().BeGreaterThan(0);

                // Simular que IngresarCfdiHandler persistió el CFDI
                var cfdi = CfdiRecibido.Ingresar(
                    empresaId: empresaId,
                    uuid: UuidCfdi.Parse(nuevoUuid.ToString()),
                    rfcEmisor: RfcMexicano.Parse(proveedor.Rfc),
                    rfcReceptor: RfcMexicano.Parse(empresa.Rfc),
                    tipo: TipoCfdi.Ingreso,
                    folio: oc.Folio.Valor,
                    serie: "PRUEBA",
                    fechaCfdi: DateTimeOffset.UtcNow,
                    total: 1160.00m,
                    subtotal: 1000.00m,
                    impuestosTrasladados: 160.00m,
                    retenciones: 0m,
                    moneda: "MXN",
                    tipoCambio: null,
                    canalOrigen: CanalOrigenCfdi.Mailbox,
                    fechaRecepcion: DateTimeOffset.UtcNow,
                    xmlBlobRef: $"cxp/2026/11/{nuevoUuid}.xml",
                    pdfBlobRef: null,
                    xmlHashSha256: "hashficticio1234567890abcdef1234567890abcdef1234567890abcdef1234",
                    metodoPago: "PPD");
                typeof(CfdiRecibido).GetProperty(nameof(CfdiRecibido.Id))!.SetValue(cfdi, nuevoCfdiId);
                cxpDb.CfdisRecibidos.Add(cfdi);
                cxpDb.SaveChanges();

                return Task.FromResult<object>(new IngresarCfdiResponse(nuevoCfdiId, nuevoUuid.ToString().ToUpperInvariant(), EstadoCfdiRecibido.PorProcesar, null));
            }
            throw new NotSupportedException();
        };

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var respuesta = await handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id), CancellationToken.None);

        respuesta.Should().NotBeNull();
        respuesta.CfdiRecibidoId.Should().Be(nuevoCfdiId);
        respuesta.UuidCfdi.Should().Be(nuevoUuid.ToString().ToUpperInvariant());
        respuesta.Folio.Should().Be(oc.Folio.Valor);
        respuesta.Total.Should().Be(1160.00m);

        generador.UltimosDatos.Should().NotBeNull();
        generador.UltimosDatos!.Emisor.Rfc.Should().Be(proveedor.Rfc);
        generador.UltimosDatos.Receptor.Rfc.Should().Be(empresa.Rfc);
        generador.UltimosDatos.Folio.Should().Be(oc.Folio.Valor);
        generador.UltimosDatos.Lineas.Should().HaveCount(1);
        generador.UltimosDatos.Lineas[0].Cantidad.Should().Be(10m);
        generador.UltimosDatos.Lineas[0].ArticuloClave.Should().Be("VID-6MM");
        generador.UltimosDatos.Lineas[0].ArticuloNombre.Should().Be("Vidrio Claro 6mm");

        sender.UltimoComandoEnviado.Should().BeOfType<IngresarCfdiCommand>();
    }

    [Fact]
    public async Task Handle_con_cantidades_parciales_especificas_usa_cantidades_del_diccionario()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId, "OC-MID2026-000104");
        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var empresa = CrearEmpresa(empresaId);
        compartidoDb.Empresas.Add(empresa);
        await compartidoDb.SaveChangesAsync();

        var proveedor = CrearProveedor(proveedorId);
        var proveedores = new FakeProveedorReadPort(proveedor);

        var env = new TestHostEnvironment("Development");
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        sender.OnSend = (cmd, ct) => Task.FromResult<object>(
            new IngresarCfdiResponse(Guid.NewGuid(), Guid.NewGuid().ToString(), EstadoCfdiRecibido.PorProcesar, null));

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var linea = oc.Lineas.First();
        var cantidades = new Dictionary<int, decimal> { [linea.Posicion] = 3.5m };

        await handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id, cantidades), CancellationToken.None);

        generador.UltimosDatos.Should().NotBeNull();
        generador.UltimosDatos!.Lineas.Should().HaveCount(1);
        generador.UltimosDatos.Lineas[0].Cantidad.Should().Be(3.5m);
    }

    [Fact]
    public async Task Handle_cuando_todas_las_lineas_tienen_cantidad_cero_o_facturada_lanza_BusinessRuleException()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var comprasDb = CrearComprasDb(dbName);
        await using var cxpDb = CrearCxpDb(dbName);
        await using var compartidoDb = CrearCompartidoDb(dbName);

        var empresaId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var oc = CrearOc(empresaId, proveedorId, "OC-MID2026-000105");

        // Simular que la línea ya fue totalmente facturada (CantidadFacturada == Cantidad)
        var linea = oc.Lineas.First();
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadFacturada))!
            .SetValue(linea, linea.Cantidad);

        comprasDb.OrdenesCompra.Add(oc);
        await comprasDb.SaveChangesAsync();

        var empresa = CrearEmpresa(empresaId);
        compartidoDb.Empresas.Add(empresa);
        await compartidoDb.SaveChangesAsync();

        var proveedor = CrearProveedor(proveedorId);
        var proveedores = new FakeProveedorReadPort(proveedor);

        var env = new TestHostEnvironment("Development");
        var generador = new SpyGeneradorCfdiPruebaService();
        var sender = new TestSender();

        var handler = new GenerarEIngresarCfdiPruebaHandler(
            comprasDb, cxpDb, compartidoDb, proveedores, generador, sender, env, _empresaContext);

        var act = () => handler.Handle(new GenerarEIngresarCfdiPruebaCommand(oc.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Code.Should().Be("OC_SIN_LINEAS_FACTURABLES");
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TestHostEnvironment(string environmentName = "Development") : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Millet.Tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeProveedorReadPort(ProveedorDto? proveedor = null) : IProveedorReadPort
    {
        public Task<ProveedorDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) =>
            Task.FromResult(proveedor);

        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken) =>
            Task.FromResult(proveedor);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class SpyGeneradorCfdiPruebaService : IGeneradorCfdiPruebaService
    {
        private readonly GeneradorCfdiPruebaService _real = new();
        public DatosGeneracionCfdiPrueba? UltimosDatos { get; private set; }

        public string GenerarXml(DatosGeneracionCfdiPrueba datos)
        {
            UltimosDatos = datos;
            return _real.GenerarXml(datos);
        }

        public Stream GenerarXmlStream(DatosGeneracionCfdiPrueba datos)
        {
            UltimosDatos = datos;
            return _real.GenerarXmlStream(datos);
        }
    }

    private sealed class TestSender : ISender
    {
        public object? UltimoComandoEnviado { get; private set; }
        public Func<object, CancellationToken, Task<object>>? OnSend { get; set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            UltimoComandoEnviado = request;
            if (OnSend is not null)
            {
                var res = OnSend(request, cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult((TResponse)res);
            }
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            UltimoComandoEnviado = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            UltimoComandoEnviado = request;
            if (OnSend is not null)
            {
                return OnSend(request, cancellationToken)!;
            }
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
