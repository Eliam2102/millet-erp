using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// F1-MP-01 S1 (E1): medida de hoja en la línea de OC. Todo o nada, hojas enteras (2440 × 3660 = 8.9304 m² por hoja), unidad M2,
/// revalidación al editar, el cambio de artículo limpia la medida y «Enviar a autorización» nombra las posiciones sin medida.
/// </summary>
public class LineaOrdenCompraMedidaTests
{
    private const decimal Hoja = 8.9304m;
    private static readonly Guid Variante = Guid.NewGuid();
    private static readonly Guid Cc = Guid.NewGuid();

    private static OrdenCompra NuevaOc() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Folio.Parse("OC-MID2026-000070"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        sinRequisicionPrevia: true,
        motivoSinRequisicion: "manual");

    private static LineaOrdenCompra Linea(
        OrdenCompra oc, decimal cantidad, string unidad = "M2", Guid? articulo = null, bool conMedida = true,
        Guid? variante = null, decimal? ancho = 2440m, decimal? alto = 3660m) =>
        oc.AgregarLineaManual(Guid.CreateVersion7(), articulo ?? Guid.CreateVersion7(), cantidad, unidad, 100m, Guid.CreateVersion7(),
            centroCostoId: Cc,
            productoAwVarianteId: conMedida ? variante ?? Variante : null, anchoMm: conMedida ? ancho : null, altoMm: conMedida ? alto : null);

    [Fact]
    public void Con_medida_y_hojas_enteras_se_guarda_y_calcula_el_area_por_hoja()
    {
        var l = Linea(NuevaOc(), 20 * Hoja);

        l.ProductoAwVarianteId.Should().Be(Variante);
        (l.AnchoMm, l.AltoMm, l.AreaHojaM2).Should().Be((2440m, 3660m, Hoja));
        l.Cantidad.Should().Be(178.6080m);
    }

    [Fact]
    public void Sin_medida_no_se_exige_nada_ni_hojas_enteras()
    {
        var l = Linea(NuevaOc(), 1.5m, "PZA", conMedida: false);

        (l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm, l.AreaHojaM2).Should().Be((null, null, null, null));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(8.93)]
    public void Cantidad_que_no_es_un_numero_entero_de_hojas_se_rechaza(double cantidad)
    {
        var oc = NuevaOc();

        var ex = Assert.Throws<BusinessRuleException>(() => Linea(oc, (decimal)cantidad));

        ex.Code.Should().Be("HOJAS_ENTERAS");
        oc.Lineas.Should().BeEmpty();
    }

    [Fact]
    public void Con_medida_la_unidad_debe_ser_M2()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Linea(NuevaOc(), 20 * Hoja, "PZA"));

        ex.Code.Should().Be("LINEA_OC_VIDRIO_UNIDAD");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void La_medida_va_completa_o_no_va(bool soloVariante, bool soloAncho)
    {
        var oc = NuevaOc();

        var ex = Assert.Throws<BusinessRuleException>(() => oc.AgregarLineaManual(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 20 * Hoja, "M2", 100m, Guid.CreateVersion7(), centroCostoId: Cc,
            productoAwVarianteId: soloAncho ? null : Variante, anchoMm: soloVariante ? null : 2440m, altoMm: null));

        ex.Code.Should().Be("LINEA_OC_MEDIDA_INCOMPLETA");
    }

    [Fact]
    public void Editar_la_cantidad_revalida_hojas_enteras_y_editar_la_unidad_revalida_M2()
    {
        // Un agregado que lanzó se descarta (la petición falla sin guardar): cada rechazo va sobre una OC nueva.
        var ocA = NuevaOc();
        var a = Linea(ocA, 20 * Hoja);
        Assert.Throws<BusinessRuleException>(() => ocA.ActualizarLinea(a.Id, cantidad: 100m)).Code.Should().Be("HOJAS_ENTERAS");
        var ocB = NuevaOc();
        var b = Linea(ocB, 20 * Hoja);
        Assert.Throws<BusinessRuleException>(() => ocB.ActualizarLinea(b.Id, unidadMedida: "PZA")).Code.Should().Be("LINEA_OC_VIDRIO_UNIDAD");

        var oc = NuevaOc();
        var l = Linea(oc, 20 * Hoja);
        oc.ActualizarLinea(l.Id, cantidad: 5 * Hoja);
        l.Cantidad.Should().Be(5 * Hoja);
    }

    [Fact]
    public void Editar_con_medida_nueva_la_reemplaza_y_cambiar_de_articulo_la_limpia()
    {
        var oc = NuevaOc();
        var l = Linea(oc, 20 * Hoja);
        var otra = Guid.NewGuid();

        oc.ActualizarLinea(l.Id, cantidad: 15 * 7.0620m, productoAwVarianteId: otra, anchoMm: 2140m, altoMm: 3300m);
        (l.ProductoAwVarianteId, l.AreaHojaM2).Should().Be((otra, 7.0620m));

        oc.ActualizarLinea(l.Id, articuloId: Guid.NewGuid(), unidadMedida: "PZA", cantidad: 1m);
        (l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm).Should().Be((null, null, null));
    }

    [Fact]
    public void Enviar_a_autorizacion_rechaza_las_lineas_de_vidrio_sin_medida_y_lista_sus_posiciones()
    {
        var oc = NuevaOc();
        var vidrio = Guid.NewGuid();
        Linea(oc, 300m, articulo: vidrio, conMedida: false); // 1: nacida de RQ, sin medida
        Linea(oc, 1m, "PZA", conMedida: false); // 2: insumo
        Linea(oc, 300m, articulo: vidrio, conMedida: false); // 3
        Linea(oc, 10 * Hoja, articulo: vidrio); // 4: ya con medida

        var ex = Assert.Throws<BusinessRuleException>(() => oc.EnviarAAutorizacion(DateTimeOffset.UtcNow, true, new HashSet<Guid> { vidrio }));

        ex.Code.Should().Be("VARIANTE_VIDRIO_REQUERIDA");
        ex.Message.Should().Contain("líneas 1, 3 antes");
        oc.Estado.Should().Be(EstadoOrdenCompra.Borrador);
    }

    [Fact]
    public void Enviar_a_autorizacion_pasa_cuando_todas_las_lineas_de_vidrio_tienen_medida()
    {
        var oc = NuevaOc();
        var vidrio = Guid.NewGuid();
        Linea(oc, 10 * Hoja, articulo: vidrio);

        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow, true, new HashSet<Guid> { vidrio });

        oc.Estado.Should().Be(EstadoOrdenCompra.EnAutorizacionJefeCompras);
    }

    [Fact]
    public void La_linea_desde_requisicion_nace_sin_medida_y_puede_recibirla_despues()
    {
        var oc = new OrdenCompra(Guid.CreateVersion7(), Guid.CreateVersion7(), Folio.Parse("OC-MID2026-000071"), 2026, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
        var l = oc.AgregarLineaDesdeRequisicion(Guid.CreateVersion7(), Guid.CreateVersion7(), 300m, "M2", 100m, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
        l.ProductoAwVarianteId.Should().BeNull();

        oc.ActualizarLinea(l.Id, cantidad: 33 * Hoja, productoAwVarianteId: Variante, anchoMm: 2440m, altoMm: 3660m);

        l.AreaHojaM2.Should().Be(Hoja);
    }
}
