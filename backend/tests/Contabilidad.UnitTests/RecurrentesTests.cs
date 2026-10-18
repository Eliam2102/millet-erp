using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>K10.4: plantilla recurrente (periodos, fechas, segregación), reversa automática en negativo y reclasificación ligada.</summary>
public class RecurrentesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Analista = Guid.NewGuid(), Supervisor = Guid.NewGuid(), Sucursal = Guid.NewGuid();

    private static TipoDocumentoContable Tipo() => new(Guid.NewGuid(), "AS", "Asiento contable", false);

    private static PartidaPlantilla P(decimal cargo, decimal abono) => new() { CuentaId = Guid.NewGuid(), Cargo = cargo, Abono = abono, SucursalId = Sucursal };

    private static PlantillaRecurrente Plantilla(int mesInicio = 11, int periodos = 12, int dia = 0, IEnumerable<PartidaPlantilla>? partidas = null) =>
        new(Guid.NewGuid(), "FIX renta", Tipo(), "Renta de oficina", 2026, mesInicio, periodos, dia, reversaAutomatica: true, naceAutorizada: true,
            partidas ?? [P(15000m, 0m), P(0m, 15000m)], Analista, "analista");

    [Fact]
    public void Los_periodos_cruzan_el_anio_y_la_fecha_es_el_dia_configurado_o_el_ultimo_del_mes()
    {
        var p = Plantilla(mesInicio: 11);
        p.Periodo(0).Should().Be((2026, 11));
        p.Periodo(2).Should().Be((2027, 1));
        p.Fecha(2027, 2).Should().Be(new DateOnly(2027, 2, 28));
        Plantilla(dia: 5).Fecha(2027, 2).Should().Be(new DateOnly(2027, 2, 5));
        PlantillaRecurrente.FechaReversa(2026, 12).Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void La_plantilla_debe_cuadrar_y_cada_partida_lleva_un_solo_lado()
    {
        ((Action)(() => Plantilla(partidas: [P(100m, 0m), P(0m, 90m)]))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_NO_CUADRA");
        ((Action)(() => Plantilla(partidas: [P(100m, 100m), P(0m, 0m)]))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PARTIDA_UN_SOLO_LADO");
        ((Action)(() => Plantilla(periodos: 0))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_PERIODOS_INVALIDOS");
        ((Action)(() => Plantilla(dia: 29))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_DIA_INVALIDO");
    }

    [Fact]
    public void La_activa_otra_persona_y_activa_ya_no_se_edita()
    {
        var p = Plantilla();
        p.Invoking(x => x.Activar(Analista, "analista", Ahora)).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA");
        p.Activar(Supervisor, "supervisor", Ahora);
        p.Estado.Should().Be(EstadoPlantillaRecurrente.Activa);
        p.Invoking(x => x.Editar("otra", Tipo(), "x", 2026, 1, 1, 0, false, false, [P(1m, 0m), P(0m, 1m)], Analista))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_NO_EDITABLE");
        p.Suspender();
        p.Estado.Should().Be(EstadoPlantillaRecurrente.Suspendida);
        p.Reanudar();
        p.Estado.Should().Be(EstadoPlantillaRecurrente.Activa);
    }

    private static Poliza Autorizada(TipoDocumentoContable tipo)
    {
        var p = Poliza.CrearManual(Guid.NewGuid(), tipo, new DateOnly(2026, 10, 31), 2026, 10, "Renta de oficina (Octubre 2026)", null,
            [new DatosPartida(Guid.NewGuid(), "FIX-6.1", null, 15000m, 0m, "MXN", 1m, null, Sucursal, null, null, null, null, null, null, null),
             new DatosPartida(Guid.NewGuid(), "FIX-6.2", null, 0m, 15000m, "MXN", 1m, null, Sucursal, null, null, null, null, null, null, null)],
            Analista, "analista");
        p.MarcarRecurrente(Guid.NewGuid(), "recurrente:x:2026-10");
        p.Autorizar(7, Supervisor, "supervisor", Ahora);
        return p;
    }

    [Fact]
    public void La_reversa_lleva_las_mismas_partidas_en_negativo_sin_anular_la_original_K104b()
    {
        var tipo = Tipo();
        var original = Autorizada(tipo);
        var reversa = original.Revertir(Guid.NewGuid(), tipo, new DateOnly(2026, 11, 1), 2026, 11, 8, Supervisor, "supervisor", Ahora);

        reversa.Concepto.Should().Be("Reversa de AS-000007");
        reversa.Relacion.Should().Be(RelacionPoliza.Reversa);
        reversa.PolizaRelacionadaId.Should().Be(original.Id);
        reversa.PlantillaRecurrenteId.Should().Be(original.PlantillaRecurrenteId);
        reversa.LlaveIdempotencia.Should().Be($"reversa:{original.Id}");
        reversa.Estado.Should().Be(EstadoPoliza.Autorizada);
        reversa.Partidas.Select(x => (x.Cargo, x.Abono)).Should().Equal((-15000m, 0m), (0m, -15000m));
        original.Estado.Should().Be(EstadoPoliza.Autorizada);
        reversa.Invoking(x => x.Revertir(Guid.NewGuid(), tipo, new DateOnly(2026, 12, 1), 2026, 12, 9, Supervisor, "s", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_REVERSA_INVALIDA");
    }

    [Fact]
    public void La_reclasificacion_se_liga_solo_a_una_autorizada_K104a()
    {
        var tipo = Tipo();
        var original = Autorizada(tipo);
        var reclasificacion = Poliza.CrearManual(Guid.NewGuid(), tipo, new DateOnly(2026, 10, 31), 2026, 10, "Reclasificación de AS-000007", null,
            original.Partidas.Select(x => x.Datos()), Analista, "analista");
        reclasificacion.LigarReclasificacion(original);
        reclasificacion.Relacion.Should().Be(RelacionPoliza.Reclasificacion);
        reclasificacion.PolizaRelacionadaId.Should().Be(original.Id);

        var borrador = Poliza.CrearManual(Guid.NewGuid(), tipo, new DateOnly(2026, 10, 31), 2026, 10, "Borrador", null,
            original.Partidas.Select(x => x.Datos()), Analista, "analista");
        Poliza.CrearManual(Guid.NewGuid(), tipo, new DateOnly(2026, 10, 31), 2026, 10, "x", null, original.Partidas.Select(x => x.Datos()), Analista, "a")
            .Invoking(x => x.LigarReclasificacion(borrador))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_RECLASIFICA_NO_AUTORIZADA");
    }

    [Fact]
    public void Otro_usuario_no_edita_la_plantilla_ajena_ni_se_activa_sin_usuario()
    {
        var p = Plantilla();
        p.Invoking(x => x.Editar("reescrita", Tipo(), "x", 2026, 11, 120, 0, false, true, [P(90000m, 0m), P(0m, 90000m)], Supervisor))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_EDITA_SOLO_QUIEN_CREA");
        p.Invoking(x => x.Activar(null, "system", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA");

        p.Editar("FIX renta", Tipo(), "FIX renta mensual", 2026, 11, 12, 0, false, false, [P(16000m, 0m), P(0m, 16000m)], Analista);
        p.Activar(Supervisor, "supervisor", Ahora);
        p.Estado.Should().Be(EstadoPlantillaRecurrente.Activa);
    }
}
