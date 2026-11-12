using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Sustituciones;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>
/// Sustitución según el motivo SAT: 01 emite con relación 04; 02 y 04 cancelan primero y emiten sin relación;
/// 03 solo cancela. La aceptación del receptor no se pide en los casos que la regla fiscal exime.
/// </summary>
public sealed class SustitucionPorMotivoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static async Task<FacturaVenta> SembrarAsync(
        FacturacionDbContext db, Guid empresaId, string rfc = "AAA010101AAA", decimal precio = 100m, string pais = "MEX")
    {
        var receptor = new DatosFiscalesReceptor(rfc, "Cliente", "601", "97000", "G03", pais, rfc.StartsWith("XAXX", StringComparison.Ordinal));
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, precio, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static async Task CancelarAsync(FacturacionDbContext db, FacturaVenta f, string motivo)
    {
        var s = SolicitudCancelacion.Crear(f.EmpresaId, f.Id, motivo, null, Ahora, Ingresos, "Ingresos");
        s.Autorizar(Contador, "Contador", Ahora);
        s.MarcarAceptada("Cancelado", Ahora);
        f.MarcarCancelacionPendiente();
        f.MarcarCancelado();
        db.SolicitudesCancelacion.Add(s);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("01", false, true, true)]
    [InlineData("02", true, true, false)]
    [InlineData("03", true, false, false)]
    [InlineData("04", true, true, false)]
    public void La_guia_dice_el_orden_y_si_lleva_relacion_04(string motivo, bool cancelaPrimero, bool emiteNueva, bool relacion)
    {
        var g = GuiaMotivoCancelacion.Para(motivo);
        g.CancelaPrimero.Should().Be(cancelaPrimero);
        g.EmiteNueva.Should().Be(emiteNueva);
        g.LlevaRelacion04.Should().Be(relacion);
        g.Pasos.Should().NotBeEmpty();
    }

    [Fact]
    public void Un_motivo_fuera_de_01_a_04_no_tiene_guia()
    {
        var act = () => GuiaMotivoCancelacion.Para("05");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CANCELACION_MOTIVO_INVALIDO");
    }

    [Fact]
    public async Task Con_motivo_02_la_nueva_solo_se_prepara_cuando_la_original_ya_se_cancelo_con_ese_motivo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var a = await SembrarAsync(db, empresaId);

        var antes = () => SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, a.Id, "02", CancellationToken.None);
        await antes.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_CANCELAR_PRIMERO");

        await CancelarAsync(db, a, "02");
        (await SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, a.Id, "02", CancellationToken.None)).Id.Should().Be(a.Id);

        var otroMotivo = () => SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, a.Id, "04", CancellationToken.None);
        await otroMotivo.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task El_motivo_04_solo_aplica_a_una_factura_global()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var nominativa = await SembrarAsync(db, empresaId);

        var act = () => SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, nominativa.Id, "04", CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_04_SOLO_GLOBAL");

        var solicitar = new SolicitarCancelacionHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora));
        var cancelar04 = () => solicitar.Handle(new SolicitarCancelacionCommand(nominativa.Id, "04", null), CancellationToken.None);
        await cancelar04.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_04_SOLO_GLOBAL");

        var global = await SembrarAsync(db, empresaId, rfc: "XAXX010101000");
        await CancelarAsync(db, global, "04");
        (await SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, global.Id, "04", CancellationToken.None)).Id.Should().Be(global.Id);
    }

    [Fact]
    public async Task El_motivo_03_no_prepara_factura_nueva()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var a = await SembrarAsync(db, empresaId);

        var handler = new DatosSustitucionHandler(db, new FakeAlcanceCajaEvaluator(), new FakeClock(Ahora));
        var act = () => handler.Handle(new DatosSustitucionQuery(a.Id, "03"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_MOTIVO_SIN_NUEVA");
    }

    [Fact]
    public async Task Los_datos_de_sustitucion_01_llevan_relacion_04_y_los_de_02_no()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var a = await SembrarAsync(db, empresaId, precio: 5000m);
        var handler = new DatosSustitucionHandler(db, new FakeAlcanceCajaEvaluator(), new FakeClock(Ahora.AddDays(3)));

        var d01 = await handler.Handle(new DatosSustitucionQuery(a.Id), CancellationToken.None);
        d01.LlevaRelacion04.Should().BeTrue();
        d01.Motivo.Should().Be("01");
        d01.RequiereAceptacion.Should().BeTrue("5,800 MXN, tres días después y receptor nacional con RFC propio");

        await CancelarAsync(db, a, "02");
        var d02 = await handler.Handle(new DatosSustitucionQuery(a.Id, "02"), CancellationToken.None);
        d02.LlevaRelacion04.Should().BeFalse();
        d02.Pasos.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("AAA010101AAA", 100, "MEX", 3, false, "1,000")]
    [InlineData("AAA010101AAA", 5000, "MEX", 0, false, "24 horas")]
    [InlineData("XAXX010101000", 5000, "MEX", 3, false, "Público en general")]
    [InlineData("XEXX010101000", 5000, "USA", 3, false, "extranjero")]
    [InlineData("AAA010101AAA", 5000, "MEX", 3, true, null)]
    public async Task La_politica_dice_cuando_no_se_pide_aceptacion(
        string rfc, decimal precio, string pais, int dias, bool requiere, string? razon)
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = await SembrarAsync(db, empresaId, rfc, precio, pais);

        var r = PoliticaAceptacionCancelacion.Evaluar(f, Ahora.AddDays(dias));

        r.RequiereAceptacion.Should().Be(requiere);
        if (razon is not null) r.RazonesSinAceptacion.Should().Contain(x => x.Contains(razon));
    }
}
