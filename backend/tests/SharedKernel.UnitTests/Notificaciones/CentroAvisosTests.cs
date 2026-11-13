using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Domain.Notificaciones;

namespace Millet.SharedKernel.UnitTests.Notificaciones;

public class CentroAvisosTests
{
    private static readonly ContextoAvisos Contexto = new(Guid.NewGuid(), Guid.NewGuid(), null);
    private static AvisoUsuario Aviso(int n, string categoria = "Accion") => new(
        ClaveAviso.Crear(Contexto.EmpresaId, "compras", "oc", Guid.Empty, "firmar", n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        "compras", "oc", Guid.Empty, $"OC-{n}", "Autorizar orden", "Requiere tu firma", DateTimeOffset.UnixEpoch.AddMinutes(n), categoria, "/compras/ordenes");

    [Fact]
    public async Task Novedades_y_totales_incluyen_las_paginas_no_visibles_y_no_depende_del_contador_neto()
    {
        var fuente = new Fuente("compras", Enumerable.Range(1, 60).Select(n => Aviso(n)).ToList());
        var centro = Crear([fuente]);
        var inicio = await centro.SincronizarAsync(Contexto, new(PageSize: 10), default);
        Assert.Equal(60, inicio.TotalSinLeerConocido);
        Assert.False(inicio.Novedades.HayNovedades);
        Assert.Equal(10, inicio.Items.Count);
        var pagina2 = await centro.SincronizarAsync(Contexto, new(Continuacion: inicio.SiguienteContinuacion, PageSize: 10), default);
        Assert.Empty(inicio.Items.Select(x => x.Aviso.ClaveOcurrencia).Intersect(pagina2.Items.Select(x => x.Aviso.ClaveOcurrencia)));
        fuente.Avisos.RemoveAt(59);
        fuente.Avisos.Add(Aviso(0)); // No está entre los diez más recientes.
        var siguiente = await centro.SincronizarAsync(Contexto, new(CursorDeteccion: inicio.Novedades.HastaSecSeguro, PageSize: 10), default);
        Assert.Equal(60, siguiente.TotalSinLeerConocido);
        Assert.Equal(1, siguiente.Novedades.CantidadConocida);
        Assert.DoesNotContain(siguiente.Items, x => x.Aviso.ClaveOcurrencia == Aviso(0).ClaveOcurrencia);
    }

    [Fact]
    public async Task Leer_no_resuelve_y_la_resolucion_procede_del_origen()
    {
        var fuente = new Fuente("compras", [Aviso(1)]);
        var centro = Crear([fuente]);
        await centro.SincronizarAsync(Contexto, new(), default);
        Assert.True(await centro.MarcarLeidoAsync(Contexto, Aviso(1).ClaveOcurrencia, default));
        var leido = await centro.SincronizarAsync(Contexto, new(), default);
        Assert.Equal(0, leido.TotalSinLeerConocido);
        Assert.Equal(1, leido.TotalAccionesConocidas);
        fuente.Avisos.Clear();
        Assert.Empty((await centro.SincronizarAsync(Contexto, new(), default)).Items);
        Assert.False(await centro.MarcarLeidoAsync(Contexto, Aviso(1).ClaveOcurrencia, default));
    }

    [Fact]
    public async Task Dos_origenes_de_una_obligacion_se_fusionan_e_importan_su_lectura()
    {
        var a = new Fuente("compras", [Aviso(1) with { LeidoOrigenAt = DateTimeOffset.UnixEpoch }]);
        var b = new Fuente("incidencias", [Aviso(1)]);
        var centro = Crear([a, b]);
        var r = await centro.SincronizarAsync(Contexto, new(), default);
        Assert.Single(r.Items);
        Assert.True(r.Items[0].Leido);
        Assert.True(await centro.MarcarLeidoAsync(Contexto, Aviso(1).ClaveOcurrencia, default));
        Assert.Equal(1, a.Lecturas);
        Assert.Equal(1, b.Lecturas);
    }

    [Fact]
    public async Task Fuente_fallida_no_se_trata_como_cero_ni_crea_recibos_de_datos_parciales()
    {
        var memoria = new Recibos();
        var centro = Crear([new Fuente("compras", [Aviso(1)]), new Fallida()], memoria);
        var r = await centro.SincronizarAsync(Contexto, new(), default);
        Assert.False(r.CoberturaCompleta);
        Assert.Equal("Failed", r.Fuentes.Single(x => x.Id == "fallida").Estado);
        Assert.Single(memoria.Datos);
    }

    [Fact]
    public async Task Cursor_de_otro_contexto_y_entradas_invalidas_se_rechazan()
    {
        var centro = Crear([new Fuente("compras", [Aviso(1), Aviso(2)])]);
        var r = await centro.SincronizarAsync(Contexto, new(PageSize: 1), default);
        await Assert.ThrowsAsync<BusinessRuleException>(() => centro.SincronizarAsync(Contexto with { UsuarioId = Guid.NewGuid() }, new(Continuacion: r.SiguienteContinuacion), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => centro.SincronizarAsync(Contexto, new(CursorDeteccion: "-1"), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => centro.SincronizarAsync(Contexto, new(PageSize: 0), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => centro.SincronizarAsync(Contexto, new(Continuacion: "eyJUaWNrcyI6MSwiQ2xhdmUiOm51bGx9"), default));
    }

    [Fact]
    public void Recibo_y_clave_respetan_episodio_y_lectura_inmutable()
    {
        Assert.NotEqual(Aviso(1).ClaveOcurrencia, Aviso(2).ClaveOcurrencia);
        var r = new AvisoLectura(Contexto.UsuarioId, Contexto.EmpresaId, Aviso(1).ClaveOcurrencia);
        r.MarcarLeido(DateTimeOffset.UnixEpoch);
        r.MarcarLeido(DateTimeOffset.UtcNow);
        Assert.Equal(DateTimeOffset.UnixEpoch, r.LeidoAt);
        Assert.Throws<ArgumentException>(() => new AvisoLectura(Guid.Empty, Contexto.EmpresaId, Aviso(1).ClaveOcurrencia));
    }

    private static CentroAvisos Crear(IEnumerable<IFuenteAvisosUsuario> fuentes, Recibos? recibos = null) =>
        new(fuentes, recibos ?? new(), new Reloj(), NullLogger<CentroAvisos>.Instance);

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
    private sealed class Fuente(string nombre, List<AvisoUsuario> avisos) : IFuenteAvisosUsuario
    {
        public string Nombre => nombre;
        public List<AvisoUsuario> Avisos => avisos;
        public int Lecturas { get; private set; }
        public Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct) => Task.FromResult<IReadOnlyList<AvisoUsuario>>(Avisos);
        public Task MarcarLeidoAsync(ContextoAvisos contexto, AvisoUsuario aviso, CancellationToken ct) { Lecturas++; return Task.CompletedTask; }
    }
    private sealed class Fallida : IFuenteAvisosUsuario
    {
        public string Nombre => "fallida";
        public Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct) => throw new InvalidOperationException("Fallo de prueba");
    }
    private sealed class Recibos : IRecibosAvisos
    {
        public Dictionary<string, ReciboAviso> Datos { get; } = [];
        public Task<T> ConBloqueoAsync<T>(ContextoAvisos contexto, Func<Task<T>> operacion, CancellationToken ct) => operacion();
        public Task<RecibosSincronizados> SincronizarAsync(ContextoAvisos contexto, IReadOnlyList<AvisoUsuario> avisos, CancellationToken ct)
        {
            foreach (var a in avisos) Datos.TryAdd(a.ClaveOcurrencia, new(a.ClaveOcurrencia, Datos.Count + 1, a.LeidoOrigenAt));
            return Task.FromResult(new RecibosSincronizados(Datos, Datos.Count));
        }
        public Task MarcarLeidoAsync(ContextoAvisos contexto, string clave, CancellationToken ct) { Datos[clave] = Datos[clave] with { LeidoAt = DateTimeOffset.UnixEpoch }; return Task.CompletedTask; }
    }
}
