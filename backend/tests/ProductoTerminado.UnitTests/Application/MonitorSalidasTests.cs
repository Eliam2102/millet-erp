using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Consultas;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class MonitorSalidasTests
{
    // EntornoPt libera a las 12:00 UTC del 28-oct-2026.
    private static readonly DateTimeOffset Liberado = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
        public string? UserName => null;
    }

    private sealed class Permisos(params string[] otorgados) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(otorgados.Contains(permiso));
    }

    private sealed class Sucursales(params Guid[] ids) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(ids);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) => Task.FromResult(ids.Contains(sucursalId));
    }

    private static readonly Guid SucB = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private const string Ver = PermisosCanonicos.ProductoTerminadoPiezasVer;

    /// <summary>Pedido P1 completo (2 piezas) en 69; con <paramref name="liberar"/> pasa a 70 por saldo cubierto.</summary>
    private static async Task<EntornoPt> Preparar(bool liberar = true, bool tieneSalida = false)
    {
        var t = new EntornoPt();
        t.AgregarPedido("P1", 1, 2);
        await t.Escanear("E1", "P1", 1, "1");
        await t.Escanear("E2", "P1", 1, "2");
        if (liberar)
        {
            t.Saldos.Saldos["P1"] = 0m;
            await t.Liberacion.ReevaluarAsync(await t.Db.PedidosPt.SingleAsync(), default);
            await t.Db.SaveChangesAsync();
        }
        t.Pedidos.Monitor[t.Pedidos.Pedidos["P1"].PedidoFacturableId] = new PedidoMonitorOrigen("Cliente SA", "Obra Norte", tieneSalida);
        return t;
    }

    private static Task<SalidasPendientesPagina> Listar(EntornoPt t, TimeSpan edad, string[]? permisos = null, Guid[]? sucursales = null, Guid? sucursal = null, string? alerta = null) =>
        new ListarSalidasPendientesHandler(t.Db, new PtAcceso(new Usuario(), new Permisos(permisos ?? [Ver]), new Sucursales(sucursales ?? [EntornoPt.Sucursal])),
            t.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Liberado + edad))
            .Handle(new ListarSalidasPendientesQuery(sucursal, alerta, null, 1, 50), CancellationToken.None);

    [Theory]
    [InlineData(0, 59, 0, "ninguna")]
    [InlineData(23, 59, 23, "ninguna")]
    [InlineData(24, 0, 24, "24h")]
    [InlineData(47, 59, 47, "24h")]
    [InlineData(48, 0, 48, "48h")]
    [InlineData(100, 0, 100, "48h")]
    public async Task Alerta_por_antiguedad_desde_la_liberacion(int h, int m, int horas, string alerta)
    {
        using var t = await Preparar();
        var fila = (await Listar(t, new TimeSpan(h, m, 0))).Items.Single();
        (fila.HorasSinSalida, fila.Alerta, fila.NumeroPedido, fila.Cliente, fila.Destino, fila.Piezas, fila.LiberadoAt)
            .Should().Be((horas, alerta, "P1", "Cliente SA", "Obra Norte", 2, Liberado));
    }

    [Fact]
    public async Task Pedido_con_salida_no_se_lista()
    {
        using var t = await Preparar(tieneSalida: true);
        (await Listar(t, TimeSpan.FromHours(50))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Pedido_retenido_69_no_se_lista()
    {
        using var t = await Preparar(liberar: false);
        (await Listar(t, TimeSpan.FromHours(50))).Total.Should().Be(0);
    }

    [Fact]
    public async Task Filtro_de_alerta_y_contadores()
    {
        using var t = await Preparar();
        var r = await Listar(t, TimeSpan.FromHours(30), alerta: "48h");
        (r.Total, r.Con24h, r.Con48h).Should().Be((0, 1, 0));
        (await Listar(t, TimeSpan.FromHours(30), alerta: "24h")).Total.Should().Be(1);
    }

    [Fact]
    public async Task Otra_sucursal_prohibida_y_sin_permiso_rechazado()
    {
        using var t = await Preparar();
        await Assert.ThrowsAsync<ForbiddenException>(() => Listar(t, TimeSpan.FromHours(30), sucursal: SucB));
        (await Listar(t, TimeSpan.FromHours(30), sucursales: [SucB])).Total.Should().Be(0);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Listar(t, TimeSpan.FromHours(30), permisos: []));
        ex.Code.Should().Be("PT_PERMISO_REQUERIDO");
    }
}
