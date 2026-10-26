using Millet.Compras.Application.Reportes.AnalisisProveedores;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.UnitTests.Oc.Domain;

namespace Millet.Compras.UnitTests.Reportes;

// F1-COM-13 (86e3jvu98): reglas de cumplimiento del análisis de proveedores.
public sealed class ReglasAnalisisProveedoresTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 22);

    private static (OrdenCompra Oc, LineaOrdenCompra Linea) OcAutorizada()
    {
        var oc = P2FirmasYSaldoTests.Crear();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
        return (oc, oc.Lineas.Single());
    }

    private static IncidenciaProveedorOc Incidencia(OrdenCompra oc, LineaOrdenCompra l, DateOnly fecha) =>
        oc.RegistrarIncidenciaProveedor(l.Id, TipoIncidenciaProveedorOc.Atraso, 1m, fecha, Guid.NewGuid(), null,
            new DateOnly(2026, 9, 1), Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static (DateOnly, decimal)[] Mov(params (int Dia, decimal Cantidad)[] m) =>
        m.Select(x => (new DateOnly(2026, 10, x.Dia), x.Cantidad)).ToArray();

    [Fact]
    public void Reprogramacion_de_incidencia_manda_y_recibir_antes_cuenta_a_tiempo()
    {
        var (oc, l) = OcAutorizada();
        var inc = Incidencia(oc, l, new DateOnly(2026, 10, 1));
        oc.ReprogramarIncidenciaProveedor(inc.Id, new DateOnly(2026, 10, 15), "Sin material",
            new DateOnly(2026, 9, 1), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var e = ReglasAnalisisProveedores.Evaluar(oc, l, Mov((10, 10m)), Hoy);

        Assert.Equal(new DateOnly(2026, 10, 15), e.Compromiso);
        Assert.Equal("incidencia", e.OrigenCompromiso);
        Assert.True(e.Evaluable);
        Assert.True(e.ATiempo);
    }

    [Theory]
    [InlineData(20, false)] // después del compromiso
    [InlineData(15, true)]  // el mismo día cuenta a tiempo
    public void Fecha_completada_contra_compromiso(int dia, bool esperado)
    {
        var (oc, l) = OcAutorizada();
        Incidencia(oc, l, new DateOnly(2026, 10, 15));
        var e = ReglasAnalisisProveedores.Evaluar(oc, l, Mov((5, 4m), (dia, 6m)), Hoy);
        Assert.Equal(new DateOnly(2026, 10, dia), e.FechaCompletada);
        Assert.Equal(esperado, e.ATiempo);
    }

    [Fact]
    public void Compromiso_vencido_sin_completar_cuenta_tarde_e_incompleta()
    {
        var (oc, l) = OcAutorizada();
        Incidencia(oc, l, new DateOnly(2026, 10, 15));
        var e = ReglasAnalisisProveedores.Evaluar(oc, l, Mov((5, 4m)), Hoy);
        Assert.True(e.Evaluable);
        Assert.False(e.ATiempo);
        Assert.True(e.Completable);
        Assert.False(e.Completa);
    }

    [Fact]
    public void Sin_compromiso_no_es_evaluable_y_el_porcentaje_es_nulo()
    {
        var (oc, l) = OcAutorizada();
        var e = ReglasAnalisisProveedores.Evaluar(oc, l, [], Hoy);
        Assert.Null(e.Compromiso);
        Assert.False(e.Evaluable);
        Assert.Null(e.ATiempo);
        Assert.False(e.Completable);
        Assert.Null(ReglasAnalisisProveedores.Porcentaje(0, 0));
        Assert.Equal(50.0m, ReglasAnalisisProveedores.Porcentaje(1, 2));
    }

    [Fact]
    public void Compromiso_futuro_sin_entrega_aun_no_se_evalua()
    {
        var (oc, l) = OcAutorizada();
        Incidencia(oc, l, new DateOnly(2026, 10, 30));
        var e = ReglasAnalisisProveedores.Evaluar(oc, l, Mov((5, 4m)), Hoy);
        Assert.False(e.Evaluable);
        Assert.False(e.Completable);
    }

    [Fact]
    public void Devolucion_despues_de_completar_deja_la_linea_incompleta()
    {
        var (oc, l) = OcAutorizada();
        Incidencia(oc, l, new DateOnly(2026, 10, 15));
        oc.RegistrarRecepcionLinea(l.Id, 10m, DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(l.Id, 8m, DateTimeOffset.UtcNow); // devolución 8.B de 2

        var e = ReglasAnalisisProveedores.Evaluar(oc, l, Mov((10, 10m), (12, -2m)), Hoy);

        Assert.True(e.ATiempo); // se completó el 10, antes del compromiso
        Assert.False(e.Completa);
    }

    [Fact]
    public void Completa_sin_fecha_de_almacen_no_cuenta_para_a_tiempo()
    {
        var (oc, l) = OcAutorizada();
        Incidencia(oc, l, new DateOnly(2026, 10, 15));
        oc.RegistrarRecepcionLinea(l.Id, 10m, DateTimeOffset.UtcNow);
        var e = ReglasAnalisisProveedores.Evaluar(oc, l, [], Hoy);
        Assert.False(e.Evaluable);
        Assert.Null(e.ATiempo);
        Assert.True(e.Completa);
    }

    [Fact]
    public void Montos_por_linea_usan_el_precio_neto()
    {
        var (oc, l) = OcAutorizada();
        oc.RegistrarRecepcionLinea(l.Id, 6m, DateTimeOffset.UtcNow);
        Assert.Equal(1000m, ReglasAnalisisProveedores.Ordenado(l));
        Assert.Equal(600m, ReglasAnalisisProveedores.RecibidoValorizado(l));
        Assert.Equal(1000m, ReglasAnalisisProveedores.PorFacturar(l));
    }
}
