using Millet.Almacen.Domain.Conteos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Conteos;

/// <summary>
/// Tests del agregado <see cref="ConteoInventario"/> (F7-PR1). Cubre
/// invariantes del ctor + transiciones del flujo + captura sin sesgo.
/// </summary>
public class ConteoInventarioTests
{
    [Fact]
    public void Crear_inicializa_planificado()
    {
        var c = Nuevo();
        c.Estado.Should().Be(EstadoConteo.Planificado);
        c.Lineas.Should().BeEmpty();
        c.SnapshotCapturadoAt.Should().BeNull();
    }

    [Fact]
    public void Iniciar_sin_lineas_falla()
    {
        var c = Nuevo();
        var act = () => c.Iniciar();
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTEO_SIN_LINEAS");
    }

    [Fact]
    public void Iniciar_con_lineas_pasa_a_en_curso_con_snapshot()
    {
        var c = Nuevo();
        AgregarLinea(c, cantidadTeorica: 100m);
        c.Iniciar();
        c.Estado.Should().Be(EstadoConteo.EnCurso);
        c.SnapshotCapturadoAt.Should().NotBeNull();
        c.FechaInicio.Should().NotBeNull();
    }

    [Fact]
    public void Agregar_linea_despues_de_iniciar_falla()
    {
        var c = Nuevo();
        AgregarLinea(c);
        c.Iniciar();
        var act = () => AgregarLinea(c);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTEO_NO_PLANIFICADO");
    }

    [Fact]
    public void Capturar_linea_actualiza_cantidad_real()
    {
        var c = Nuevo();
        AgregarLinea(c, cantidadTeorica: 100m);
        c.Iniciar();
        var linea = c.Lineas.Single();
        linea.Capturar(95m, Guid.NewGuid());

        linea.CantidadRealCapturada.Should().Be(95m);
        linea.CapturadoPor.Should().NotBeNull();
        linea.CapturadoAt.Should().NotBeNull();
    }

    [Fact]
    public void Enviar_a_conciliacion_con_lineas_sin_capturar_falla()
    {
        var c = Nuevo();
        AgregarLinea(c);
        c.Iniciar();
        // Sin capturar.
        var act = () => c.EnviarAConciliacion();
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTEO_CAPTURA_INCOMPLETA");
    }

    [Fact]
    public void Enviar_a_conciliacion_con_todas_capturadas_pasa()
    {
        var c = Nuevo();
        AgregarLinea(c);
        c.Iniciar();
        c.Lineas.Single().Capturar(100m, Guid.NewGuid());
        c.EnviarAConciliacion();
        c.Estado.Should().Be(EstadoConteo.EnConciliacion);
    }

    [Fact]
    public void Aprobar_desde_en_conciliacion_pasa_a_aprobado()
    {
        var c = ConteoEnConciliacion();
        c.Aprobar(Guid.NewGuid());
        c.Estado.Should().Be(EstadoConteo.Aprobado);
        c.AprobadorId.Should().NotBeNull();
        c.FechaAprobacion.Should().NotBeNull();
    }

    [Fact]
    public void MarcarAplicado_desde_aprobado_pasa_a_aplicado()
    {
        var c = ConteoEnConciliacion();
        c.Aprobar(Guid.NewGuid());
        c.MarcarAplicado();
        c.Estado.Should().Be(EstadoConteo.Aplicado);
        c.FechaCierre.Should().NotBeNull();
    }

    [Fact]
    public void MarcarAplicado_desde_otros_estados_falla()
    {
        var c = ConteoEnConciliacion();
        var act = () => c.MarcarAplicado();
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("CONTEO_NO_APROBADO");
    }

    [Fact]
    public void Linea_conteo_con_cantidad_negativa_falla()
    {
        var act = () => new LineaConteo(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), cantidadTeorica: -1m, costoPromedioSnapshot: 100m);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("LINEA_CONTEO_CANT_NEGATIVA");
    }

    [Fact]
    public void RecuentoConteo_con_secuencia_cero_falla()
    {
        var act = () => new RecuentoConteo(
            Guid.NewGuid(), Guid.NewGuid(), 0, 50m, Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("RECUENTO_SECUENCIA_INVALIDA");
    }

    // ─── Helpers ───
    private static ConteoInventario Nuevo() =>
        new(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            tipo: TipoConteo.Rotativo,
            fechaPlanificada: new DateOnly(2026, 5, 25),
            responsableId: Guid.NewGuid(),
            subAlmacenId: Guid.NewGuid());

    private static void AgregarLinea(
        ConteoInventario c, decimal cantidadTeorica = 100m, decimal costoSnap = 50m)
    {
        c.AgregarLinea(new LineaConteo(
            id: Guid.NewGuid(),
            conteoId: c.Id,
            articuloId: Guid.NewGuid(),
            subAlmacenId: c.SubAlmacenId ?? Guid.NewGuid(),
            ubicacionId: Guid.NewGuid(),
            cantidadTeorica: cantidadTeorica,
            costoPromedioSnapshot: costoSnap));
    }

    private static ConteoInventario ConteoEnConciliacion()
    {
        var c = Nuevo();
        AgregarLinea(c);
        c.Iniciar();
        c.Lineas.Single().Capturar(100m, Guid.NewGuid());
        c.EnviarAConciliacion();
        return c;
    }
}
