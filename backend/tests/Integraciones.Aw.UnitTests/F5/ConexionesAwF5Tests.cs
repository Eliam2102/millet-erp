using Microsoft.Extensions.Logging.Abstractions;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application.Integration.Tablero;
using static Millet.Integraciones.Aw.Infrastructure.Pedidos.AwSolicitudesSqlReader;

namespace Millet.Integraciones.Aw.UnitTests.F5;

/// <summary>Flujo F5 (11-nov): reglas de Jorge para clientes, hoja entera, Tax ID, ingesta y lector de NC.</summary>
public sealed class ConexionesAwF5Tests
{
    // ---- N-08 / M-299: KZ_GESPERRT 0 activo, 1 bloqueado; KZ_STATUS no se usa ----
    private static readonly MapeoEstatusClientesAwOptions Mapeo = new()
    {
        Version = "jt10nov",
        KzGesperrt = { [0] = false, [1] = true },
    };

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, true)]
    [InlineData(null, 0, false)]
    public void Estatus_sale_de_KZ_GESPERRT_e_ignora_KZ_STATUS(int? kzStatus, int kzGesperrt, bool bloqueado)
    {
        Mapeo.TieneCodigoSinMapeo(kzStatus, kzGesperrt).Should().BeFalse();
        Mapeo.Resolver(kzStatus, kzGesperrt).Should().Be(bloqueado);
    }

    [Fact]
    public void Marca_de_bloqueo_sin_equivalencia_queda_pendiente()
    {
        Mapeo.TieneCodigoSinMapeo(1, 2).Should().BeTrue();
        Mapeo.Resolver(1, 2).Should().BeNull();
    }

    // ---- M-294: Tax ID del extranjero = KU_KUNDEN.EXTERNE_KNR ----
    private static AwClienteOrigenFila Cliente(string? externeKnr) => new(
        7, 1, "GLASS CORP", null, null, "MAIN ST 1", "MIAMI", "33101", "FL", "USA", null, null,
        null, null, null, "CONTADO", "USD", null, null, null, 1, 0, null, null, [], ExterneKnr: externeKnr);

    [Theory]
    [InlineData(" 12-3456789 ", "12-3456789")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Tax_id_de_A_W_llega_al_snapshot(string? externeKnr, string? esperado) =>
        AwClienteSnapshotMapper.Mapear(Cliente(externeKnr), new Dictionary<string, string>(), DateTime.UtcNow)
            .Snapshot!.NumRegIdTrib.Should().Be(esperado);

    [Fact]
    public void Lector_de_clientes_pide_EXTERNE_KNR_solo_en_SQL_Server()
    {
        AwClientesSqlOrigen.SqlPagina.Should().Contain("k.EXTERNE_KNR");
        AwClientesSqlOrigen.Columnas.Should().NotContain("EXTERNE_KNR"); // el origen demo en Postgres no la tiene
    }

    // ---- N-09: hoja entera = familia 2*, medidas > 0, artículo activo ----
    [Fact]
    public void Medidas_de_hoja_entera_siguen_la_regla_de_Jorge()
    {
        var sql = AwProductosSqlOrigen.SqlMedidas;
        sql.Should().Contain("BA_ABW_WGR LIKE '2%'").And.Contain("BA_BREITE > 0").And.Contain("BA_HOEHE > 0")
            .And.Contain("KZ_GESPERRT = 0");
    }

    // ---- 86e40b4mp / M-233: dato inválido va a la bandeja con la diferencia ----
    private static CabeceraRow Cab(decimal? totalCantidad = null) => new(
        "10432218", "CIRCUITO", "56380", "GLOBAL", "GCO123456AB9", "G03", "PUE", "03", "CONTADO", "MXN",
        null, null, null, "Ventas Circuito", "MostradorInmediato", new DateOnly(2026, 11, 1), null, "15",
        totalCantidad, null, null, null, null);

    private static LineaRow Lin(string descripcion = "VIDRIO CLARO 6MM") => new(
        1, "5137", descripcion, null, 3m, "M2", 100m, 0m, 0m, null, null, null, null, null);

    [Fact]
    public void Posicion_sin_descripcion_es_dato_invalido_y_no_revienta()
    {
        var r = Construir(Cab(), [Lin("  ")], [], Guid.NewGuid(), 1, new AwPedidosOptions(), NullLogger.Instance);
        r.Datos.Should().BeNull();
        r.EsperaConfiguracion.Should().BeFalse();
        r.Detalle.Should().Contain("sin descripción");
    }

    [Fact]
    public void Total_de_cantidad_que_no_cuadra_trae_campo_y_valores()
    {
        var r = Construir(Cab(totalCantidad: 5m), [Lin()], [], Guid.NewGuid(), 1, new AwPedidosOptions(), NullLogger.Instance);
        r.Motivo.Should().Be(MotivoExcepcion.TotalesNoCuadran);
        r.Diferencia.Should().Be(new DiferenciaCampoIngesta("total_cantidad", "3", "5"));
    }

    [Fact]
    public void Detalle_de_excepcion_se_recorta_a_1000()
    {
        var e = ExcepcionImportacion.Crear(Guid.NewGuid(), Millet.Facturacion.Domain.Pedidos.OrigenPedido.Aw, "1", MotivoExcepcion.Otro, new string('x', 1500));
        e.Detalle!.Length.Should().Be(1000);
        e.Actualizar(MotivoExcepcion.TotalesNoCuadran, "d", new("importe_total", "10", "12"));
        (e.Campo, e.ValorRecibido, e.ValorEsperado).Should().Be(("importe_total", "10", "12"));
    }

    // ---- M-454: lector de NC encendido sin cadena = error visible ----
    [Fact]
    public async Task Lector_NC_sin_cadena_queda_en_error_en_el_tablero()
    {
        var r = await new NotasCreditoAwFuenteTablero(false, NullLogger<NotasCreditoAwFuenteTablero>.Instance)
            .LeerAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);
        r.EnError.Should().Be(1);
        r.Mensajes.Single().Estado.Should().Be(EstadoMensajeIntegracion.Error);
        r.Mensajes.Single().UltimoError.Should().Contain("AwClientesDb");

        (await new NotasCreditoAwFuenteTablero(true, NullLogger<NotasCreditoAwFuenteTablero>.Instance)
            .LeerAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None)).EnError.Should().Be(0);
    }
}
