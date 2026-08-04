using Millet.Almacen.Domain.Movimientos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Movimientos;

/// <summary>
/// Tests del agregado <see cref="MovimientoInventario"/> + VOs
/// asociados (F2-PR1). Cubre invariantes del ctor, transiciones de
/// estado y el folio.
/// </summary>
public class MovimientoInventarioTests
{
    private static Guid Emp() => Guid.NewGuid();
    private static Guid Art() => Guid.NewGuid();

    [Fact]
    public void Crear_movimiento_arranca_en_borrador()
    {
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 5, 23));

        mov.Estado.Should().Be(EstadoMovimiento.Borrador);
        mov.Folio.Should().BeNull();
        mov.Tipo.Should().Be(TipoMovimiento.EntradaCompra);
    }

    [Fact]
    public void Validar_sin_lineas_falla()
    {
        var mov = NuevoBorrador();
        var act = () => mov.Validar();
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("MOV_SIN_LINEAS");
    }

    [Fact]
    public void Validar_con_lineas_pasa_a_validado()
    {
        var mov = NuevoBorrador();
        AgregarLinea(mov, cantidad: 5, costo: 100m);
        mov.Validar();
        mov.Estado.Should().Be(EstadoMovimiento.Validado);
    }

    [Fact]
    public void Registrar_asigna_folio_y_pasa_a_registrado()
    {
        var mov = NuevoBorrador();
        AgregarLinea(mov);
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 42);
        mov.Registrar(folio, Guid.NewGuid());

        mov.Estado.Should().Be(EstadoMovimiento.Registrado);
        mov.Folio.Should().Be("M-ENT2026-000042");
        mov.RegistradoPor.Should().NotBeNull();
        mov.RegistradoAt.Should().NotBeNull();
    }

    [Fact]
    public void Registrar_sin_lineas_falla()
    {
        var mov = NuevoBorrador();
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1);
        var act = () => mov.Registrar(folio, Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("MOV_SIN_LINEAS");
    }

    [Fact]
    public void Cancelar_desde_registrado_falla()
    {
        var mov = NuevoBorrador();
        AgregarLinea(mov);
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1);
        mov.Registrar(folio, Guid.NewGuid());

        var act = () => mov.Cancelar("test");
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("MOV_NO_CANCELABLE");
    }

    [Fact]
    public void Agregar_linea_despues_de_registrar_falla()
    {
        var mov = NuevoBorrador();
        AgregarLinea(mov);
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1);
        mov.Registrar(folio, Guid.NewGuid());

        var act = () => AgregarLinea(mov);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("MOV_NO_BORRADOR");
    }

    [Fact]
    public void Linea_con_cantidad_cero_falla()
    {
        var mov = NuevoBorrador();
        var act = () => AgregarLinea(mov, cantidad: 0m);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("LINEA_MOV_CANT_NO_POSITIVA");
    }

    [Fact]
    public void Linea_con_costo_negativo_falla()
    {
        var mov = NuevoBorrador();
        var act = () => AgregarLinea(mov, costo: -1m);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("LINEA_MOV_COSTO_NEGATIVO");
    }

    [Fact]
    public void Monto_total_se_calcula_como_cantidad_por_costo()
    {
        var mov = NuevoBorrador();
        AgregarLinea(mov, cantidad: 3.5m, costo: 100.50m);
        var linea = mov.Lineas.Single();
        linea.MontoTotalMxn.Should().Be(Math.Round(3.5m * 100.50m, 2));
    }

    [Fact]
    public void Vale_pendiente_regularizacion_se_marca_a_48h()
    {
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.SalidaPorVale,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 5, 23));

        // Usar reflection / método interno: simulamos via VincularSalida.
        typeof(MovimientoInventario)
            .GetMethod("VincularSalida", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(mov, new object?[]
            {
                null,                       // rqId
                "blob-vale.pdf",            // valeBlobRef
                Guid.NewGuid(),             // personaDestinatariaId
            });

        mov.PendienteRegularizacion.Should().BeTrue();
        mov.FechaLimiteRegularizacion.Should().NotBeNull();
        var diff = mov.FechaLimiteRegularizacion!.Value - DateTimeOffset.UtcNow;
        diff.TotalHours.Should().BeApproximately(48, 1);
    }

    [Theory]
    [InlineData(TipoMovimiento.EntradaCompra, "ENT")]
    [InlineData(TipoMovimiento.SalidaConsumo, "SAL")]
    [InlineData(TipoMovimiento.SalidaPorVale, "SAL")]
    [InlineData(TipoMovimiento.DevolucionSalida, "DEV")]
    [InlineData(TipoMovimiento.AjustePositivo, "AJP")]
    [InlineData(TipoMovimiento.AjusteNegativo, "AJN")]
    [InlineData(TipoMovimiento.AjustePrecioFactura, "AJF")]
    [InlineData(TipoMovimiento.BajaPorDano, "BAJ")]
    [InlineData(TipoMovimiento.ReincorporacionTrasRevision, "REI")]
    public void Folio_usa_prefijo_correcto_por_tipo(TipoMovimiento tipo, string prefijoEsperado)
    {
        var folio = FolioMovimiento.Construir(tipo, 2026, 7);
        folio.Valor.Should().StartWith($"M-{prefijoEsperado}2026-");
    }

    [Fact]
    public void Folio_secuencial_se_zero_pad_a_6_digitos()
    {
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 42);
        folio.Valor.Should().Be("M-ENT2026-000042");
    }

    [Fact]
    public void FolioSecuencia_incrementar_devuelve_siguiente()
    {
        var sec = new FolioSecuenciaMovimiento(Guid.NewGuid(), "ENT", 2026, 41);
        sec.Incrementar().Should().Be(42);
        sec.UltimoNumero.Should().Be(42);
    }

    // ─── Helpers ───
    private static MovimientoInventario NuevoBorrador() =>
        new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 5, 23));

    private static void AgregarLinea(
        MovimientoInventario mov,
        decimal cantidad = 1m,
        decimal costo = 100m)
    {
        var linea = new LineaMovimiento(
            id: Guid.NewGuid(),
            movimientoId: mov.Id,
            posicion: mov.Lineas.Count + 1,
            articuloId: Art(),
            cantidad: cantidad,
            unidadMedida: "PZA",
            costoUnitarioMxn: costo);
        mov.AgregarLinea(linea);
    }

    // ── Almacén-por-línea PR4: helper de cabecera nivel 4 ──────────────────

    [Fact]
    public void Sin_helper_el_movimiento_nace_con_UbicacionHelperId_null()
    {
        // El almacenista capturó línea por línea: NULL = "no usó el helper".
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 7, 23));

        mov.UbicacionHelperId.Should().BeNull();
    }

    [Fact]
    public void Con_helper_el_movimiento_conserva_la_ubicacion_elegida()
    {
        var bin = Guid.NewGuid();

        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 7, 23),
            ubicacionHelperId: bin);

        mov.UbicacionHelperId.Should().Be(bin);
    }

    [Fact]
    public void Helper_en_Guid_Empty_se_normaliza_a_null()
    {
        // Guid.Empty no es un FK válido; se guarda NULL en vez de romper el
        // INSERT con una referencia inexistente.
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 7, 23),
            ubicacionHelperId: Guid.Empty);

        mov.UbicacionHelperId.Should().BeNull();
    }

    [Fact]
    public void El_helper_no_participa_en_las_invariantes_del_movimiento()
    {
        // Es dato de reportería: un movimiento sin helper valida y se registra
        // igual que uno con helper (la ubicación que manda es la de la línea).
        var mov = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Emp(),            fechaMovimiento: new DateOnly(2026, 7, 23));
        AgregarLinea(mov);

        var validar = () => mov.Validar();

        validar.Should().NotThrow();
        mov.Estado.Should().Be(EstadoMovimiento.Validado);
    }
}
