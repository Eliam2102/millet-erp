using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas.ReenviarCfdiCorreo;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Envios;

public sealed class EnvioCorreoTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static FacturaVenta Factura(Guid empresaId, bool timbrar)
    {
        var f = FacturaVenta.CrearBorrador(
            empresaId, "FA-000001", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("XAXX010101000", "Público", "616", "97000", "S01", "MEX", true),
            new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Producto de prueba", "H87", 2m, 100m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        if (timbrar)
        {
            f.MarcarTimbradoEnProceso();
            f.MarcarTimbrado("UUID-1", "sc", "ss", "20001000000300022815", DateTimeOffset.UtcNow, "SAT970701NN3", Guid.NewGuid());
        }
        return f;
    }

    // ---- PDF ----

    [Fact]
    public void Pdf_bilingue_genera_bytes_no_vacios()
    {
        var gen = new QuestPdfFacturaGenerator();

        var pdf = gen.Generar(Factura(Guid.NewGuid(), timbrar: true), FormatoPdfFactura.Bilingue);

        pdf.Contenido.Should().NotBeEmpty();
        pdf.ContentType.Should().Be("application/pdf");
        pdf.NombreSugerido.Should().Contain("bilingue");
    }

    [Fact]
    public void Pdf_termica_genera_bytes_no_vacios()
    {
        var gen = new QuestPdfFacturaGenerator();

        var pdf = gen.Generar(Factura(Guid.NewGuid(), timbrar: false), FormatoPdfFactura.TermicaSimplificada);

        pdf.Contenido.Should().NotBeEmpty();
        pdf.NombreSugerido.Should().Contain("termica");
    }

    // ---- Bitácora ----

    [Fact]
    public void Bitacora_Encolar_arranca_Pendiente()
    {
        var b = BitacoraEnvioCorreo.Encolar(Guid.NewGuid(), Guid.NewGuid(), "cliente@correo.com");

        b.Estado.Should().Be(EstadoEnvioCorreo.Pendiente);
        b.Intentos.Should().Be(0);
    }

    [Fact]
    public void Bitacora_destinatario_invalido_lanza()
    {
        var act = () => BitacoraEnvioCorreo.Encolar(Guid.NewGuid(), Guid.NewGuid(), "no-es-correo");

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ENVIO_DESTINATARIO_INVALIDO");
    }

    [Fact]
    public void Bitacora_MarcarEnviado_y_Fallido_cuentan_intentos()
    {
        var b = BitacoraEnvioCorreo.Encolar(Guid.NewGuid(), Guid.NewGuid(), "c@c.com");

        b.MarcarFallido("smtp down");
        b.Estado.Should().Be(EstadoEnvioCorreo.Fallido);
        b.Intentos.Should().Be(1);

        b.MarcarEnviado(DateTimeOffset.UtcNow);
        b.Estado.Should().Be(EstadoEnvioCorreo.Enviado);
        b.Intentos.Should().Be(2);
        b.EnviadoAt.Should().NotBeNull();
    }

    // ---- ReenviarCfdiCorreoHandler ----

    [Fact]
    public async Task Reenviar_factura_timbrada_encola_bitacora()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.FacturasVenta.Add(Factura(empresaId, timbrar: true));
        await db.SaveChangesAsync();
        var factura = await db.FacturasVenta.SingleAsync();

        var handler = new ReenviarCfdiCorreoHandler(db, new FakeEmpresaContext(empresaId));
        var resp = await handler.Handle(new ReenviarCfdiCorreoCommand(factura.Id, "cliente@correo.com"), CancellationToken.None);

        resp.Estado.Should().Be("Pendiente");
        (await db.BitacorasEnvioCorreo.SingleAsync()).Destinatario.Should().Be("cliente@correo.com");
    }

    [Fact]
    public async Task Reenviar_factura_no_timbrada_lanza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.FacturasVenta.Add(Factura(empresaId, timbrar: false));
        await db.SaveChangesAsync();
        var factura = await db.FacturasVenta.SingleAsync();

        var handler = new ReenviarCfdiCorreoHandler(db, new FakeEmpresaContext(empresaId));
        var act = () => handler.Handle(new ReenviarCfdiCorreoCommand(factura.Id, "c@c.com"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_TIMBRADA");
    }

    [Fact]
    public async Task Reenviar_factura_inexistente_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var handler = new ReenviarCfdiCorreoHandler(db, new FakeEmpresaContext(empresaId));

        var act = () => handler.Handle(new ReenviarCfdiCorreoCommand(Guid.NewGuid(), "c@c.com"), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }
}
