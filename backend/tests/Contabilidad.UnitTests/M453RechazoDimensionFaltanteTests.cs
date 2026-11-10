using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-453 (86e3zr740): sin área por omisión, una póliza automática a 500/600 sin el área o el equipo que exige la regla C-03 va a
/// Rechazos como «Dato faltante», con el aviso del validador como mensaje (sin el prefijo técnico de la validación). Datos FIX.
/// </summary>
public class M453RechazoDimensionFaltanteTests
{
    private static ValidationException FaltanAreaYEquipo() => new([
        new("partidas[0].dim2Id", "CONTAB_DIM_OBLIGATORIA_FALTANTE", "Partida 1: Falta Área: es obligatoria para la cuenta 601.75 en «Gasto»."),
        new("partidas[0].dim3Id", "CONTAB_DIM_OBLIGATORIA_FALTANTE", "Partida 1: Falta Equipo: es obligatoria para la cuenta 601.75 en «Gasto»."),
        new("partidas[0]", "CONTAB_DIM_FALTANTES", "Partida 1 (cuenta 601.75): capture área y equipo."),
    ]);

    [Fact]
    public void La_dimension_obligatoria_faltante_es_dato_faltante()
    {
        var r = GenerarPolizaDesdeEventoHandler.Clasificar(FaltanAreaYEquipo());

        r.Should().NotBeNull();
        r!.Value.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
        r.Value.Detalle.Should().Be("partidas[0].dim2Id");
    }

    [Fact]
    public void El_mensaje_del_rechazo_es_el_aviso_del_validador()
    {
        var mensaje = GenerarPolizaDesdeEventoHandler.MensajeRechazo(FaltanAreaYEquipo());

        mensaje.Should().Be("Partida 1: Falta Área: es obligatoria para la cuenta 601.75 en «Gasto». "
            + "Partida 1: Falta Equipo: es obligatoria para la cuenta 601.75 en «Gasto».");
        mensaje.Should().NotContain("Errores de validación").And.NotContain("dim2Id");
    }

    [Fact]
    public void Otros_errores_conservan_el_mensaje_de_la_excepcion()
    {
        var otra = new ValidationException([new("partidas[0].dim2Id", "CONTAB_DIM_CENTRO_INACTIVO", "El centro está inactivo.")]);
        GenerarPolizaDesdeEventoHandler.MensajeRechazo(otra).Should().Be(otra.Message);

        var rechazo = new RechazoMotorException(CausaRechazoContable.RolSinCuenta, "GASTO", "El rol GASTO no tiene cuenta.");
        GenerarPolizaDesdeEventoHandler.MensajeRechazo(rechazo).Should().Be("El rol GASTO no tiene cuenta.");
    }
}
