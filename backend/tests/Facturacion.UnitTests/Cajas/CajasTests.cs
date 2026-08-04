using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Facturacion.Application.Cajas;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Cajas (CAJAS-PR1, 12-cajas.md): agregado (replace-sets idempotentes,
/// invariantes de nombre/ids), CRUD con concurrencia optimista y alcances
/// administrativos por usuario.
/// </summary>
public sealed class CajasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    // ---- Dominio: Caja ----

    [Fact]
    public void Caja_Crear_valida_nombre()
    {
        var vacio = () => Caja.Crear(Empresa, "  ", null);
        vacio.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CAJA_NOMBRE_INVALIDO");

        var largo = () => Caja.Crear(Empresa, new string('x', 101), null);
        largo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CAJA_NOMBRE_INVALIDO");

        var caja = Caja.Crear(Empresa, "  Caja Cancún  ", "   ");
        caja.Nombre.Should().Be("Caja Cancún");
        caja.Descripcion.Should().BeNull();
        caja.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Caja_ReemplazarSucursales_conserva_filas_que_sobreviven_y_es_idempotente()
    {
        var caja = Caja.Crear(Empresa, "Caja 1", null);
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var s3 = Guid.NewGuid();

        caja.ReemplazarSucursales([s1, s2, s2]); // duplicado en input → una fila
        caja.Sucursales.Should().HaveCount(2);
        var filaS1 = caja.Sucursales.Single(s => s.SucursalId == s1);

        caja.ReemplazarSucursales([s1, s3]); // s2 sale, s3 entra, s1 conserva su id
        caja.Sucursales.Should().HaveCount(2);
        caja.Sucursales.Select(s => s.SucursalId).Should().BeEquivalentTo([s1, s3]);
        caja.Sucursales.Single(s => s.SucursalId == s1).Id.Should().Be(filaS1.Id);

        caja.ReemplazarSucursales([]); // replace-set vacío = todas las sucursales (alcance abierto)
        caja.Sucursales.Should().BeEmpty();

        var invalido = () => caja.ReemplazarSucursales([Guid.Empty]);
        invalido.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CAJA_SUCURSAL_INVALIDA");
    }

    [Fact]
    public void Caja_ReemplazarCanales_y_Usuarios_validan_ids()
    {
        var caja = Caja.Crear(Empresa, "Caja 1", null);

        var canalInvalido = () => caja.ReemplazarCanales([0]);
        canalInvalido.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CAJA_CANAL_INVALIDO");

        caja.ReemplazarCanales([1, 4, 4]);
        caja.Canales.Select(c => c.CanalVentaId).Should().BeEquivalentTo([(short)1, (short)4]);

        var usuarioInvalido = () => caja.ReemplazarUsuarios([Guid.Empty]);
        usuarioInvalido.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CAJA_USUARIO_INVALIDO");
    }

    // ---- Dominio: UsuarioAlcance ----

    [Fact]
    public void UsuarioAlcance_rechaza_comodin_total_y_valida_ids()
    {
        var comodin = () => UsuarioAlcance.Crear(Empresa, Guid.NewGuid(), null, null);
        comodin.Should().Throw<BusinessRuleException>().Where(e => e.Code == "USUARIO_ALCANCE_COMODIN_TOTAL");

        var canal = () => UsuarioAlcance.Crear(Empresa, Guid.NewGuid(), null, 0);
        canal.Should().Throw<BusinessRuleException>().Where(e => e.Code == "USUARIO_ALCANCE_CANAL_INVALIDO");

        var ok = UsuarioAlcance.Crear(Empresa, Guid.NewGuid(), Guid.NewGuid(), null);
        ok.CanalVentaId.Should().BeNull();
    }

    // ---- Handlers: CRUD ----

    [Fact]
    public async Task CrearCaja_rechaza_nombre_duplicado()
    {
        await using var db = NewDb();
        var handler = new CrearCajaHandler(db, new FakeEmpresaContext(Empresa));

        await handler.Handle(new CrearCajaCommand("Caja Mostrador", null), CancellationToken.None);

        var duplicado = () => handler.Handle(new CrearCajaCommand("Caja Mostrador", null), CancellationToken.None);
        await duplicado.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CAJA_NOMBRE_DUPLICADO");
    }

    [Fact]
    public async Task ActualizarCaja_choca_con_version_desactualizada()
    {
        await using var db = NewDb();
        var creada = await new CrearCajaHandler(db, new FakeEmpresaContext(Empresa))
            .Handle(new CrearCajaCommand("Caja 1", null), CancellationToken.None);

        var handler = new ActualizarCajaHandler(db);
        var viejo = () => handler.Handle(
            new ActualizarCajaCommand(creada.Id, creada.Version - 1, "Caja 1B", null, Activa: true),
            CancellationToken.None);
        await viejo.Should().ThrowAsync<ConcurrencyException>();

        var ok = await handler.Handle(
            new ActualizarCajaCommand(creada.Id, creada.Version, "Caja 1B", "mostrador", Activa: false),
            CancellationToken.None);

        var caja = await db.Cajas.AsNoTracking().SingleAsync(c => c.Id == creada.Id);
        caja.Nombre.Should().Be("Caja 1B");
        caja.Estatus.Should().Be(EstatusCatalogo.Inactivo);
        // El incremento de Version lo hace el interceptor real de SaveChanges
        // (no corre bajo InMemory); aquí solo se asserta que la respuesta
        // refleja la versión persistida.
        ok.Version.Should().Be(caja.Version);
    }

    [Fact]
    public async Task ReemplazarSucursales_valida_contra_el_catalogo()
    {
        await using var db = NewDb();
        var creada = await new CrearCajaHandler(db, new FakeEmpresaContext(Empresa))
            .Handle(new CrearCajaCommand("Caja 1", null), CancellationToken.None);

        var inactiva = Guid.NewGuid();
        var activa = Guid.NewGuid();
        var handler = new ReemplazarSucursalesCajaHandler(db, new FakeSucursalesReadPort(noActivas: [inactiva]));

        var invalida = () => handler.Handle(
            new ReemplazarSucursalesCajaCommand(creada.Id, creada.Version, [activa, inactiva]),
            CancellationToken.None);
        await invalida.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CAJA_SUCURSAL_NO_ACTIVA");

        await handler.Handle(
            new ReemplazarSucursalesCajaCommand(creada.Id, creada.Version, [activa]),
            CancellationToken.None);
        (await db.Cajas.AsNoTracking().Include(c => c.Sucursales).SingleAsync(c => c.Id == creada.Id))
            .Sucursales.Select(s => s.SucursalId).Should().BeEquivalentTo([activa]);
    }

    [Fact]
    public async Task ReemplazarCanales_valida_activos_en_el_catalogo()
    {
        await using var db = NewDb();
        var creada = await new CrearCajaHandler(db, new FakeEmpresaContext(Empresa))
            .Handle(new CrearCajaCommand("Caja 1", null), CancellationToken.None);

        var handler = new ReemplazarCanalesCajaHandler(db, new FakeCanalesVentaReadPort(todoActivo: false));
        var invalido = () => handler.Handle(
            new ReemplazarCanalesCajaCommand(creada.Id, creada.Version, [1]), CancellationToken.None);
        await invalido.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CAJA_CANAL_NO_ACTIVO");
    }

    // ---- Handlers: alcances administrativos ----

    [Fact]
    public async Task ReemplazarUsuarioAlcances_hace_replace_set_completo()
    {
        await using var db = NewDb();
        var usuario = Guid.NewGuid();
        var sucursal = Guid.NewGuid();
        var handler = new ReemplazarUsuarioAlcancesHandler(
            db, new FakeEmpresaContext(Empresa), new FakeSucursalesReadPort(), new FakeCanalesVentaReadPort());

        await handler.Handle(new ReemplazarUsuarioAlcancesCommand(usuario, [
            new UsuarioAlcanceInput(sucursal, null),
            new UsuarioAlcanceInput(null, 4),
            new UsuarioAlcanceInput(null, 4), // duplicado → una fila
        ]), CancellationToken.None);

        (await db.UsuariosAlcance.CountAsync()).Should().Be(2);

        var respuesta = await handler.Handle(
            new ReemplazarUsuarioAlcancesCommand(usuario, [new UsuarioAlcanceInput(sucursal, 4)]),
            CancellationToken.None);

        respuesta.Total.Should().Be(1);
        var fila = await db.UsuariosAlcance.AsNoTracking().SingleAsync();
        fila.SucursalId.Should().Be(sucursal);
        fila.CanalVentaId.Should().Be(4);
    }

    // ---- Queries ----

    [Fact]
    public async Task ListarCajas_y_Detalle_devuelven_alcances_y_version()
    {
        await using var db = NewDb();
        var empresaCtx = new FakeEmpresaContext(Empresa);
        var creada = await new CrearCajaHandler(db, empresaCtx)
            .Handle(new CrearCajaCommand("Caja Cancún", "mostrador"), CancellationToken.None);

        await new ReemplazarCanalesCajaHandler(db, new FakeCanalesVentaReadPort())
            .Handle(new ReemplazarCanalesCajaCommand(creada.Id, creada.Version, [1, 4]), CancellationToken.None);

        // CAJAS-PR6: listar exige administrar ∨ operar ∨ supervisar (OR en handler).
        var listado = await new ListarCajasHandler(db, new FakeCurrentUserPermissions("facturacion.caja.operar"))
            .Handle(new ListarCajasQuery(), CancellationToken.None);
        listado.Should().ContainSingle(c => c.Nombre == "Caja Cancún" && c.Canales == 2);

        var sinPermiso = () => new ListarCajasHandler(db, new FakeCurrentUserPermissions())
            .Handle(new ListarCajasQuery(), CancellationToken.None);
        await sinPermiso.Should().ThrowAsync<ForbiddenException>();

        var detalle = await new CajaDetalleHandler(db, new FakeCanalesVentaReadPort(
                nombres: new Dictionary<short, string> { [1] = "Tienda Cancún", [4] = "CC MID" }))
            .Handle(new CajaDetalleQuery(creada.Id), CancellationToken.None);

        detalle.Canales.Select(c => c.Nombre).Should().BeEquivalentTo(["Tienda Cancún", "CC MID"]);
        detalle.Version.Should().Be(
            (await db.Cajas.AsNoTracking().SingleAsync(c => c.Id == creada.Id)).Version);

        var inexistente = () => new CajaDetalleHandler(db, new FakeCanalesVentaReadPort())
            .Handle(new CajaDetalleQuery(Guid.NewGuid()), CancellationToken.None);
        await inexistente.Should().ThrowAsync<EntityNotFoundException>();
    }
}
