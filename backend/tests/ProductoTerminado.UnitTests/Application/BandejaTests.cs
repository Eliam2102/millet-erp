using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class BandejaTests
{
    private sealed class UsuarioFalso(Guid? id) : ICurrentUserContext
    {
        public Guid? UserId { get; } = id;
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

    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
    private static readonly Guid OtraSucursal = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private const string Gestionar = PermisosCanonicos.ProductoTerminadoExcepcionesGestionar;
    private const string Todas = PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales;
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static PtAcceso Acceso(string[] permisos, params Guid[] sucursales) => new(new UsuarioFalso(Actor), new PermisosFalsos(permisos), new SucursalesFalsas(sucursales));

    private static EscaneoProcesador Proc(EntornoPt t) => new(t.Db, t.Pedidos, t.Sucursales, t.Eventos, new RelojFijo(Ahora));

    private static Task<EscaneoPt> Fila(EntornoPt t) => t.Db.Escaneos.SingleAsync();

    private static Task<EscaneoDto> Descartar(EntornoPt t, EscaneoPt e, PtAcceso a, int? version = null, string motivo = "duplicado en planta") =>
        new DescartarEscaneoHandler(t.Db, a, new RelojFijo(Ahora)).Handle(new DescartarEscaneoCommand(e.Id, version ?? e.Version, motivo), CancellationToken.None);

    private static Task<EscaneoDto> Revisar(EntornoPt t, EscaneoPt e, PtAcceso a) =>
        new MarcarReposicionRevisadaHandler(t.Db, a, new RelojFijo(Ahora)).Handle(new MarcarReposicionRevisadaCommand(e.Id, e.Version), CancellationToken.None);

    private static Task<EscaneoDto> Reintentar(EntornoPt t, EscaneoPt e, PtAcceso a) =>
        new ReintentarEscaneoHandler(t.Db, a, Proc(t)).Handle(new ReintentarEscaneoCommand(e.Id, e.Version), CancellationToken.None);

    private static async Task<EscaneoPt> Pospuesto(EntornoPt t)
    {
        await t.Escanear("E1", pedido: "P1");
        return await Fila(t);
    }

    [Fact]
    public async Task Descartar_con_motivo_resuelve_y_no_crea_pieza()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        var dto = await Descartar(t, e, Acceso([Gestionar], EntornoPt.Sucursal));
        dto.ResueltoAt.Should().NotBeNull();
        dto.MotivoResolucion.Should().Be("duplicado en planta");
        (await Fila(t)).ResueltoPor.Should().Be(Actor);
        (await t.Db.Piezas.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Motivo_vacio_o_en_blanco_no_valida(string motivo) =>
        new DescartarEscaneoValidator().Validate(new DescartarEscaneoCommand(Guid.NewGuid(), 0, motivo)).IsValid.Should().BeFalse();

    [Fact]
    public async Task Segundo_descarte_es_conflicto()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        var a = Acceso([Gestionar], EntornoPt.Sucursal);
        await Descartar(t, e, a);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Descartar(t, e, a));
        ex.Code.Should().Be("PT_ESCANEO_YA_RESUELTO");
    }

    [Fact]
    public async Task Version_vieja_lanza_concurrencia()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Descartar(t, e, Acceso([Gestionar], EntornoPt.Sucursal), e.Version + 1));
    }

    [Fact]
    public async Task Revisar_resuelve_reposicion_y_rechaza_excepcion()
    {
        using var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        await t.Escanear("E1");
        await t.Escanear("E1", at: new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Unspecified));
        var a = Acceso([Gestionar], EntornoPt.Sucursal);
        var repo = await t.Db.Escaneos.SingleAsync(x => x.Resultado == ResultadoEscaneo.Reposicion);
        (await Revisar(t, repo, a)).ResueltoAt.Should().NotBeNull();

        await t.Escanear("E2", pos: 9);
        var exc = await t.Db.Escaneos.SingleAsync(x => x.Resultado == ResultadoEscaneo.Excepcion);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Revisar(t, exc, a));
        ex.Code.Should().Be("PT_REVISION_NO_APLICA");
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Usuario_de_otra_sucursal_no_puede_actuar()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        await Assert.ThrowsAsync<ForbiddenException>(() => Descartar(t, e, Acceso([Gestionar], OtraSucursal)));
    }

    [Fact]
    public async Task Sin_sucursal_solo_actua_con_bypass()
    {
        using var t = new EntornoPt();
        await t.Escanear("E1", pedido: "P1", clave: "XX");
        var e = await Fila(t);
        e.SucursalId.Should().BeNull();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Descartar(t, e, Acceso([Gestionar])));
        ex.Code.Should().Be("SUCURSAL_NO_DETERMINADA");
        (await Descartar(t, e, Acceso([Gestionar, Todas]))).ResueltoAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reintento_con_el_pedido_presente_crea_una_pieza()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        t.AgregarPedido("P1", 1, 2);
        var dto = await Reintentar(t, e, Acceso([Gestionar], EntornoPt.Sucursal));
        dto.Resultado.Should().Be("Alta");
        (await t.Db.Piezas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reintento_que_sigue_fallando_conserva_la_fila_y_suma_un_intento()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        var antes = e.Intentos;
        var dto = await Reintentar(t, e, Acceso([Gestionar], EntornoPt.Sucursal));
        dto.Resultado.Should().Be("Pospuesto");
        dto.Intentos.Should().Be(antes + 1);
        (await t.Db.Piezas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sin_permiso_lanza_pt_permiso_requerido()
    {
        using var t = new EntornoPt();
        var e = await Pospuesto(t);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Descartar(t, e, Acceso([], EntornoPt.Sucursal)));
        ex.Code.Should().Be("PT_PERMISO_REQUERIDO");
    }
}
