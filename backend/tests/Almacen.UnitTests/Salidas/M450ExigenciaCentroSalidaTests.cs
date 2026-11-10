using Millet.Almacen.Application.Salidas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// M-450 (86e3zr7ft): la salida de consumo (requisición o vale) con una línea sin centro de costo no se registra si la cuenta de
/// consumo exige área o equipo (regla C-03). Datos ficticios.
/// </summary>
public sealed class M450ExigenciaCentroSalidaTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Centro = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 11, 9);

    [Fact]
    public async Task Linea_sin_centro_con_cuenta_que_exige_area_no_sale()
    {
        var puerto = new Puerto(exigeArea: true, exigeEquipo: true);

        var act = () => ExigenciaCentroSalida.VerificarAsync(puerto, Empresa, Sucursal, Dia, [Centro, null], "del vale no trae centro de costo", default);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("SALIDA_SIN_AREA_EQUIPO");
        ex.Message.Should().Contain("501.FIX").And.Contain("el área y el equipo").And.Contain("la línea 2");
        puerto.Recibida!.TipoEvento.Should().Be(ExigenciaCentroSalida.EventoConsumo);
        puerto.Recibida.Campo.Should().Be(ExigenciaCentroSalida.CampoConsumo);
    }

    [Fact]
    public async Task Todas_las_lineas_con_centro_no_consultan_a_Contabilidad()
    {
        var puerto = new Puerto(exigeArea: true, exigeEquipo: false);

        await ExigenciaCentroSalida.VerificarAsync(puerto, Empresa, Sucursal, Dia, [Centro, Centro], "x", default);

        puerto.Recibida.Should().BeNull();
    }

    [Fact]
    public async Task Sin_exigencia_la_linea_sin_centro_sale()
    {
        var puerto = new Puerto(exigeArea: false, exigeEquipo: false);

        await ExigenciaCentroSalida.VerificarAsync(puerto, Empresa, Sucursal, Dia, [null], "x", default);

        puerto.Recibida.Should().NotBeNull();
    }

    [Fact]
    public async Task Sin_sucursal_o_sin_puerto_no_hay_exigencia_que_revisar()
    {
        await ExigenciaCentroSalida.VerificarAsync(new Puerto(true, true), Empresa, null, Dia, [null], "x", default);
        await ExigenciaCentroSalida.VerificarAsync(null, Empresa, Sucursal, Dia, [null], "x", default);
    }

    private sealed class Puerto(bool exigeArea, bool exigeEquipo) : IDimensionesSalidaInventarioPort
    {
        public SolicitudDimensionesSalida? Recibida { get; private set; }

        public Task<DimensionesSalidaResultado> ResolverAsync(SolicitudDimensionesSalida solicitud, CancellationToken ct)
        {
            Recibida = solicitud;
            return Task.FromResult(new DimensionesSalidaResultado(null, null, null, exigeArea, exigeEquipo, "501.FIX"));
        }
    }
}
