using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.CartaPorte;

/// <summary>
/// H-U32: un CFDI tipo T lleva Moneda XXX (el PAC rechaza MXN con CP103). H-U33: una configuración vehicular que exige remolque
/// (catálogo oficial c_ConfigAutotransporte, columna «Remolque» = 1) se rechaza antes de reservar folio y de llamar al PAC (CP184).
/// Datos ficticios.
/// </summary>
public sealed class CartaPortePacPrevalidacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>Cuenta los folios reservados.</summary>
    private sealed class SenderFolios : ISender
    {
        public int Reservados { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                Reservados++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"CP-{Reservados:D6}", Reservados, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Emisor", "601", "76120");

    private static async Task<(Guid Vehiculo, Guid Operador)> SembrarAsync(FacturacionDbContext db, Guid empresaId, string config)
    {
        var v = Vehiculo.Crear(empresaId, "PRU-001", config, 2022, "TPAF01", "PERM-1", "Aseguradora", "POL-1", pesoBrutoVehicular: 17.5m);
        var o = Operador.Crear(empresaId, "OPER010101AAA", "Operador Prueba", "LIC-1");
        db.Vehiculos.Add(v);
        db.Operadores.Add(o);
        await db.SaveChangesAsync();
        return (v.Id, o.Id);
    }

    private static EmitirCartaPorteHandler Handler(FacturacionDbContext db, Guid empresaId, ISender sender, FakeFiscalApiClient pac) =>
        new(db, sender, new FakePeriodoContablePort(), pac, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Emisor", "601", 0.16m, "76120")),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirCartaPorteCommand Command(string tipo, Guid vehiculoId, Guid operadorId) => new(
        Guid.NewGuid(), null, tipo, "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "MXN", "Origen", "Destino", 300m, vehiculoId, operadorId, null,
        Ahora, Ahora.AddHours(4), tipo == "I" ? 5000m : 0m, tipo == "I" ? 0.16m : null,
        [new CartaPorteMercanciaInput("Vidrio templado", "43211503", "KGM", 10m, 500m, false)],
        OrigenCodigoPostal: "97345", OrigenEstado: "YUC", DestinoCodigoPostal: "77500", DestinoEstado: "ROO");

    // ---- H-U32 ----

    [Fact]
    public void Traslado_se_crea_con_moneda_XXX_aunque_se_pida_MXN()
    {
        var cp = Dominio.CartaPorte.CrearBorrador(Guid.NewGuid(), TipoComprobante.Traslado, "CP-1", 1, Guid.NewGuid(), null, null,
            Receptor(), Emisor(), "MXN", 2026, 11, "Origen", "Destino", 300m, Guid.NewGuid(), Guid.NewGuid(), null, null, Ahora, Ahora.AddHours(4));

        cp.Moneda.Should().Be("XXX");
    }

    [Fact]
    public void Ingreso_conserva_la_moneda_pedida()
    {
        var cp = Dominio.CartaPorte.CrearBorrador(Guid.NewGuid(), TipoComprobante.Ingreso, "CP-1", 1, Guid.NewGuid(), null, null,
            Receptor(), Emisor(), "MXN", 2026, 11, "Origen", "Destino", 300m, Guid.NewGuid(), Guid.NewGuid(), null, null, Ahora, Ahora.AddHours(4),
            montoServicio: 1000m, tasaIvaServicio: 0.16m);

        cp.Moneda.Should().Be("MXN");
    }

    [Fact]
    public async Task Emitir_T_manda_al_PAC_moneda_XXX()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarAsync(db, empresaId, "C2");
        var pac = new FakeFiscalApiClient();

        await Handler(db, empresaId, new SenderFolios(), pac).Handle(Command("T", v, o), CancellationToken.None);

        pac.UltimaEmision!.Moneda.Should().Be("XXX");
    }

    // ---- H-U33 ----

    [Theory]
    [InlineData("C2R2", true)]
    [InlineData("c3r2", true)]
    [InlineData("T3S2", true)]
    [InlineData("T3S2R4", true)]
    [InlineData("T3S3S2", true)]
    [InlineData("C2", false)]
    [InlineData("C3", false)]
    [InlineData("VL", false)]        // 0, 1 en el catálogo: opcional
    [InlineData("OTROEVGP", false)]  // 0, 1
    [InlineData("GPLUTA", false)]
    [InlineData("NOEXISTE", false)]  // fuera del catálogo: decide el PAC
    [InlineData("", false)]
    public void Catalogo_oficial_indica_si_la_configuracion_exige_remolque(string config, bool exige) =>
        ConfigVehicularRemolque.ExigeRemolque(config).Should().Be(exige);

    [Fact]
    public async Task Configuracion_con_remolque_se_rechaza_sin_reservar_folio_ni_llamar_al_PAC()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarAsync(db, empresaId, "C2R2");
        var sender = new SenderFolios();
        var pac = new FakeFiscalApiClient();

        var act = () => Handler(db, empresaId, sender, pac).Handle(Command("T", v, o), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == ConfigVehicularRemolque.Codigo);
        sender.Reservados.Should().Be(0);
        pac.UltimaEmision.Should().BeNull();
        (await db.CartasPorte.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Configuracion_sin_remolque_timbra()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarAsync(db, empresaId, "C2");
        var sender = new SenderFolios();

        var resp = await Handler(db, empresaId, sender, new FakeFiscalApiClient()).Handle(Command("T", v, o), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        sender.Reservados.Should().Be(1);
    }
}
