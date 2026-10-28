using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Consultas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class ConsultasTests
{
    private sealed class UsuarioFalso : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
        public string? UserName => null;
    }

    private sealed class PermisosFalsos(params string[] otorgados) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(otorgados.Contains(permiso));
    }

    private sealed class SucursalesFalsas(params Guid[] ids) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(ids);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken) => Task.FromResult(ids.Contains(sucursalId));
    }

    private static readonly Guid SucB = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private const string Ver = PermisosCanonicos.ProductoTerminadoPiezasVer;
    private const string VerTodas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
    private const string Gestionar = PermisosCanonicos.ProductoTerminadoExcepcionesGestionar;
    private const string GestionarTodas = PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales;

    private static PtAcceso Acceso(string[] permisos, params Guid[] sucursales) => new(new UsuarioFalso(), new PermisosFalsos(permisos), new SucursalesFalsas(sucursales));

    private static Task<PiezasPagina> Piezas(EntornoPt t, PtAcceso a, string? pedido = null, Guid? sucursal = null) =>
        new ListarPiezasHandler(t.Db, a).Handle(new ListarPiezasQuery(pedido, sucursal, 1, 50), CancellationToken.None);

    private static Task<EscaneosPagina> Escaneos(EntornoPt t, PtAcceso a, string? resultado = null) =>
        new ListarEscaneosHandler(t.Db, a).Handle(new ListarEscaneosQuery(resultado, 1, 50), CancellationToken.None);

    private static async Task Escanear(EntornoPt t, int n, string pedido = "P1")
    {
        for (var i = 1; i <= n; i++) await t.Escanear($"{pedido}-{i}", pedido, 1, i.ToString());
    }

    [Fact]
    public async Task Avance_8_de_10_es_parcial_y_reenviar_no_cambia_nada()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 10);
        await Escanear(t, 8);
        await Escanear(t, 8);
        var r = await Piezas(t, Acceso([Ver], EntornoPt.Sucursal), "P1");
        var av = r.Avance.Single();
        (av.Terminadas, av.Esperadas, av.Faltantes, av.AvanceFisico).Should().Be((8, 10, 2, "Parcial"));
        r.Total.Should().Be(8);
    }

    [Fact]
    public async Task Operativo_ve_solo_su_sucursal_y_otra_sucursal_es_prohibida()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3);
        await Escanear(t, 2);
        (await Piezas(t, Acceso([Ver], SucB))).Total.Should().Be(0);
        (await Piezas(t, Acceso([Ver], EntornoPt.Sucursal))).Total.Should().Be(2);
        await Assert.ThrowsAsync<ForbiddenException>(() => Piezas(t, Acceso([Ver], EntornoPt.Sucursal), sucursal: SucB));
        (await Piezas(t, Acceso([Ver, VerTodas]), sucursal: SucB)).Total.Should().Be(0);
        (await Piezas(t, Acceso([Ver, VerTodas]))).Total.Should().Be(2);
    }

    [Fact]
    public async Task Pieza_se_lista_por_sucursal_fisica_no_por_la_de_venta()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 3); // vendido desde Sucursal; la pieza se escanea en SucB
        t.Sucursales.Claves["02"] = SucB;
        await t.Escanear("E1", clave: "02");
        (await Piezas(t, Acceso([Ver], SucB))).Total.Should().Be(1);
        (await Piezas(t, Acceso([Ver], EntornoPt.Sucursal))).Total.Should().Be(0);
    }

    [Fact]
    public async Task Bandeja_filtra_por_resultado_y_oculta_resueltas_y_sin_sucursal()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        await t.Escanear("X1", pos: 9); // Excepcion
        await t.Escanear("X2", pedido: "NOEXISTE"); // Pospuesto
        await t.Escanear("X3", pos: 9, clave: "ZZ"); // Excepcion sin sucursal
        var oper = Acceso([Gestionar], EntornoPt.Sucursal);
        var corp = Acceso([Gestionar, GestionarTodas]);
        (await Escaneos(t, oper, "Excepcion")).Items.Should().ContainSingle(x => x.Etiqueta == "X1");
        (await Escaneos(t, oper)).Total.Should().Be(2);
        (await Escaneos(t, corp, "Excepcion")).Total.Should().Be(2);
        (await Escaneos(t, corp)).Total.Should().Be(3);

        var x1 = t.Db.Escaneos.Single(e => e.Etiqueta == "X1");
        x1.Resolver(Guid.NewGuid(), "ok", DateTimeOffset.UtcNow);
        await t.Db.SaveChangesAsync();
        (await Escaneos(t, corp)).Items.Should().NotContain(x => x.Etiqueta == "X1");
    }

    [Fact]
    public void Resultado_invalido_no_valida() =>
        new ListarEscaneosValidator().Validate(new ListarEscaneosQuery("Alta", 1, 50)).IsValid.Should().BeFalse();

    [Fact]
    public async Task Sin_permiso_lanza_pt_permiso_requerido()
    {
        using var t = new EntornoPt();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Piezas(t, Acceso([], EntornoPt.Sucursal)));
        ex.Code.Should().Be("PT_PERMISO_REQUERIDO");
        (await Assert.ThrowsAsync<ForbiddenException>(() => Escaneos(t, Acceso([])))).Code.Should().Be("PT_PERMISO_REQUERIDO");
    }
}
