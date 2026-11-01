using Millet.Administracion.Application.Auditoria;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compartido.UnitTests;

/// <summary>ADM-BIT (86e3xd4ut): renglón de control del CSV y documento raíz de la cuenta bancaria del proveedor.</summary>
public sealed class BitacoraAdmBitTests
{
    private static readonly DateTimeOffset Generado = new(2026, 10, 30, 18, 5, 0, TimeSpan.Zero);

    [Fact]
    public void El_renglon_de_control_lleva_total_rango_filtros_usuario_y_hora_en_12_columnas()
    {
        var filtros = new ConsultarBitacoraQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30),
            Modulo: "Compras", Accion: "actualizar", ZonaHoraria: "America/Mexico_City");

        var control = BitacoraCsv.Control(137, filtros, "Auditor Uno", Generado);

        control.Split("\",\"").Should().HaveCount(12);
        control.Should().StartWith("\"Total 137\",\"Rango 2026-10-01 a 2026-10-30 (America/Mexico_City)\"");
        control.Should().Contain("\"Filtros: modulo=Compras; accion=actualizar\"");
        control.Should().Contain("\"Exportó Auditor Uno\"");
        control.Should().Contain("\"Generado 2026-10-30T18:05:00.0000000+00:00\"");
    }

    [Fact]
    public void Sin_filtros_el_renglon_de_control_lo_dice_y_un_nombre_con_formula_se_neutraliza()
    {
        var control = BitacoraCsv.Control(0, new ConsultarBitacoraQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)),
            "=HYPERLINK(\"x\")", Generado);

        control.Should().Contain("\"Total 0\"").And.Contain("(UTC)").And.Contain("\"Filtros: ninguno\"");
        control.Should().Contain("\"Exportó =HYPERLINK(\"\"x\"\")\"", "el nombre no empieza la celda, no es fórmula");
    }

    [Fact]
    public void La_cuenta_bancaria_del_proveedor_pertenece_al_proveedor_en_la_bitacora()
    {
        var proveedorId = Guid.NewGuid();
        var cuenta = CuentaBancariaProveedor.Capturar(proveedorId, "Banorte", "072180000000004569", null, null, "USD",
            "Proveedor SA", Guid.NewGuid(), "Capturista", Generado);

        cuenta.Should().BeAssignableTo<IBelongsToAggregate>().Which.AggregateRootId.Should().Be(proveedorId);
    }
}
