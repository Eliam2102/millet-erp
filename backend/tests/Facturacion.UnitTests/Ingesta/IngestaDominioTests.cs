using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Ingesta;

public sealed class IngestaDominioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static PedidoFacturable Aw(long version = 1) => PedidoFacturable.ImportarDesdeAw(
        empresaId: Guid.NewGuid(),
        numeroPedido: "AW-1000",
        sucursalId: Guid.NewGuid(),
        clienteId: Guid.NewGuid(),
        clienteNombre: "Cliente A+W",
        canalVentaId: (short)1,
        comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
        moneda: "MXN",
        obraId: null,
        obraNombre: null,
        comentarios: "trae el camión por atrás",
        versionOrigen: version,
        estadoOrigen: "15");

    // ---- IngestaControl ----

    [Fact]
    public void IngestaControl_Crear_y_Aplicar_actualiza_version_y_estado()
    {
        var c = IngestaControl.Crear(Guid.NewGuid(), OrigenPedido.Aw, "AW-1000", "hash1", 1, EstadoIngesta.Importado, Guid.NewGuid(), Ahora);
        c.UltimaVersionAplicada.Should().Be(1);

        c.Aplicar("hash2", 2, EstadoIngesta.Facturado, c.PedidoFacturableId, Ahora);
        c.UltimaVersionAplicada.Should().Be(2);
        c.HashContenido.Should().Be("hash2");
        c.Estado.Should().Be(EstadoIngesta.Facturado);
    }

    // ---- ExcepcionImportacion ----

    [Fact]
    public void Excepcion_Resolver_marca_resuelta_y_no_dos_veces()
    {
        var e = ExcepcionImportacion.Crear(Guid.NewGuid(), OrigenPedido.PlantaPintura, "FPIN-1", MotivoExcepcion.ClienteNoExiste, "no existe en master");
        e.Resuelto.Should().BeFalse();

        e.Resolver(Guid.NewGuid(), Ahora);
        e.Resuelto.Should().BeTrue();
        e.ResueltoAt.Should().NotBeNull();

        var act = () => e.Resolver(Guid.NewGuid(), Ahora);
        act.Should().Throw<BusinessRuleException>().Where(x => x.Code == "EXCEPCION_YA_RESUELTA");
    }

    // ---- PedidoFacturable origen A+W (matriz, dominio) ----

    [Fact]
    public void ImportarDesdeAw_arranca_Importado_origen_Aw_con_version()
    {
        var p = Aw(version: 3);

        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        p.Origen.Should().Be(OrigenPedido.Aw);
        p.VersionOrigen.Should().Be(3);
        p.EstadoOrigen.Should().Be("15");
    }

    [Fact]
    public void RefrescarCabecera_desde_Importado_actualiza_y_sube_version()
    {
        var p = Aw(version: 1);

        p.RefrescarCabecera(Guid.NewGuid(), "Cliente nuevo", (short)8,
            ComportamientoFiscal.ExportacionConCce, "USD", 9L, "Obra X", null, 2, "69");

        p.ClienteNombre.Should().Be("Cliente nuevo");
        p.Moneda.Should().Be("USD");
        p.VersionOrigen.Should().Be(2);
        p.EstadoOrigen.Should().Be("69");
        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    [Fact]
    public void RefrescarCabecera_sobre_Facturado_lanza_la_factura_manda()
    {
        var p = Aw();
        p.MarcarFacturado(Guid.NewGuid());

        var act = () => p.RefrescarCabecera(Guid.NewGuid(), "x", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 2, "115");

        act.Should().Throw<BusinessRuleException>().Where(x => x.Code == "PEDIDO_FACTURADO_NO_REFRESCABLE");
    }

    [Fact]
    public void Cancelar_desde_Importado_pasa_a_Cancelado()
    {
        var p = Aw();

        p.Cancelar();

        p.Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
    }

    [Fact]
    public void Cancelar_sobre_Facturado_lanza_la_factura_manda()
    {
        var p = Aw();
        p.MarcarFacturado(Guid.NewGuid());

        var act = () => p.Cancelar();

        act.Should().Throw<BusinessRuleException>().Where(x => x.Code == "PEDIDO_FACTURADO_NO_CANCELABLE_AUTO");
    }

    [Fact]
    public void Bloquear_y_Liberar_alternan_estado()
    {
        var p = Aw();

        p.Bloquear();
        p.Estado.Should().Be(EstadoPedidoFacturable.Bloqueado);

        p.Liberar();
        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }
}
