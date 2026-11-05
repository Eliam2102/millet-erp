using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Consultas = Millet.ProductoTerminado.Application.Consultas;
using Millet.ProductoTerminado.Application.Traspasos;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class TraspasosTests
{
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

    private static readonly Guid Destino = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static PtAcceso Acceso(string[]? permisos = null, params Guid[] sucursales) =>
        new(new Usuario(), new Permisos(permisos ?? [PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, PermisosCanonicos.ProductoTerminadoPiezasVer]),
            new Sucursales(sucursales.Length == 0 ? [EntornoPt.Sucursal, Destino] : sucursales));

    private static async Task<EntornoPt> Preparar(int piezas = 2)
    {
        var t = new EntornoPt();
        t.AgregarPedido("P1", 1, piezas);
        for (var i = 1; i <= piezas; i++) await t.Escanear($"E{i}", "P1", 1, i.ToString());
        return t;
    }

    private static CrearTraspasoHandler Salida(EntornoPt t, PtAcceso? acceso = null) => new(t.Db, acceso ?? Acceso(), new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
    private static RecibirTraspasoHandler Recepcion(EntornoPt t, PtAcceso? acceso = null) => new(t.Db, acceso ?? Acceso(), new RelojFijo(Ahora.AddHours(5)));

    [Fact]
    public async Task Salida_pasa_las_piezas_a_transito_con_movimiento_y_referencia_del_traspaso()
    {
        using var t = await Preparar();
        var dto = await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"]), default);

        dto.Estado.Should().Be("EnTransito");
        dto.Total.Should().Be(2);
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnTransito && p.SucursalId == EntornoPt.Sucursal)).Should().BeTrue();
        var salidas = await t.Db.Movimientos.Where(m => m.Tipo == TipoMovimientoPieza.TrasladoSalida).ToListAsync();
        salidas.Should().HaveCount(2).And.OnlyContain(m => m.Referencia == dto.Id.ToString() && m.SucursalOrigenId == EntornoPt.Sucursal && m.SucursalDestinoId == Destino);
    }

    [Fact]
    public async Task Recepcion_total_deja_las_piezas_en_PT_de_destino()
    {
        using var t = await Preparar();
        var salida = await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"]), default);
        var dto = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);

        dto.Estado.Should().Be("Recibido");
        dto.Pendientes.Should().Be(0);
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT && p.SucursalId == Destino)).Should().BeTrue();
        (await t.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.TrasladoRecepcion)).Should().Be(2);
    }

    [Fact]
    public async Task Recepcion_con_una_pieza_de_menos_queda_ConDiferencia_y_la_pendiente_sigue_en_transito()
    {
        using var t = await Preparar();
        var salida = await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"]), default);
        var llega = (await t.Db.Piezas.SingleAsync(p => p.Etiqueta == "E1")).Id;

        var dto = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [llega]), default);

        dto.Estado.Should().Be("ConDiferencia");
        dto.Pendientes.Should().Be(1);
        (await t.Db.Piezas.SingleAsync(p => p.Etiqueta == "E2")).Estado.Should().Be(EstadoPieza.EnTransito);
        (await t.Db.Piezas.SingleAsync(p => p.Etiqueta == "E1")).SucursalId.Should().Be(Destino);

        // La que faltaba llega después: la diferencia se aclara.
        var final = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        final.Estado.Should().Be("Recibido");
    }

    [Fact]
    public async Task Piezas_en_transito_muestran_destino_y_no_cuentan_como_EnPt_en_el_origen()
    {
        using var t = await Preparar();
        await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);

        var r = await new Consultas.ListarPiezasHandler(t.Db, Acceso()).Handle(new Consultas.ListarPiezasQuery("P1", EntornoPt.Sucursal, 1, 50), default);

        var enTransito = r.Items.Single(p => p.Estado == "EnTransito");
        enTransito.Etiqueta.Should().Be("E1");
        enTransito.SucursalDestinoId.Should().Be(Destino);
        r.Items.Single(p => p.Etiqueta == "E2").SucursalDestinoId.Should().BeNull();
        r.Avance.Single().EnPt.Should().Be(1);
    }

    [Fact]
    public async Task Pieza_ya_recibida_no_se_recibe_dos_veces()
    {
        using var t = await Preparar(1);
        var salida = await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        var id = (await t.Db.Piezas.SingleAsync()).Id;

        var otra = async () => await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [id]), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_RECEPCION_INVALIDA");
    }

    [Fact]
    public async Task Pieza_en_transito_no_puede_salir_en_otro_traspaso()
    {
        using var t = await Preparar(1);
        await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        var otra = async () => await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_PIEZA_NO_EN_PT");
    }

    [Fact]
    public async Task Pieza_de_otra_sucursal_o_etiqueta_inexistente_no_sale()
    {
        using var t = await Preparar(1);
        var otraSucursal = async () => await Salida(t).Handle(new CrearTraspasoCommand(Destino, EntornoPt.Sucursal, ["E1"]), default);
        await otraSucursal.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_PIEZA_OTRA_SUCURSAL");
        var inexistente = async () => await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["NO-EXISTE"]), default);
        await inexistente.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_PIEZAS_INVALIDAS");
    }

    [Fact]
    public async Task Sin_permiso_o_fuera_de_alcance_no_opera()
    {
        using var t = await Preparar(1);
        var soloVer = async () => await Salida(t, Acceso([PermisosCanonicos.ProductoTerminadoPiezasVer])).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        await soloVer.Should().ThrowAsync<ForbiddenException>();

        var salida = await Salida(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        var origenRecibe = async () => await Recepcion(t, Acceso(null, EntornoPt.Sucursal)).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        await origenRecibe.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void Origen_igual_a_destino_o_sin_piezas_no_crea_traspaso()
    {
        var f = () => TraspasoPt.Crear(EntornoPt.Empresa, Destino, Destino, [Guid.NewGuid()], Ahora);
        f.Should().Throw<BusinessRuleException>();
        var g = () => TraspasoPt.Crear(EntornoPt.Empresa, EntornoPt.Sucursal, Destino, [], Ahora);
        g.Should().Throw<BusinessRuleException>();
    }
}
