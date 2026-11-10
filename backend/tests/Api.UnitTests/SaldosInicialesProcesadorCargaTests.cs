using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Infrastructure.Adapters;
using Millet.Compartido.Application.Cargas.Lotes;
using Millet.Identidad.Domain;

namespace Millet.Api.UnitTests;

/// <summary>M-166 ↔ M-047: el lote común encuentra el tipo «contabilidad.saldos-iniciales» y sus filas llegan como balanza de apertura.</summary>
public sealed class SaldosInicialesProcesadorCargaTests
{
    private static readonly string[] ColumnasEsperadas = ["cuenta", "cargo", "abono", "tipoAuxiliar", "claveAuxiliar", "sucursal", "concepto"];
    private static readonly string[] ObligatoriasEsperadas = ["cuenta", "cargo", "abono", "sucursal"];

    [Fact]
    public void El_tipo_de_carga_de_saldos_iniciales_queda_registrado_para_el_lote_comun()
    {
        var services = new ServiceCollection();

        services.AddCargasContabilidad();

        var registro = Assert.Single(services, d => d.ServiceType == typeof(IProcesadorCarga));
        Assert.Equal(typeof(SaldosInicialesProcesadorCarga), registro.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registro.Lifetime);
    }

    [Fact]
    public void Declara_tipo_columnas_fecha_de_corte_y_permisos_de_polizas()
    {
        var p = new SaldosInicialesProcesadorCarga(null!, null!, null!);

        Assert.Equal("contabilidad.saldos-iniciales", p.Tipo);
        Assert.True(p.RequiereFechaCorte);
        Assert.Equal(ColumnasEsperadas, p.Columnas.Select(c => c.Nombre));
        Assert.Equal(ObligatoriasEsperadas, p.ColumnasObligatorias);
        Assert.Equal(PermisosCanonicos.ContabilidadPolizasCapturar, p.PermisoSubir);
        Assert.Equal(PermisosCanonicos.ContabilidadPolizasAutorizar, p.PermisoAutorizar);
    }

    [Fact]
    public void Las_filas_del_lote_llegan_como_balanza_de_apertura_y_un_importe_no_numerico_es_error_de_su_fila()
    {
        var filas = new[]
        {
            new FilaCarga(2, new Dictionary<string, string> { ["cuenta"] = " 102.01.01.00 ", ["cargo"] = "$1,500.00", ["abono"] = "", ["sucursal"] = "MID" }),
            new FilaCarga(3, new Dictionary<string, string>
            {
                ["cuenta"] = "105.01.00.00", ["cargo"] = "500", ["tipoAuxiliar"] = "Cliente", ["claveAuxiliar"] = "C001", ["sucursal"] = "MID",
            }),
            new FilaCarga(4, new Dictionary<string, string> { ["cuenta"] = "301.01.00.00", ["abono"] = "dos mil", ["sucursal"] = "MID" }),
        };

        var (balanza, errores) = SaldosInicialesProcesadorCarga.Convertir(filas);

        Assert.Equal((2, "102.01.01.00", 1_500m, 0m, (string?)null), (balanza[0].Fila, balanza[0].Cuenta, balanza[0].Cargo, balanza[0].Abono, balanza[0].TipoAuxiliar));
        Assert.Equal(("Cliente", "C001", "MID"), (balanza[1].TipoAuxiliar, balanza[1].ClaveAuxiliar, balanza[1].Sucursal));
        var error = Assert.Single(errores);
        Assert.Equal((4, "abono", "AP_IMPORTE_INVALIDO"), (error.Fila, error.Campo, error.Codigo));
    }
}
