using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25): el área o equipo capturado en la alta desde el extracto y al distribuir el costo de importación viaja
/// en el evento; el motor lo pone en la línea de la plantilla con centro de costo (concepto bancario, gasto de importación). Sin el
/// dato el evento sigue igual que antes. Payload serializado como el outbox (enums como número). Datos ficticios.
/// </summary>
public class H1CentroCapturadoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Centro = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    private static EventoContable Leer(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), JsonSerializer.Serialize(payload));

    private static EventoContable Comision(Guid? centro, Guid? movimientoBase = null) => Leer(N.MovimientoBancarioRegistrado, new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(),
        Sentido = 2, Monto = 100m, Moneda = "MXN", FechaValor = new DateOnly(2026, 11, 6), ReferenciaBancaria = "EXT-FIX",
        ConceptoId = Guid.NewGuid(), ConceptoNombre = "Comisión bancaria", Iva = 16m, SucursalId = Sucursal,
        MovimientoBaseId = movimientoBase, CentroCostoId = centro,
    });

    private static EventoContable Costo(Guid? centro) => Leer(N.CostoImportacionAplicado, new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, ExpedienteId = Guid.NewGuid(), SucursalId = Sucursal, NumeroVersion = 1,
        Partidas = new[] { new { PartidaId = Guid.NewGuid(), ArticuloId = Guid.NewGuid(), DiferenciaMxn = 10_000m, InventarioMxn = 8_000m,
            GastoImportacionMxn = 2_000m } },
        CentroCostoId = centro,
    });

    private static LineaPlantillaContable L(string rol, LadoPartida lado, string campo, bool porLinea = false, bool centro = false) =>
        new() { Rol = rol, Lado = lado, Campo = campo, PorLinea = porLinea, ConCentroCosto = centro };

    [Fact]
    public void Paso3_la_comision_lleva_el_area_capturada_en_la_linea_del_concepto()
    {
        var e = Comision(Centro);

        e.CentroCostoId.Should().Be(Centro);
        // Plantilla v2 del movimiento bancario: el concepto por línea con centro; IVA y bancos sin centro.
        GenerarPolizaDesdeEventoHandler.Importes(L("CONCEPTO_BANCARIO", LadoPartida.Cargo, "Base", porLinea: true, centro: true), e)
            .Should().ContainSingle().Which.Should().Match<(decimal Importe, Guid? Centro, ReferenciaPuente? Puente, LineaEventoContable? Linea)>(x =>
                x.Importe == 100m && x.Centro == Centro);
        GenerarPolizaDesdeEventoHandler.Importes(L("BANCOS", LadoPartida.Abono, "Total"), e)
            .Should().ContainSingle().Which.Importe.Should().Be(116m);
    }

    [Fact]
    public void Paso3_sin_area_el_movimiento_sigue_sin_centro_y_el_del_IVA_no_genera_poliza()
    {
        Comision(null).CentroCostoId.Should().BeNull("compatible: los eventos de antes no traen el campo");
        var iva = Comision(Centro, movimientoBase: Guid.NewGuid());
        iva.Importes["Total"].Should().Be(0m);
        iva.CentroCostoId.Should().BeNull("el centro va solo con la base");
    }

    [Fact]
    public void Paso4_el_costo_de_importacion_lleva_el_centro_en_el_cargo_a_gasto_y_no_en_el_inventario()
    {
        var e = Costo(Centro);

        e.CentroCostoId.Should().Be(Centro);
        GenerarPolizaDesdeEventoHandler.Importes(L("GASTO_IMPORTACION", LadoPartida.Cargo, "GastoImportacion", centro: true), e)
            .Should().ContainSingle().Which.Should().Match<(decimal Importe, Guid? Centro, ReferenciaPuente? Puente, LineaEventoContable? Linea)>(x =>
                x.Importe == 2_000m && x.Centro == Centro);
        Costo(null).CentroCostoId.Should().BeNull();
    }
}
