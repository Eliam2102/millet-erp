using Millet.CuentasPorCobrar.Application.Anticipos;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

namespace Millet.CuentasPorCobrar.UnitTests.Anticipos;

/// <summary>
/// F1-CXC-05 — consulta de anticipos con aplicaciones en CxC: totales por
/// moneda (nunca cross-divisa), alcance por sucursal (ADR-0051) y validador.
/// </summary>
public sealed class AplicacionesAnticipoClienteQueryTests
{
    private static readonly Guid SucursalMid = Guid.NewGuid();
    private static readonly Guid SucursalCun = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static AnticipoConAplicacionesDto Anticipo(
        string folio, string moneda, decimal cobrado, decimal aplicado, decimal comprometido,
        string estado, Guid? sucursalId, Guid? clienteId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), clienteId ?? ClienteId, "Cliente", folio, Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow, sucursalId, "MID — Mérida", moneda, estado, cobrado, aplicado, comprometido,
            cobrado - aplicado, cobrado - aplicado, null, null, []);

    private static readonly AnticipoConAplicacionesDto[] Datos =
    [
        Anticipo("ANT-1", "MXN", 30_000m, 20_000m, 0m, EstadoAnticipoAplicaciones.Parcial, SucursalMid),
        Anticipo("ANT-2", "MXN", 5_000m, 0m, 1_000m, EstadoAnticipoAplicaciones.Pendiente, SucursalCun),
        Anticipo("ANT-3", "USD", 1_000m, 1_000m, 0m, EstadoAnticipoAplicaciones.Aplicado, SucursalMid),
        Anticipo("ANT-4", "MXN", 9_999m, 0m, 0m, EstadoAnticipoAplicaciones.Cancelado, SucursalMid),
        Anticipo("ANT-5", "MXN", 700m, 0m, 0m, EstadoAnticipoAplicaciones.Pendiente, null),
    ];

    private static Task<AplicacionesAnticipoResponse> Consultar(
        AplicacionesAnticipoClienteQuery query, FakePort? port = null) =>
        new AplicacionesAnticipoClienteHandler(port ?? new FakePort(Datos)).Handle(query, CancellationToken.None);

    [Fact]
    public async Task Totales_por_moneda_cuadran_cobrado_menos_aplicado_igual_saldo_sin_cancelados()
    {
        var r = await Consultar(new AplicacionesAnticipoClienteQuery(null));

        r.Anticipos.Should().HaveCount(5);
        r.TotalesPorMoneda.Select(t => t.Moneda).Should().Equal("MXN", "USD");
        var mxn = r.TotalesPorMoneda[0];
        mxn.Cobrado.Should().Be(35_700m, "el anticipo cancelado no suma");
        mxn.Aplicado.Should().Be(20_000m);
        mxn.Comprometido.Should().Be(1_000m);
        mxn.Saldo.Should().Be(15_700m);
        r.TotalesPorMoneda.Should().AllSatisfy(t => (t.Cobrado - t.Aplicado).Should().Be(t.Saldo));
        r.TotalesPorMoneda[1].Should().Be(new TotalesAnticiposMonedaDto("USD", 1_000m, 1_000m, 0m, 0m));
    }

    [Fact]
    public async Task Operativo_solo_ve_sus_sucursales_y_bypass_ve_todas()
    {
        var operativo = new AplicacionesAnticipoClienteQuery(null) { SucursalesPermitidas = [SucursalMid] };
        var r = await Consultar(operativo);
        r.Anticipos.Select(a => a.Folio).Should().BeEquivalentTo(["ANT-1", "ANT-3", "ANT-4"]);
        r.TotalesPorMoneda.Single(t => t.Moneda == "MXN").Cobrado.Should().Be(30_000m);

        var sinSucursales = new AplicacionesAnticipoClienteQuery(null) { SucursalesPermitidas = [] };
        (await Consultar(sinSucursales)).Anticipos.Should().BeEmpty();

        var corporativo = new AplicacionesAnticipoClienteQuery(null) { SucursalesPermitidas = null };
        (await Consultar(corporativo)).Anticipos.Should().HaveCount(5);
    }

    [Fact]
    public async Task Pasa_el_cliente_al_puerto_y_filtra_por_estado()
    {
        var port = new FakePort(Datos);
        var r = await Consultar(new AplicacionesAnticipoClienteQuery(ClienteId, "pendiente"), port);

        port.ClienteRecibido.Should().Be(ClienteId);
        r.Anticipos.Select(a => a.Folio).Should().BeEquivalentTo(["ANT-2", "ANT-5"]);
        r.TotalesPorMoneda.Should().ContainSingle().Which.Saldo.Should().Be(5_700m);

        var cancelados = await Consultar(new AplicacionesAnticipoClienteQuery(null, "Cancelado"));
        cancelados.Anticipos.Should().ContainSingle().Which.Folio.Should().Be("ANT-4");
    }

    [Fact]
    public void Validador_rechaza_cliente_vacio_y_estado_desconocido()
    {
        var v = new AplicacionesAnticipoClienteValidator();

        v.Validate(new AplicacionesAnticipoClienteQuery(null)).IsValid.Should().BeTrue();
        v.Validate(new AplicacionesAnticipoClienteQuery(ClienteId, "Parcial")).IsValid.Should().BeTrue();
        v.Validate(new AplicacionesAnticipoClienteQuery(Guid.Empty)).IsValid.Should().BeFalse();
        v.Validate(new AplicacionesAnticipoClienteQuery(null, "Abierto")).IsValid.Should().BeFalse();
    }

    private sealed class FakePort(IReadOnlyList<AnticipoConAplicacionesDto> datos) : IFacturacionAplicacionesAnticipoReadPort
    {
        public Guid? ClienteRecibido { get; private set; }

        public Task<IReadOnlyList<AnticipoConAplicacionesDto>> ListarAsync(Guid? clienteId, CancellationToken ct)
        {
            ClienteRecibido = clienteId;
            return Task.FromResult(datos);
        }
    }
}
