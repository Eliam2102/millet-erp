using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Application.Proformas.RegistrarConformidadProforma;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Proformas;

/// <summary>
/// M-234: conformidad del cliente por versión de la proforma.
/// Criterios: aprobar la versión 2 no aprueba la 1; aprobar una vencida da error; la conformidad guarda
/// quién, cuándo y medio.
/// </summary>
public sealed class ProformaConformidadTests
{
    // 11-nov-2026 12:00 en México.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static ProformaPedido Proforma(
        FacturacionDbContext db, Guid empresa, Guid pedidoId, int version, DateTimeOffset generadaEn, DateOnly? vigenteHasta)
    {
        var p = ProformaPedido.Generar(
            empresa, Guid.NewGuid(), pedidoId, version, "PED-2001", "{}", "[]", 116m, "MXN",
            Guid.NewGuid(), "Cajera Prueba", generadaEn, vigenteHasta);
        db.ProformasPedido.Add(p);
        return p;
    }

    /// <summary>Pedido con dos versiones: la 1 (ya vencida) y la 2 (vigente).</summary>
    private static (Guid Pedido, ProformaPedido V1, ProformaPedido V2) Escenario(FacturacionDbContext db, Guid empresa)
    {
        var pedido = Guid.NewGuid();
        var v1 = Proforma(db, empresa, pedido, 1, Ahora.AddDays(-20), new DateOnly(2026, 11, 1));
        var v2 = Proforma(db, empresa, pedido, 2, Ahora.AddDays(-1), new DateOnly(2026, 11, 25));
        db.PedidosFacturables.Add(PedidoDe(db, empresa, pedido));
        db.SaveChanges();
        return (pedido, v1, v2);
    }

    private static Millet.Facturacion.Domain.Pedidos.PedidoFacturable PedidoDe(FacturacionDbContext _, Guid empresa, Guid id)
    {
        var p = Millet.Facturacion.Domain.Pedidos.PedidoFacturable.ImportarDesdeAw(
            empresa, "PED-2001", Guid.NewGuid(), Guid.NewGuid(), "Cliente prueba", 1,
            Millet.Facturacion.Domain.Facturas.ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        typeof(Millet.SharedKernel.Domain.BaseEntity).GetProperty("Id")!.SetValue(p, id);
        return p;
    }

    private static RegistrarConformidadProformaHandler Handler(FacturacionDbContext db, Guid empresa) =>
        new(db, new FakeAlcanceCajaEvaluator(), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora));

    private static RegistrarConformidadProformaCommand Comando(Guid pedido, Guid proforma, DateOnly? fecha = null) =>
        new(pedido, proforma, "  Contacto Prueba ", MedioConformidadProforma.Correo, fecha, "  ok por correo ");

    [Fact]
    public async Task Registra_quien_cuando_y_medio_en_la_version_indicada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, _, v2) = Escenario(db, empresa);

        var r = await Handler(db, empresa).Handle(Comando(pedido, v2.Id), CancellationToken.None);

        Assert.Equal(v2.Id, r.ProformaId);
        Assert.Equal("Contacto Prueba", r.Quien);
        Assert.Equal("Correo", r.Medio);
        Assert.Equal(new DateOnly(2026, 11, 11), r.Fecha); // sin fecha = hoy (día fiscal en México)
        Assert.Equal("ok por correo", r.Notas);
        Assert.Equal("Cajera Prueba", r.RegistradaPor);
    }

    [Fact]
    public async Task Aprobar_la_version_2_no_aprueba_la_1()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, v1, v2) = Escenario(db, empresa);

        await Handler(db, empresa).Handle(Comando(pedido, v2.Id), CancellationToken.None);

        var conformidades = await db.ProformasConformidad.ToListAsync();
        var unica = Assert.Single(conformidades);
        Assert.Equal(v2.Id, unica.ProformaPedidoId);
        Assert.DoesNotContain(conformidades, c => c.ProformaPedidoId == v1.Id);
    }

    [Fact]
    public async Task Aprobar_una_proforma_vencida_da_error_y_no_guarda_nada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, v1, _) = Escenario(db, empresa); // v1 venció el 1-nov

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa).Handle(Comando(pedido, v1.Id), CancellationToken.None));

        Assert.Equal("FAC_PROFORMA_VENCIDA", ex.Code);
        Assert.Empty(db.ProformasConformidad);
    }

    [Fact]
    public async Task El_ultimo_dia_de_vigencia_todavia_se_puede_aprobar()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Guid.NewGuid();
        var p = Proforma(db, empresa, pedido, 1, Ahora.AddDays(-15), new DateOnly(2026, 11, 11));
        db.PedidosFacturables.Add(PedidoDe(db, empresa, pedido));
        db.SaveChanges();

        var r = await Handler(db, empresa).Handle(Comando(pedido, p.Id), CancellationToken.None);

        Assert.Equal(p.Id, r.ProformaId);
    }

    [Fact]
    public async Task Una_version_solo_acepta_una_conformidad()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, _, v2) = Escenario(db, empresa);
        await Handler(db, empresa).Handle(Comando(pedido, v2.Id), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa).Handle(Comando(pedido, v2.Id), CancellationToken.None));

        Assert.Equal("FAC_PROFORMA_YA_CONFORME", ex.Code);
    }

    [Theory]
    [InlineData(2026, 11, 12, "FAC_PROFORMA_CONFORMIDAD_FECHA_FUTURA")]
    [InlineData(2026, 11, 9, "FAC_PROFORMA_CONFORMIDAD_FECHA_ANTERIOR")] // v2 se generó el 10-nov
    public async Task La_fecha_no_puede_ser_futura_ni_anterior_a_la_generacion(int a, int m, int d, string codigo)
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, _, v2) = Escenario(db, empresa);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa).Handle(Comando(pedido, v2.Id, new DateOnly(a, m, d)), CancellationToken.None));

        Assert.Equal(codigo, ex.Code);
    }

    [Fact]
    public async Task Proforma_de_otro_pedido_responde_no_encontrada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, _, _) = Escenario(db, empresa);
        var ajena = Proforma(db, empresa, Guid.NewGuid(), 1, Ahora, new DateOnly(2026, 11, 25));
        db.SaveChanges();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            Handler(db, empresa).Handle(Comando(pedido, ajena.Id), CancellationToken.None));
    }

    [Fact]
    public async Task La_ultima_proforma_trae_su_conformidad()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, _, v2) = Escenario(db, empresa);
        var sinConformidad = await new UltimaProformaPedidoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new UltimaProformaPedidoQuery(pedido), CancellationToken.None);
        Assert.Null(sinConformidad!.Conformidad);

        await Handler(db, empresa).Handle(Comando(pedido, v2.Id), CancellationToken.None);

        var ultima = await new UltimaProformaPedidoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new UltimaProformaPedidoQuery(pedido), CancellationToken.None);
        Assert.Equal("Contacto Prueba", ultima!.Conformidad!.Quien);
        Assert.Equal(2, ultima.Version);
    }
}
