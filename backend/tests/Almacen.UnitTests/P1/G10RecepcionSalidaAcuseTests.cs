using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Recepciones.Pdf;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Salidas.Pdf;
using Millet.Almacen.Application.Vales;
using Millet.Documentos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>G-10: rechazo en el acto, recepción impresa con quién recibió, acuse de la salida y motivo del vale urgente.</summary>
public sealed class G10RecepcionSalidaAcuseTests
{
    private static RegistrarRecepcionLineaInput Linea(P1Fixture f, decimal cantidad, decimal rechazada = 0m, decimal danada = 0m, string? evidencia = "blob://evidencia/foto.jpg") =>
        new(f.ArticuloId, f.LineaId, cantidad, null, null, f.BinId, CantidadDanada: danada, MotivoDano: danada > 0 ? "Golpe en el empaque" : null,
            CantidadRechazada: rechazada, MotivoRechazo: rechazada > 0 ? "No es el material pedido" : null, EvidenciaBlobRef: evidencia);

    private static Task<RegistrarRecepcionResponse> ConFactura(P1Fixture f, params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConFacturaHandler(f.Db, f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P7Support.Conversion(), CfdiFake.Valido(), new ProveedorRfcFake())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas, RecibidoPorNombre: "  Encargado de patio  "), default);

    private static Task<RegistrarRecepcionResponse> ConPackingList(P1Fixture f, params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConPackingListHandler(f.Db, f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, "packing.pdf", null, lineas), default);

    [Fact]
    public async Task Criterio1_partida_rechazada_en_el_acto_no_suma_existencia_y_deja_el_pendiente_en_la_orden()
    {
        await using var f = new P1Fixture();

        var r = await ConFactura(f, Linea(f, cantidad: 2, rechazada: 3));

        var mov = await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Id == r.RecepcionId);
        mov.Lineas.Sum(l => l.Cantidad).Should().Be(2m);
        var rechazo = await f.Db.RecepcionRechazos.SingleAsync();
        (rechazo.RecepcionId, rechazo.Cantidad, rechazo.Motivo).Should().Be((r.RecepcionId, 3m, "No es el material pedido"));
        f.Events.Items.OfType<OcRecepcionRegistradaIntegrationEvent>().Single().Lineas.Sum(l => l.Cantidad).Should().Be(2m);
        // Pendiente de la OC = 10 - 2 aceptadas: los 3 rechazados siguen pendientes (recibir 8 cabe; 9 no).
        var ex = await Assert.ThrowsAnyAsync<BusinessRuleException>(() => ConFactura(f, Linea(f, 9)));
        ex.Code.Should().Be("RECEPCION_EXCEDE_PENDIENTE");
        await ConFactura(f, Linea(f, 8));
    }

    [Fact]
    public async Task Rechazo_por_lista_de_empaque_tambien_queda_registrado_sin_inventario()
    {
        await using var f = new P1Fixture();

        var r = await ConPackingList(f, Linea(f, cantidad: 4, rechazada: 1));

        (await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Id == r.RecepcionId)).Lineas.Sum(l => l.Cantidad).Should().Be(4m);
        (await f.Db.RecepcionRechazos.SingleAsync()).Cantidad.Should().Be(1m);
    }

    [Fact]
    public async Task Rechazar_todas_las_partidas_no_registra_recepcion()
    {
        await using var f = new P1Fixture();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => ConFactura(f, Linea(f, cantidad: 0, rechazada: 10)));

        ex.Code.Should().Be("RECEPCION_TODO_RECHAZADO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        (await f.Db.RecepcionRechazos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Dano_con_evidencia_la_guarda_en_la_linea_de_revision()
    {
        await using var f = new P1Fixture();
        var subRev = new Domain.Catalogo.SubAlmacen(Guid.NewGuid(), (await f.Db.Almacenes.SingleAsync()).Id, "REV", "Material en revisión", Domain.Catalogo.TipoSubAlmacen.MaterialEnRevision);
        f.Db.SubAlmacenes.Add(subRev);
        f.Db.Ubicaciones.Add(new Domain.Catalogo.Ubicacion(Guid.NewGuid(), subRev.Id, "REV-01", "Revisión 1"));
        await f.Db.SaveChangesAsync();

        await ConFactura(f, Linea(f, cantidad: 5, danada: 2, evidencia: "blob://evidencia/dano.pdf"));

        var lineas = await f.Db.LineasMovimiento.ToListAsync();
        lineas.Should().ContainSingle(l => l.EvidenciaBlobRef == "blob://evidencia/dano.pdf" && l.Cantidad == 2m);
    }

    [Theory]
    [InlineData(5, 2, 0, null)]
    [InlineData(5, 0, 2, " ")]
    [InlineData(0, 0, 2, null)]
    public void Criterio1_danado_o_rechazado_sin_evidencia_se_rechaza(decimal cantidad, decimal danada, decimal rechazada, string? evidencia)
    {
        var l = new RegistrarRecepcionLineaInput(Guid.NewGuid(), Guid.NewGuid(), cantidad, null, null, Guid.NewGuid(), CantidadDanada: danada,
            MotivoDano: "Golpe", CantidadRechazada: rechazada, MotivoRechazo: "Otro material", EvidenciaBlobRef: evidencia);
        var cmdA = new RegistrarRecepcionConFacturaCommand(Guid.NewGuid(), P1Fixture.Fecha, Guid.NewGuid(), null, null, [l]);
        var cmdB = new RegistrarRecepcionConPackingListCommand(Guid.NewGuid(), P1Fixture.Fecha, "packing.pdf", null, [l]);

        new RegistrarRecepcionConFacturaValidator().Validate(cmdA).Errors.Should().Contain(e => e.PropertyName.EndsWith("EvidenciaBlobRef"));
        new RegistrarRecepcionConPackingListValidator().Validate(cmdB).Errors.Should().Contain(e => e.PropertyName.EndsWith("EvidenciaBlobRef"));
        var conEvidencia = l with { EvidenciaBlobRef = "blob://evidencia/foto.jpg" };
        new RegistrarRecepcionConFacturaValidator().Validate(cmdA with { Lineas = [conEvidencia] }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rechazo_sin_motivo_se_rechaza_y_partida_sin_nada_tambien()
    {
        var sinMotivo = new RegistrarRecepcionLineaInput(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, Guid.NewGuid(), CantidadRechazada: 1, EvidenciaBlobRef: "blob://x");
        var vacia = new RegistrarRecepcionLineaInput(Guid.NewGuid(), Guid.NewGuid(), 0, null, null, Guid.NewGuid());
        var v = new RegistrarRecepcionConFacturaValidator();
        var cmd = new RegistrarRecepcionConFacturaCommand(Guid.NewGuid(), P1Fixture.Fecha, Guid.NewGuid(), null, null, [sinMotivo]);

        v.Validate(cmd).Errors.Should().Contain(e => e.PropertyName.EndsWith("MotivoRechazo"));
        v.Validate(cmd with { Lineas = [vacia] }).Errors.Should().Contain(e => e.PropertyName.EndsWith("Cantidad"));
    }

    [Fact]
    public async Task Criterio2_la_recepcion_se_imprime_con_quien_recibio_ademas_de_quien_capturo()
    {
        await using var f = new P1Fixture();
        var r = await ConFactura(f, Linea(f, cantidad: 2, rechazada: 1));
        (await f.Db.Movimientos.SingleAsync(m => m.Id == r.RecepcionId)).RecibidoPorNombre.Should().Be("Encargado de patio");

        PdfLicencia.Inicializar();
        var impresion = new ComprobanteRecepcionImpresion(null, "ENT-2026-000100", P1Fixture.Fecha, "OC-P1", "Vidrio y Herrajes del Sureste",
            [new(1, "VID-6MM", "Vidrio claro 6 mm", "HOJA", 2, "ALM-MID-G › MP · R1")],
            [new("VID-6MM", "Vidrio claro 6 mm", "HOJA", 1, "No es el material pedido")], "REMISIÓN: R-55", "Usuario que capturó", "Encargado de patio");
        Encoding.ASCII.GetString(ComprobanteRecepcionDocumento.Generar(impresion), 0, 4).Should().Be("%PDF");
        ComprobanteRecepcionPdfHandler.Comentarios(new(r.RecepcionId, "ENT", P1Fixture.Fecha, Guid.Empty, null, null, null, null, null, null, EstadoMovimiento.Registrado,
            1, "Llegó en tarima", null, null, [], Remision: "R-55")).Should().Be("REMISIÓN: R-55 Llegó en tarima");
    }

    private static Task<RegistrarSalidaResponse> SalidaConRq(P1Fixture f, Guid? destinatario, string? acuse) =>
        new RegistrarSalidaConRequisicionHandler(f.Db, f.Rq, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db))
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, destinatario, null, [new(f.ArticuloId, f.LineaId, 1, null, null, null, null, f.BinId)], acuse), default);

    private static Task<RegistrarSalidaResponse> Vale(P1Fixture f, Guid? destinatario, string? motivo, string? acuse) =>
        new RegistrarSalidaPorValeHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P1Fixture.Calendario(), new P1Fixture.Centros(), new P7Support.Conversion(), P7Support.Apartados(f.Db))
            .Handle(new(P1Fixture.Fecha, "vale.pdf", destinatario, null, [new(f.ArticuloId, null, 1, Guid.NewGuid(), null, null, null, f.BinId)], motivo, acuse), default);

    [Theory]
    [InlineData(false, "Quien recibe", "SALIDA_SIN_DESTINATARIO")]
    [InlineData(true, null, "SALIDA_SIN_ACUSE")]
    [InlineData(true, "   ", "SALIDA_SIN_ACUSE")]
    public async Task Criterio3_salida_sin_destinatario_o_sin_acuse_no_se_confirma(bool conDestinatario, string? acuse, string codigo)
    {
        await using var f = new P1Fixture();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => SalidaConRq(f, conDestinatario ? Guid.NewGuid() : null, acuse));

        ex.Code.Should().Be(codigo);
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Criterio3_salida_con_acuse_guarda_nombre_fecha_y_hora_y_sale_en_el_comprobante()
    {
        await using var f = new P1Fixture();
        var antes = DateTimeOffset.UtcNow;

        var r = await SalidaConRq(f, Guid.NewGuid(), "  Auxiliar de mantenimiento ");

        var mov = await f.Db.Movimientos.SingleAsync(m => m.Id == r.SalidaId);
        mov.AcuseRecibeNombre.Should().Be("Auxiliar de mantenimiento");
        mov.AcuseRecibeAt.Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        var comentarios = ComprobanteSalidaPdfHandler.Comentarios(Detalle(mov));
        comentarios.Should().Contain("ACUSE: AUXILIAR DE MANTENIMIENTO ").And.MatchRegex(@"\d{2}/\d{2}/\d{4} \d{2}:\d{2}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Criterio3_vale_sin_motivo_de_urgencia_no_se_confirma(string? motivo)
    {
        await using var f = new P1Fixture();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(f, Guid.NewGuid(), motivo, "Quien recibe"));

        ex.Code.Should().Be("VALE_SIN_MOTIVO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Vale_con_motivo_destinatario_y_acuse_se_confirma_y_conserva_el_motivo()
    {
        await using var f = new P1Fixture();

        var r = await Vale(f, Guid.NewGuid(), " Paro de línea 2 ", "Auxiliar de mantenimiento");

        var mov = await f.Db.Movimientos.SingleAsync(m => m.Id == r.SalidaId);
        (mov.MotivoUrgencia, mov.AcuseRecibeNombre).Should().Be(("Paro de línea 2", "Auxiliar de mantenimiento"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(f, null, "Paro", "Quien recibe"));
    }

    private static SalidaDetalle Detalle(MovimientoInventario m) => new(m.Id, "SAL", P1Fixture.Fecha, Guid.Empty, null, null, null, null, false, null, null, null,
        EstadoMovimiento.Registrado, 1, null, null, null, null, null, [], AcuseRecibeNombre: m.AcuseRecibeNombre, AcuseRecibeAt: m.AcuseRecibeAt);
}
