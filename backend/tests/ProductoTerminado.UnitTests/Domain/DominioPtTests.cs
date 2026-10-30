using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Domain;

public sealed class DominioPtTests
{
    private static PedidoPt Pedido(params (int Pos, decimal Cant, string? Sat)[] lineas)
    {
        var p = PedidoPt.Crear(Guid.NewGuid(), Guid.NewGuid(), "P-1", Guid.NewGuid(), null, 1);
        p.ActualizarPosiciones(1, lineas.Select(l => (l.Pos, (Guid?)null, "d", l.Cant, l.Sat)));
        return p;
    }

    [Fact]
    public void LineaE48_NoEsperaPiezas() =>
        Pedido((1, 3m, "E48")).Posiciones.Single().PiezasEsperadas.Should().Be(0);

    [Fact]
    public void CantidadNoEntera_EsperaCeroYMarca()
    {
        var pos = Pedido((1, 2.5m, "H87")).Posiciones.Single();
        pos.PiezasEsperadas.Should().Be(0);
        pos.CantidadNoEntera.Should().BeTrue();
    }

    [Fact]
    public void CantidadEntera_EsperaEsaCantidad()
    {
        var pos = Pedido((1, 4m, "H87")).Posiciones.Single();
        pos.PiezasEsperadas.Should().Be(4);
        pos.CantidadNoEntera.Should().BeFalse();
    }

    [Fact]
    public void ActualizarPosiciones_HaceUpsertPorPosicion()
    {
        var p = Pedido((1, 4m, "H87"));
        p.ActualizarPosiciones(2, [(1, null, "d", 6m, "H87")]);
        p.Posiciones.Should().ContainSingle().Which.PiezasEsperadas.Should().Be(6);
    }

    private static EscaneoPt Escaneo() => EscaneoPt.Registrar(Guid.NewGuid(), "Demo", "  ET-1 ", " P-1 ", 1, null, " 2 ", 2821, null,
        DateTime.UtcNow, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Registrar_RecortaEspacios()
    {
        var e = Escaneo();
        e.Etiqueta.Should().Be("ET-1");
        e.NumeroPedido.Should().Be("P-1");
        e.NumeroPiezaTexto.Should().Be("2");
    }

    [Fact]
    public void Resolver_DosVeces_Lanza()
    {
        var e = Escaneo();
        e.MarcarExcepcion(MotivosPt.PedidoInexistente);
        e.EstaAbierto.Should().BeTrue();
        e.Resolver(Guid.NewGuid(), "ok", DateTimeOffset.UtcNow);
        e.EstaAbierto.Should().BeFalse();
        var act = () => e.Resolver(Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        act.Should().Throw<ConflictException>().Which.Code.Should().Be("PT_ESCANEO_YA_RESUELTO");
    }

    [Fact]
    public void Intentar_SeTopaEnElMaximo()
    {
        var e = Escaneo();
        for (var i = 0; i < 10; i++) e.Intentar(3);
        e.Intentos.Should().Be(3);
    }
}
