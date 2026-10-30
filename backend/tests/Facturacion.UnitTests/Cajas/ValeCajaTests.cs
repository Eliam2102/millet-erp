using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Cajas.Vales;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf.ValeCaja;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Vale de Caja (86e3ww6tw): folio consecutivo por caja fijado al registrar el
/// retiro, modelo de impresión (importe en letra, casillas de forma de pago) y
/// plantilla con la fuente del kit. Datos ficticios.
/// </summary>
public sealed class ValeCajaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Expedicion = new(2026, 7, 10);
    private static readonly Dictionary<string, string> FormasPago = new() { ["04"] = "Tarjeta de crédito" };
    private const string Motivo = "Pago de flete ficticio de prueba";

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    [Fact]
    public async Task Retiros_toman_folios_consecutivos_por_caja_y_el_deposito_no_lleva_folio()
    {
        using var db = NewDb();
        var cajaA = Caja.Crear(Empresa, "Caja A", null);
        var cajaB = Caja.Crear(Empresa, "Caja B", null);
        var sesionA = CajaSesion.Abrir(Empresa, cajaA.Id, SucursalId, Cajero, 500m, Expedicion, Ahora, null);
        var sesionB = CajaSesion.Abrir(Empresa, cajaB.Id, SucursalId, Cajero, 500m, Expedicion, Ahora, null);
        db.AddRange(cajaA, cajaB, sesionA, sesionB);
        await db.SaveChangesAsync();
        var handler = new RegistrarCajaMovimientoHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
            new FakeClock(Ahora), new FakeSucursalesReadPort(), new FakeCatalogosSatReadPort(),
            new FakeIntegrationEventPublisher());

        Task<CajaMovimientoRegistradoResponse> Registrar(Guid sesion, TipoCajaMovimiento tipo) =>
            handler.Handle(new RegistrarCajaMovimientoCommand(sesion, tipo, "01", 100m, Motivo), CancellationToken.None);

        var primero = await Registrar(sesionA.Id, TipoCajaMovimiento.Retiro);
        var deposito = await Registrar(sesionA.Id, TipoCajaMovimiento.Deposito);
        var segundo = await Registrar(sesionA.Id, TipoCajaMovimiento.Retiro);
        var deOtraCaja = await Registrar(sesionB.Id, TipoCajaMovimiento.Retiro);

        primero.FolioVale.Should().Be(1);
        segundo.FolioVale.Should().Be(2);
        deposito.FolioVale.Should().BeNull();
        deOtraCaja.FolioVale.Should().Be(1);
        (await db.Cajas.SingleAsync(c => c.Id == cajaA.Id)).UltimoFolioVale.Should().Be(2);
        (await db.CajaMovimientos.SingleAsync(m => m.Id == segundo.Id)).CajaId.Should().Be(cajaA.Id);
    }

    [Fact]
    public void El_folio_solo_se_asigna_una_vez_y_solo_a_retiros()
    {
        var deposito = CajaMovimiento.Crear(Empresa, Guid.NewGuid(), TipoCajaMovimiento.Deposito, "01", 100m, Cajero, "Depósito");
        var retiro = Retiro("01", 100m, folio: 7);

        var enDeposito = () => deposito.AsignarFolioVale(Guid.NewGuid(), 1);
        var otraVez = () => retiro.AsignarFolioVale(Guid.NewGuid(), 8);

        enDeposito.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VALE_SOLO_RETIRO");
        otraVez.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VALE_FOLIO_ASIGNADO");
        retiro.FolioVale.Should().Be(7);
    }

    [Fact]
    public void El_modelo_lleva_folio_cajero_importe_en_letra_motivo_y_efectivo_marcado()
    {
        var modelo = Modelo(Retiro("01", 3000m, folio: 12), new DateOnly(2026, 7, 11));

        modelo.Folio.Should().Be(12);
        modelo.FechaExpedicion.Should().Be(Expedicion);
        modelo.FechaImpresion.Should().Be(new DateOnly(2026, 7, 11));
        modelo.Cajero.Should().Be("Cajera Ficticia");
        modelo.Importe.Should().Be(3000m);
        modelo.ImporteEnLetra.Should().Be("TRES MIL PESOS 00/100 M.N.");
        modelo.Concepto.Should().Be(Motivo);
        modelo.FormaPago.Should().Be(FormaPagoVale.Efectivo);
        modelo.FormaPagoOtro.Should().BeNull();
        modelo.EmpresaNombre.Should().Be("Empresa Ficticia SA de CV");
        modelo.NombreArchivo.Should().Be("vale-de-caja-12.pdf");
    }

    [Theory]
    [InlineData("02", FormaPagoVale.Cheque, null)]
    [InlineData("04", FormaPagoVale.Otro, "04 - Tarjeta de crédito")]
    [InlineData("28", FormaPagoVale.Otro, "28")]
    public void La_forma_de_pago_marca_su_casilla(string clave, FormaPagoVale casilla, string? otro)
    {
        var modelo = Modelo(Retiro(clave, 250.5m, folio: 1), Expedicion);

        modelo.FormaPago.Should().Be(casilla);
        modelo.FormaPagoOtro.Should().Be(otro);
        modelo.ImporteEnLetra.Should().Be("DOSCIENTOS CINCUENTA PESOS 50/100 M.N.");
    }

    [Fact]
    public void La_reimpresion_conserva_los_datos_y_solo_cambia_la_fecha_de_impresion()
    {
        var retiro = Retiro("01", 3000m, folio: 3);

        var impresion = Modelo(retiro, Expedicion);
        var reimpresion = Modelo(retiro, Expedicion.AddDays(5));

        reimpresion.Should().BeEquivalentTo(impresion with { FechaImpresion = Expedicion.AddDays(5) });
    }

    [Fact]
    public void Un_deposito_no_tiene_vale()
    {
        var deposito = CajaMovimiento.Crear(Empresa, Guid.NewGuid(), TipoCajaMovimiento.Deposito, "01", 100m, Cajero, "Depósito");

        var act = () => Modelo(deposito, Expedicion);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VALE_SOLO_RETIRO");
    }

    [Fact]
    public void La_plantilla_genera_el_pdf_con_Carlito()
    {
        var pdf = new QuestPdfValeCajaGenerator().Generar(Modelo(Retiro("04", 3000m, folio: 1), Expedicion));

        var texto = Encoding.Latin1.GetString(pdf.Contenido);
        texto.Should().StartWith("%PDF");
        texto.Should().Contain("Carlito").And.NotContain("Calibri");
        pdf.ContentType.Should().Be("application/pdf");
        pdf.NombreSugerido.Should().Be("vale-de-caja-1.pdf");
        ValeCajaDocument.Fecha(new DateOnly(2026, 7, 5)).Should().Be("05 / 07 / 2026");
    }

    [Fact]
    public void Con_logo_la_plantilla_incrusta_la_imagen_y_conserva_el_nombre_de_la_empresa()
    {
        var logo = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var conLogo = Modelo(Retiro("01", 3000m, folio: 1), Expedicion) with { Logo = logo };

        var sinLogo = Encoding.Latin1.GetString(new QuestPdfValeCajaGenerator().Generar(conLogo with { Logo = null }).Contenido);
        var texto = Encoding.Latin1.GetString(new QuestPdfValeCajaGenerator().Generar(conLogo).Contenido);

        texto.Should().Contain("/Subtype /Image");
        sinLogo.Should().NotContain("/Subtype /Image");
    }

    private static CajaMovimiento Retiro(string formaPago, decimal importe, int folio)
    {
        var retiro = CajaMovimiento.Crear(
            Empresa, Guid.NewGuid(), TipoCajaMovimiento.Retiro, formaPago, -importe, Cajero, Motivo);
        retiro.AsignarFolioVale(Guid.NewGuid(), folio);
        return retiro;
    }

    private static ValeCajaPdfModel Modelo(CajaMovimiento retiro, DateOnly impresion) =>
        ValeCajaPdfModelBuilder.Desde(
            retiro, "Cajera Ficticia", "Empresa Ficticia SA de CV", null, FormasPago, Expedicion, impresion);
}
