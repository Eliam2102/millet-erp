using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Departamentos;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.CentrosCosto.Infrastructure.PublicAdapters;
using Millet.Compras.Domain;
using Millet.Compras.Application;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
namespace Millet.Compras.UnitTests.Application;

public sealed class ADM08CentroCostoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Departamento = Guid.NewGuid();
    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Alcance(AlcanceDim3 valor) : IAlcanceDim3Evaluator
    {
        public Task<AlcanceDim3> ResolverAsync(CancellationToken cancellationToken) => Task.FromResult(valor);
    }
    private static CentrosCostoDbContext Contexto() => new(
        new DbContextOptionsBuilder<CentrosCostoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());
    private sealed record Datos(Guid Planta, Guid Area, Guid Maquina, Guid OtraArea, Guid OtraMaquina);
    private static async Task<Datos> SembrarAsync(CentrosCostoDbContext db, bool equivalencia = true)
    {
        var datos = new Datos(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var g2 = Guid.NewGuid(); var g3 = Guid.NewGuid();
        db.Dim1s.Add(new Dim1(datos.Planta, "101", "DEMO planta"));
        db.Dim2s.AddRange(new Dim2(datos.Area, datos.Planta, "A", "DEMO departamento", g2), new Dim2(datos.OtraArea, datos.Planta, "B", "DEMO otra área", g2));
        db.Dim3s.AddRange(new Dim3(datos.Maquina, datos.Area, "MA", "DEMO máquina", g3), new Dim3(datos.OtraMaquina, datos.OtraArea, "MB", "DEMO otra máquina", g3));
        if (equivalencia) db.DepartamentoCentrosCosto.Add(new DepartamentoCentroCosto(Guid.NewGuid(), Empresa, Sucursal, Departamento, datos.Area, "DEMO · por validar con Laura (V49)"));
        await db.SaveChangesAsync(); return datos;
    }
    private static CentroCostoCapturaAdapter Captura(CentrosCostoDbContext db, AlcanceDim3 a) =>
        new(db, new Alcance(a), new Dim3ElegibilidadAdapter(db, new Alcance(a)));

    [Fact]
    public async Task Sin_maquina_sin_alcance_hereda_departamento_solo_lectura()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db);
        var captura = Captura(db, AlcanceDim3.Ninguno());
        var contexto = await captura.ObtenerAsync(Sucursal, Departamento, default);
        Assert.False(contexto.PuedeElegir); Assert.Equal(2, contexto.Heredado!.Nivel);
        Assert.Equal(d.Area, await captura.ResolverAsync(Sucursal, Departamento, null, default));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => captura.ResolverAsync(Sucursal, Departamento, d.Maquina, default));
        Assert.Equal("CECO_SOLO_LECTURA", ex.Code);
    }
    [Fact]
    public async Task Una_maquina_en_alcance_se_prellena_si_no_hay_equivalencia()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db, false);
        var contexto = await Captura(db, AlcanceDim3.De(new HashSet<Guid> { d.Maquina })).ObtenerAsync(Sucursal, Departamento, default);
        Assert.Null(contexto.Heredado); Assert.True(contexto.PuedeElegir);
        Assert.Equal(d.Maquina, contexto.UnicaOpcion!.Id);
    }
    [Fact]
    public async Task Con_alcance_elige_ancestros_y_maquina_sin_habilitar_hermanas()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db);
        var captura = Captura(db, AlcanceDim3.De(new HashSet<Guid> { d.Maquina }));
        Assert.True((await captura.ObtenerAsync(Sucursal, Departamento, default)).PuedeElegir);
        var ids = (await captura.BuscarAsync(null, false, default)).Select(x => x.Id).ToArray();
        Assert.Contains(d.Area, ids); Assert.Contains(d.Planta, ids); Assert.Contains(d.Maquina, ids);
        Assert.DoesNotContain(d.OtraArea, ids); Assert.DoesNotContain(d.OtraMaquina, ids);
        Assert.Equal(d.Maquina, await captura.ResolverAsync(Sucursal, Departamento, d.Maquina, default));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => captura.ResolverAsync(Sucursal, Departamento, d.OtraMaquina, default));
        Assert.Equal("CECO_INVALIDO", ex.Code);
    }
    [Fact]
    public async Task Lineas_distintas_admiten_centros_distintos_con_alcance_total()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db); var captura = Captura(db, AlcanceDim3.Total());
        Assert.Equal(d.Area, await captura.ResolverAsync(Sucursal, Departamento, null, default));
        Assert.Equal(d.OtraArea, await captura.ResolverAsync(Sucursal, Departamento, d.OtraArea, default));
        Assert.Equal(d.OtraMaquina, await captura.ResolverAsync(Sucursal, Departamento, d.OtraMaquina, default));
    }
    [Fact]
    public async Task Sin_equivalencia_mensaje_claro_y_con_alcance_puede_elegir()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db, false);
        foreach (var a in new[] { AlcanceDim3.Ninguno(), AlcanceDim3.Total() })
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Captura(db, a).ResolverAsync(Sucursal, Departamento, null, default));
            Assert.Equal(CentroCostoCapturaRegla.SinEquivalencia, ex.Message);
        }
        Assert.Equal(d.Area, await Captura(db, AlcanceDim3.Total()).ResolverAsync(Sucursal, Departamento, d.Area, default));
    }
    [Fact]
    public async Task Centro_inactivo_y_ancestro_inactivo_rechazados()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db);
        (await db.Dim1s.SingleAsync()).CambiarEstatus(EstatusCatalogo.Inactivo); await db.SaveChangesAsync();
        var port = new Dim3ElegibilidadAdapter(db, new Alcance(AlcanceDim3.Total()));
        foreach (var id in new[] { d.Planta, d.Area, d.Maquina })
            Assert.Equal(Dim3Elegibilidad.Inactiva, await port.EvaluarAsync(id, false, default));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Captura(db, AlcanceDim3.Ninguno()).ResolverAsync(Sucursal, Departamento, null, default));
        Assert.Equal("CECO_INVALIDO", ex.Code);
    }
    [Fact]
    public async Task Oc_manual_y_lectura_historica_admiten_departamento_sin_maquina()
    {
        await using var db = Contexto(); var d = await SembrarAsync(db);
        var contable = await new ContabilidadCentroCostoAdapter(db).ObtenerAsync([d.Area], default);
        Assert.Equal(Millet.Contabilidad.Domain.DimensionContable.Dim2, contable[d.Area].Nivel);
        var captura = Captura(db, AlcanceDim3.Ninguno());
        Assert.Contains(await captura.BuscarAsync(null, true, default), x => x.Id == d.Area && x.Nivel == 2);
        Assert.Equal(Dim3Elegibilidad.Valida, await new Dim3ElegibilidadAdapter(db, new Alcance(AlcanceDim3.Ninguno())).EvaluarAsync(d.Area, false, default));
        (await db.Dim2s.SingleAsync(x => x.Id == d.Area)).CambiarEstatus(EstatusCatalogo.Inactivo); await db.SaveChangesAsync();
        var lectura = await new Dim3ReadAdapter(db).ObtenerAsync([d.Area, d.Maquina, d.Planta], default);
        Assert.Equal(3, lectura.Count); Assert.False(lectura[d.Area].Activa); Assert.Equal("A", lectura[d.Area].Clave);
    }
    [Fact]
    public async Task Equivalencia_de_otra_sucursal_no_se_hereda()
    {
        await using var db = Contexto(); await SembrarAsync(db);
        Assert.Null((await Captura(db, AlcanceDim3.Ninguno()).ObtenerAsync(Guid.NewGuid(), Departamento, default)).Heredado);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Usa_departamento_del_requisitante_y_no_del_capturista_ni_cabecera(bool sinDepartamento)
    {
        await using var org = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());
        var solicitante = Guid.NewGuid(); var capturista = Guid.NewGuid();
        org.Empleados.AddRange(new Empleado(Guid.NewGuid(), Empresa, "ADM08-SOL", "DEMO solicitante",
            departamentoId: sinDepartamento ? null : Departamento, usuarioId: solicitante),
            new Empleado(Guid.NewGuid(), Empresa, "ADM08-CAP", "DEMO capturista", departamentoId: Guid.NewGuid(), usuarioId: capturista));
        await org.SaveChangesAsync();
        var rq = new Requisicion(Guid.NewGuid(), Empresa, Folio.Parse("MID2026-000002"), 2026,
            Clasificacion.Servicio, Sucursal, Guid.NewGuid(), null, solicitante, capturista, Prioridad.Normal, DateTimeOffset.UtcNow);
        Assert.Equal(sinDepartamento ? Guid.Empty : Departamento, await CentroCostoRqResolver.DepartamentoAsync(rq, org, default));
    }
    [Fact]
    public async Task Empleado_de_otra_empresa_no_aporta_departamento_a_la_rq()
    {
        await using var org = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());
        var solicitante = Guid.NewGuid();
        org.Empleados.Add(new Empleado(Guid.NewGuid(), Guid.NewGuid(), "ADM08-OTRA", "DEMO otra empresa",
            departamentoId: Guid.NewGuid(), usuarioId: solicitante));
        await org.SaveChangesAsync();
        var rq = new Requisicion(Guid.NewGuid(), Empresa, Folio.Parse("MID2026-000003"), 2026,
            Clasificacion.Servicio, Sucursal, Departamento, null, solicitante, Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow);
        Assert.Equal(Departamento, await CentroCostoRqResolver.DepartamentoAsync(rq, org, default));
    }
    [Fact]
    public void Transmitir_congela_centro_del_departamento_en_linea()
    {
        var rq = new Requisicion(Guid.NewGuid(), Empresa, Folio.Parse("MID2026-000001"), 2026,
            Clasificacion.Servicio, Sucursal, Departamento, null, Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow);
        var cc = Guid.NewGuid(); var articulo = Guid.NewGuid(); var precio = Money.Of(10, "MXN");
        var linea = rq.AgregarLinea(Guid.NewGuid(), articulo, 1, "PZA", precio, centroCostoId: cc);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        var ex = Assert.Throws<BusinessRuleException>(() => rq.ActualizarLineaEstructural(linea.Id, articulo, 1, "PZA", precio, null, Guid.NewGuid(), null, null));
        Assert.Equal("LINEAS_SOLO_EN_BORRADOR", ex.Code); Assert.Equal(cc, linea.CentroCostoId);
    }
}
