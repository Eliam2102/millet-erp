using Microsoft.Extensions.Logging.Abstractions;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

public sealed class AwRetornoDocumentoServiceTests
{
    private sealed class PuertoFalso : IAwRetornoPort
    {
        public List<AwRetornoDocumento> Agregados { get; } = [];
        public bool Apagado { get; init; }
        public Task<int?> AgregarDocumentoAsync(AwRetornoDocumento d, CancellationToken ct)
        {
            if (Apagado) throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado");
            Agregados.Add(d);
            return Task.FromResult<int?>(Agregados.Count);
        }
        public Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct) =>
            Apagado ? throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado")
                    : Task.FromResult<IReadOnlyList<AwRetornoAcuse>>([]);
    }

    private sealed class VersionFalsa : IAwVersionPedidoReadPort
    {
        public Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private static AwRetornoDocumentoService Servicio(PuertoFalso p) =>
        new(p, new VersionFalsa(), NullLogger<AwRetornoDocumentoService>.Instance);

    [Fact]
    public async Task Factura_de_pedido_AW_arma_la_fila_Documento_con_pedido_version_folio_uuid_e_importe()
    {
        var p = new PuertoFalso();
        await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9", "900101", 4), default);
        p.Agregados.Should().ContainSingle().Which.Should().Be(
            new AwRetornoDocumento("900101", "FacturaFinal", 4, "F-9", "UUID-1", 1160m));
    }

    [Fact]
    public async Task Nota_de_credito_de_A_W_arma_la_fila_NotaCredito_con_el_folio_fiscal()
    {
        var p = new PuertoFalso();
        await Servicio(p).RegistrarNotaCreditoAsync(new("UUID-NC", 1500m, "NC-7", "900101", 4), default);
        p.Agregados.Should().ContainSingle().Which.Should().Be(
            new AwRetornoDocumento("900101", "NotaCredito", 4, "NC-7", "UUID-NC", 1500m));
    }

    [Fact]
    public async Task Nota_de_credito_del_ERP_sin_pedido_de_A_W_no_escribe_nada()
    {
        var p = new PuertoFalso();
        (await Servicio(p).RegistrarNotaCreditoAsync(new("UUID-NC", 1500m, "NC-7"), default)).Should().BeNull();
        p.Agregados.Should().BeEmpty();
    }

    [Fact]
    public async Task Factura_de_pedido_no_AW_no_escribe_nada()
    {
        var p = new PuertoFalso();
        (await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9"), default)).Should().BeNull();
        p.Agregados.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_el_canal_sin_configurar_no_falla_ni_escribe()
    {
        var p = new PuertoFalso { Apagado = true };
        (await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9", "900101", 4), default)).Should().BeNull();
        (await Servicio(p).ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 0));
    }
}
