using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.UnitTests.Pedidos;

/// <summary>
/// Tests del bridge <see cref="AwMasterProvisioningAdapter"/> (ADR-0048,
/// cierra &lt;MasterProvisioningAw&gt;): vista → command de DatosMaestros →
/// lectura fiscal para Facturación. Los handlers de Provisionar* se validan
/// en el E2E (upsert real contra Postgres); aquí, el ruteo y los null-paths.
/// </summary>
public class AwMasterProvisioningAdapterTests
{
    private sealed class FakeClientesReader : IAwClientesReader
    {
        public AwClienteMaster? Resultado { get; set; }
        public Task<AwClienteMaster?> LeerClienteAsync(string clienteRef, CancellationToken ct) =>
            Task.FromResult(Resultado);
    }

    private sealed class FakeArticulosReader : IAwArticulosReader
    {
        public AwArticuloMaster? Resultado { get; set; }
        public Task<AwArticuloMaster?> LeerArticuloAsync(string articuloRef, CancellationToken ct) =>
            Task.FromResult(Resultado);
    }

    /// <summary>ISender mínimo: responde con lo configurado y captura el request.</summary>
    private sealed class FakeSender : ISender
    {
        public object? RespuestaCliente { get; set; }
        public object? RespuestaProducto { get; set; }
        public List<object> Enviados { get; } = new();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Enviados.Add(request);
            object? respuesta = request switch
            {
                ProvisionarClienteDesdeAwCommand => RespuestaCliente,
                ProvisionarProductoAwCommand => RespuestaProducto,
                _ => null,
            };
            return Task.FromResult((TResponse)respuesta!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
        { Enviados.Add(request!); return Task.CompletedTask; }

        public Task<object?> Send(object request, CancellationToken ct = default)
        { Enviados.Add(request); return Task.FromResult<object?>(null); }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private readonly FakeClientesReader _clientes = new();
    private readonly FakeArticulosReader _articulos = new();
    private readonly FakeSender _sender = new();

    private AwMasterProvisioningAdapter CrearAdapter(AwPedidosOptions? opts = null) => new(
        _clientes,
        _articulos,
        _sender,
        Options.Create(opts ?? new AwPedidosOptions()),
        NullLogger<AwMasterProvisioningAdapter>.Instance);

    [Fact]
    public async Task EnsureCliente_VistaSinRegistro_DevuelveNull_SinProvisionar()
    {
        _clientes.Resultado = null;

        var r = await CrearAdapter().EnsureClienteDesdeAwAsync("999", default);

        Assert.Null(r); // → la matriz cae a ClienteNoExiste (bandeja)
        Assert.Empty(_sender.Enviados);
    }

    [Fact]
    public async Task EnsureCliente_VistaConDatos_Provisiona_YMapeaLectura()
    {
        var clienteId = Guid.CreateVersion7();
        _clientes.Resultado = new AwClienteMaster(
            "56380", "GLOBAL CONSTRUCCIONES SA DE CV", "GCO123456AB9",
            null, null, "97370", null, null, null, "9991664177");
        _sender.RespuestaCliente = new ProvisionarClienteDesdeAwResponse(
            clienteId, "GCO123456AB9", "GLOBAL CONSTRUCCIONES SA DE CV",
            null, "97370", null, null, null, "MXN", false, Creado: true);

        var r = await CrearAdapter().EnsureClienteDesdeAwAsync("56380", default);

        Assert.NotNull(r);
        Assert.Equal(clienteId, r!.ClienteId);
        Assert.Equal("GCO123456AB9", r.Rfc);
        // Régimen no viene de A+W → null: el pedido ingesta pero no timbra
        // hasta completarlo en la UI. El CP sí viaja (PLZ, prefill fiscal).
        Assert.Null(r.RegimenFiscal);
        Assert.Equal("97370", r.CodigoPostalFiscal);

        var cmd = Assert.IsType<ProvisionarClienteDesdeAwCommand>(Assert.Single(_sender.Enviados));
        Assert.Equal("56380", cmd.ReferenciaExterna);
        Assert.Equal("GCO123456AB9", cmd.Rfc);
        Assert.Equal("97370", cmd.CodigoPostalFiscal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("9737")]      // 4 dígitos
    [InlineData("97-370")]    // separador
    [InlineData("CP97370")]   // texto
    public async Task EnsureCliente_CpNoValido_ProvisionaConCpNull(string? cpCrudo)
    {
        _clientes.Resultado = new AwClienteMaster(
            "56380", "GLOBAL CONSTRUCCIONES SA DE CV", null,
            null, null, cpCrudo, null, null, null, null);
        _sender.RespuestaCliente = new ProvisionarClienteDesdeAwResponse(
            Guid.CreateVersion7(), null, "GLOBAL CONSTRUCCIONES SA DE CV",
            null, null, null, null, null, "MXN", false, Creado: true);

        await CrearAdapter().EnsureClienteDesdeAwAsync("56380", default);

        var cmd = Assert.IsType<ProvisionarClienteDesdeAwCommand>(Assert.Single(_sender.Enviados));
        Assert.Null(cmd.CodigoPostalFiscal);
    }

    [Fact]
    public async Task EnsureArticulo_VistaSinRegistro_DevuelveNull()
    {
        _articulos.Resultado = null;

        var r = await CrearAdapter().EnsureArticuloDesdeAwAsync("404", default);

        Assert.Null(r);
        Assert.Empty(_sender.Enviados);
    }

    [Fact]
    public async Task EnsureArticulo_SugiereClaveUnidadSat_DesdeElMapeo()
    {
        var productoId = Guid.CreateVersion7();
        _articulos.Resultado = new AwArticuloMaster("5137", "VIDRIO CLARO 6MM", "M2");
        _sender.RespuestaProducto = new ProvisionarProductoAwResponse(
            productoId, "VIDRIO CLARO 6MM", null, "MTK", null,
            null, null, null, "Aw", Creado: true);

        var r = await CrearAdapter().EnsureArticuloDesdeAwAsync("5137", default);

        Assert.NotNull(r);
        Assert.Equal("MTK", r!.ClaveUnidadSat);
        Assert.Equal("Aw", r.Origen);
        // La clave prod/serv NO se sugiere — la completa el operador.
        Assert.Null(r.ClaveProdServSat);

        var cmd = Assert.IsType<ProvisionarProductoAwCommand>(Assert.Single(_sender.Enviados));
        Assert.Equal("5137", cmd.ReferenciaExterna);
        Assert.Equal("MTK", cmd.ClaveUnidadSatSugerida); // default M2→MTK del mapeo
    }

    [Fact]
    public async Task EnsureArticulo_UnidadSinMapeo_SugerenciaNull()
    {
        _articulos.Resultado = new AwArticuloMaster("77", "SELLADO PERIMETRAL", "SRV");
        _sender.RespuestaProducto = new ProvisionarProductoAwResponse(
            Guid.CreateVersion7(), "SELLADO PERIMETRAL", null, null, null,
            null, null, null, "Aw", Creado: true);

        await CrearAdapter().EnsureArticuloDesdeAwAsync("77", default);

        var cmd = Assert.IsType<ProvisionarProductoAwCommand>(Assert.Single(_sender.Enviados));
        Assert.Null(cmd.ClaveUnidadSatSugerida);
    }
}
