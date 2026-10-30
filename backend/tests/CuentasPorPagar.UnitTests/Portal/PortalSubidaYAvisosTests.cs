using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Application.Portal;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.CuentasPorPagar.Domain.Portal;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.Portal;

public sealed class PortalSubidaYAvisosTests
{
    private const string RfcProveedor = "AAA010101AAA";
    private static readonly Guid Prov = Guid.NewGuid();
    private static readonly byte[] Xml = "<cfdi/>"u8.ToArray();

    private sealed class Parser(string rfcEmisor, TipoCfdi tipo) : IXmlCfdiParser
    {
        public DatosCfdiParseados Parsear(string xml) => Parsear(Stream.Null);
        public DatosCfdiParseados Parsear(Stream xml) => new("UUID-1", rfcEmisor, "Emisor", "BBB010101BBB", "Receptor", tipo, "10", "A",
            DateTimeOffset.UtcNow, 116, 100, 16, 0, "MXN", null, []);
    }

    private sealed class Proveedores : IProveedorReadPort
    {
        public Task<ProveedorDto?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorDto?>(id == Prov ? new(id, RfcProveedor, "Prov", null, false, true) : null);
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => Task.FromResult<ProveedorDto?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class Mediador(Func<IngresarCfdiCommand, IngresarCfdiResponse> respuesta) : ISender
    {
        public List<IngresarCfdiCommand> Comandos { get; } = [];
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            var c = (IngresarCfdiCommand)(object)request;
            Comandos.Add(c);
            return Task.FromResult((TResponse)(object)respuesta(c));
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static IngresarCfdiResponse Ok(IngresarCfdiCommand _) => new(Guid.NewGuid(), "UUID-1", EstadoCfdiRecibido.PorProcesar, null);

    private static (SubirCfdiPortalHandler H, Mediador M) Crear(string rfc, TipoCfdi tipo, Func<IngresarCfdiCommand, IngresarCfdiResponse>? r = null)
    {
        var m = new Mediador(r ?? Ok);
        return (new SubirCfdiPortalHandler(new Parser(rfc, tipo), new Proveedores(), m), m);
    }

    [Fact]
    public async Task Rfc_de_otro_proveedor_se_rechaza_y_no_se_guarda_nada()
    {
        var (h, m) = Crear("CCC010101CCC", TipoCfdi.Ingreso);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(Prov, Xml, null), default));

        Assert.Equal("PORTAL_RFC_NO_COINCIDE", ex.Code);
        Assert.Empty(m.Comandos);
    }

    [Theory]
    [InlineData(TipoCfdi.Traslado)]
    [InlineData(TipoCfdi.Egreso)]
    [InlineData(TipoCfdi.Nomina)]
    public async Task Solo_se_aceptan_tipos_I_y_P(TipoCfdi tipo)
    {
        var (h, m) = Crear(RfcProveedor, tipo);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(Prov, Xml, null), default));

        Assert.Equal("PORTAL_TIPO_NO_ACEPTADO", ex.Code);
        Assert.Empty(m.Comandos);
    }

    [Theory]
    [InlineData(TipoCfdi.Ingreso)]
    [InlineData(TipoCfdi.Pago)]
    public async Task Factura_o_complemento_del_proveedor_entran_por_IngresarCfdi_con_canal_portal(TipoCfdi tipo)
    {
        var (h, m) = Crear(RfcProveedor.ToLowerInvariant(), tipo);

        var r = await h.Handle(new(Prov, Xml, null), default);

        Assert.Equal("Recibido", r.Resultado);
        Assert.Equal(CanalOrigenCfdi.PortalProveedor, Assert.Single(m.Comandos).Canal);
    }

    [Fact]
    public async Task Uuid_repetido_responde_Ya_lo_teniamos_sin_error()
    {
        var (h, _) = Crear(RfcProveedor, TipoCfdi.Ingreso, _ => throw new BusinessRuleException("CFDI_DUPLICADO", "dup"));

        var r = await h.Handle(new(Prov, Xml, null), default);

        Assert.Equal("YaLoTeniamos", r.Resultado);
        Assert.Equal("Ya lo teníamos.", r.Mensaje);
        Assert.Null(r.CfdiId);
    }

    [Fact]
    public async Task Archivos_muy_grandes_o_pdf_falso_se_rechazan_antes_de_leer_el_xml()
    {
        var (h, m) = Crear(RfcProveedor, TipoCfdi.Ingreso);

        var grande = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(Prov, new byte[SubirCfdiPortalHandler.MaxXmlBytes + 1], null), default));
        var pdfFalso = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(Prov, Xml, "no soy pdf"u8.ToArray()), default));
        var vacio = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(Prov, [], null), default));

        Assert.Equal("PORTAL_XML_MUY_GRANDE", grande.Code);
        Assert.Equal("PORTAL_PDF_INVALIDO", pdfFalso.Code);
        Assert.Equal("PORTAL_XML_REQUERIDO", vacio.Code);
        Assert.Empty(m.Comandos);
    }

    private sealed class Correo : IPortalCorreoPort
    {
        public List<string> Avisos { get; } = [];
        public Task EnviarCodigoAsync(string d, string c, CancellationToken ct) => Task.CompletedTask;
        public Task EnviarAvisoPagoAsync(string d, AvisoPagoCorreo a, CancellationToken ct) { Avisos.Add(d); return Task.CompletedTask; }
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public async Task Aviso_de_pago_sale_una_sola_vez_a_los_correos_activos_aunque_el_worker_corra_dos_veces()
    {
        var reloj = new P8.PeriodoAbiertoStub();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa(), reloj, reloj);
        var ahora = DateTimeOffset.UtcNow;
        var empresa = Guid.NewGuid();
        var factura = Factura.CapturarSinOc(empresa, null, null, Prov, Guid.NewGuid(), "10", "A", ahora, ahora,
            DateOnly.FromDateTime(ahora.UtcDateTime), "MXN", null, 100, 0, 16, 0, 116, "prueba", ahora);
        var activo = new AccesoPortalProveedor(empresa, Prov, "a@prov.mx");
        var inactivo = new AccesoPortalProveedor(empresa, Prov, "b@prov.mx");
        inactivo.CambiarActivo(false);
        db.AddRange(factura, activo, inactivo, new AvisoPagoPortal(empresa, factura.Id, Guid.NewGuid(), 116, new(2026, 10, 30)));
        await db.SaveChangesAsync();
        var correo = new Correo();
        var handler = new EnviarAvisosPagoPortalHandler(db, correo, reloj, NullLogger<EnviarAvisosPagoPortalHandler>.Instance);

        var primera = await handler.Handle(new(), default);
        var segunda = await handler.Handle(new(), default);

        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);
        Assert.Equal(["a@prov.mx"], correo.Avisos);
    }
}
