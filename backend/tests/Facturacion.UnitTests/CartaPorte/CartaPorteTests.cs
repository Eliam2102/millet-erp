using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.CartaPorte.CrearSiguienteTramo;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.CartaPorte;

public sealed class CartaPorteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeSenderFoliosUnicos : ISender
    {
        private int _n;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                _n++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"CP-{_n:D6}", _n, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // ---- Dominio ----

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    [Fact]
    public void CrearBorrador_traslado_tiene_total_cero()
    {
        var cp = Dominio.CartaPorte.CrearBorrador(Guid.NewGuid(), TipoComprobante.Traslado, "CP-1", 1, Guid.NewGuid(),
            null, null, Receptor(), new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "MXN", 2026, 5, "Conkal", "Cancún", 300m,
            Guid.NewGuid(), Guid.NewGuid(), null, null, Ahora, Ahora.AddHours(4));

        cp.Tipo.Should().Be(TipoComprobante.Traslado);
        cp.Total.Should().Be(0m);
    }

    [Fact]
    public void CrearBorrador_ingreso_factura_el_servicio()
    {
        var cp = Dominio.CartaPorte.CrearBorrador(Guid.NewGuid(), TipoComprobante.Ingreso, "CP-1", 1, Guid.NewGuid(),
            null, null, Receptor(), new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "MXN", 2026, 5, "Conkal", "Cancún", 300m,
            Guid.NewGuid(), Guid.NewGuid(), null, null, Ahora, Ahora.AddHours(4), montoServicio: 5000m, tasaIvaServicio: 0.16m);

        cp.Tipo.Should().Be(TipoComprobante.Ingreso);
        cp.Total.Should().Be(5800m);
    }

    [Fact]
    public void CrearBorrador_ingreso_sin_monto_lanza()
    {
        var act = () => Dominio.CartaPorte.CrearBorrador(Guid.NewGuid(), TipoComprobante.Ingreso, "CP-1", 1, Guid.NewGuid(),
            null, null, Receptor(), new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "MXN", 2026, 5, "Conkal", "Cancún", 300m,
            Guid.NewGuid(), Guid.NewGuid(), null, null, Ahora, Ahora.AddHours(4), montoServicio: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CARTA_PORTE_MONTO_INVALIDO");
    }

    [Fact]
    public void Vehiculo_y_Operador_validan_campos()
    {
        var v = () => Vehiculo.Crear(Guid.NewGuid(), "", "VL", 2020);
        v.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VEHICULO_PLACA_INVALIDA");
        var o = () => Operador.Crear(Guid.NewGuid(), "RFC", "", "LIC");
        o.Should().Throw<BusinessRuleException>().Where(e => e.Code == "OPERADOR_NOMBRE_INVALIDO");
    }

    // ---- Handler ----

    private static async Task<(Guid vehiculoId, Guid operadorId)> SembrarCatalogosAsync(FacturacionDbContext db, Guid empresaId)
    {
        var v = Vehiculo.Crear(empresaId, "ABC-123", "VL", 2022, "TPAF01", "PERM-1", "Seguros X", "POL-1",
            pesoBrutoVehicular: 17.5m);
        var o = Operador.Crear(empresaId, "OPER010101AAA", "Juan Pérez", "LIC-12345");
        db.Vehiculos.Add(v);
        db.Operadores.Add(o);
        await db.SaveChangesAsync();
        return (v.Id, o.Id);
    }

    private static EmitirCartaPorteHandler EmitirHandler(FacturacionDbContext db, Guid empresaId, ISender sender) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirCartaPorteCommand Command(string tipo, Guid vehiculoId, Guid operadorId) => new(
        Guid.NewGuid(), null, tipo, "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "MXN", "Conkal", "Cancún", 300m, vehiculoId, operadorId, null,
        Ahora, Ahora.AddHours(4), tipo == "I" ? 5000m : 0m, tipo == "I" ? 0.16m : null,
        [new CartaPorteMercanciaInput("Vidrio templado", "43211503", "KGM", 10m, 500m, false)],
        OrigenCodigoPostal: "97345", OrigenEstado: "YUC",
        DestinoCodigoPostal: "77500", DestinoEstado: "ROO");

    [Fact]
    public async Task Emitir_carta_porte_T_timbra_con_total_cero()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarCatalogosAsync(db, empresaId);

        var resp = await EmitirHandler(db, empresaId, new FakeSenderFoliosUnicos())
            .Handle(Command("T", v, o), CancellationToken.None);

        resp.TipoCfdi.Should().Be("Traslado");
        resp.Estado.Should().Be("Timbrado");
        resp.Total.Should().Be(0m);
        (await db.CartasPorte.Include(c => c.Mercancias).SingleAsync()).Mercancias.Should().HaveCount(1);
    }

    [Fact]
    public async Task Emitir_carta_porte_I_factura_el_servicio()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarCatalogosAsync(db, empresaId);

        var resp = await EmitirHandler(db, empresaId, new FakeSenderFoliosUnicos())
            .Handle(Command("I", v, o), CancellationToken.None);

        resp.TipoCfdi.Should().Be("Ingreso");
        resp.Total.Should().Be(5800m);
    }

    [Fact]
    public async Task Emitir_con_vehiculo_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (_, o) = await SembrarCatalogosAsync(db, empresaId);

        var act = () => EmitirHandler(db, empresaId, new FakeSenderFoliosUnicos())
            .Handle(Command("T", Guid.NewGuid(), o), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "VEHICULO_NO_ENCONTRADO");
    }

    // ---- Siguiente tramo ----

    private static CrearSiguienteTramoHandler TramoHandler(FacturacionDbContext db, Guid empresaId, ISender sender) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    [Fact]
    public async Task SiguienteTramo_referencia_la_previa_y_hereda_mercancias()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var (v, o) = await SembrarCatalogosAsync(db, empresaId);
        var primera = await EmitirHandler(db, empresaId, sender).Handle(Command("T", v, o), CancellationToken.None);

        var v2 = Vehiculo.Crear(empresaId, "XYZ-987", "VL", 2023, pesoBrutoVehicular: 12m);
        var o2 = Operador.Crear(empresaId, "OPER020202BBB", "Pedro López", "LIC-67890");
        db.Vehiculos.Add(v2);
        db.Operadores.Add(o2);
        await db.SaveChangesAsync();

        var segunda = await TramoHandler(db, empresaId, sender).Handle(new CrearSiguienteTramoCommand(
            primera.Id, "T", Guid.NewGuid(), "Cancún", "Cozumel", 60m, v2.Id, o2.Id, Ahora.AddHours(5), Ahora.AddHours(8), 0m, null,
            OrigenCodigoPostal: "77500", OrigenEstado: "ROO",
            DestinoCodigoPostal: "77600", DestinoEstado: "ROO"),
            CancellationToken.None);

        segunda.CartaPortePreviaId.Should().Be(primera.Id);
        var cp2 = await db.CartasPorte.Include(c => c.Mercancias).FirstAsync(c => c.Id == segunda.Id);
        cp2.Mercancias.Should().HaveCount(1); // heredada de la primera
        cp2.Origen.Should().Be("Cancún");
        cp2.OrigenCodigoPostal.Should().Be("77500");
        cp2.DestinoEstado.Should().Be("ROO");
    }

    [Fact]
    public async Task SiguienteTramo_sobre_previa_no_timbrada_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (v, o) = await SembrarCatalogosAsync(db, empresaId);
        // Sembrar una CP en Borrador (no timbrada).
        var cp = Dominio.CartaPorte.CrearBorrador(empresaId, TipoComprobante.Traslado, "CP-X", 99, Guid.NewGuid(),
            null, null, Receptor(), new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "MXN", 2026, 5, "A", "B", 10m, v, o, null, null, Ahora, Ahora.AddHours(1));
        cp.AgregarMercancia("Vidrio", "43211503", "KGM", 1m, 10m, false);
        db.CartasPorte.Add(cp);
        await db.SaveChangesAsync();

        var act = () => TramoHandler(db, empresaId, new FakeSenderFoliosUnicos()).Handle(new CrearSiguienteTramoCommand(
            cp.Id, "T", Guid.NewGuid(), "B", "C", 20m, v, o, Ahora, Ahora.AddHours(1), 0m, null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CARTA_PORTE_PREVIA_NO_TIMBRADA");
    }
}
