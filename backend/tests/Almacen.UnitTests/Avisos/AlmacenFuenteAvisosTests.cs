using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Avisos;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Notificaciones;

namespace Millet.Almacen.UnitTests.Avisos;

public sealed class AlmacenFuenteAvisosTests
{
    private const string Revisar = AvisosInternosNotificacionService.PermisoRevisarActa;
    private static ContextoAvisos Contexto => new(P1Fixture.EmpresaId, P1Fixture.EmpresaId, null);

    [Fact]
    public async Task Actas_sin_limite_30_importan_lectura_y_leer_no_resuelve_revision()
    {
        await using var f = new P1Fixture();
        var permisos = new Acceso(Revisar);
        var fuente = Fuente(f, permisos);
        for (var i = 0; i < 35; i++) AgregarActa(f, 2020 + i / 12, i % 12 + 1);
        var previo = f.Db.AvisosAlmacen.Local.First();
        previo.MarcarLeido();
        await f.Db.SaveChangesAsync();

        var avisos = await fuente.ConsultarAsync(Contexto, default);
        avisos.Should().HaveCount(35).And.OnlyContain(a => a.Categoria == "Accion");
        var actasVisibles = await new ListarActasCierreHandler(f.Db, new Nombres()).Handle(new(), default);
        actasVisibles.Should().HaveCount(35).And.Contain(a => a.Id == previo.ReferenciaId);
        avisos.Single(a => a.DocumentoId == previo.ReferenciaId).LeidoOrigenAt.Should().Be(previo.LeidoAt);
        var nuevo = avisos.First(a => a.LeidoOrigenAt is null);
        await fuente.MarcarLeidoAsync(Contexto, nuevo, default);
        var leido = await f.Db.AvisosAlmacen.SingleAsync(a => a.ReferenciaId == nuevo.DocumentoId);
        var fecha = leido.LeidoAt;
        await fuente.MarcarLeidoAsync(Contexto, nuevo, default);
        leido.LeidoAt.Should().Be(fecha).And.NotBeNull();
        (await fuente.ConsultarAsync(Contexto, default)).Should().HaveCount(35);
        (await f.Db.ActasCierre.SingleAsync(a => a.Id == nuevo.DocumentoId)).MarcarRevisada(Contexto.UsuarioId);
        await f.Db.SaveChangesAsync();
        (await fuente.ConsultarAsync(Contexto, default)).Should().HaveCount(34);
    }

    [Fact]
    public async Task Revocacion_o_usuario_inactivo_retira_aviso_persistido()
    {
        await using var f = new P1Fixture();
        AgregarActa(f);
        await f.Db.SaveChangesAsync();
        var acceso = new Acceso(Revisar);
        var fuente = Fuente(f, acceso);
        (await fuente.ConsultarAsync(Contexto, default)).Should().ContainSingle();
        acceso.Activo = false;
        (await fuente.ConsultarAsync(Contexto, default)).Should().BeEmpty();
        acceso.Activo = true;
        acceso.Permisos.Clear();
        (await fuente.ConsultarAsync(Contexto, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Vale_vigente_tiene_clave_comun_y_desaparece_al_regularizar()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        AgregarAviso(f, AvisoAlmacen.TipoValeVencido, vale.Id);
        await f.Db.SaveChangesAsync();
        var alcance = new Documentos(new DocumentoSucursales(vale.Id, [Guid.NewGuid()]));
        var fuente = Fuente(f, new Acceso("almacen.salidas.por-vale", "almacen.salidas.leer-todas"), alcance);
        var aviso = (await fuente.ConsultarAsync(Contexto, default)).Should().ContainSingle().Subject;
        aviso.Categoria.Should().Be("Accion");
        aviso.ClaveOcurrencia.Should().Be(ClaveAviso.Crear(Contexto.EmpresaId, "almacen", "movimiento", vale.Id, "regularizar-vale", "1"));
        vale.RegularizarVale(Guid.NewGuid());
        await f.Db.SaveChangesAsync();
        (await fuente.ConsultarAsync(Contexto, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Lectura_no_ofrece_regularizar_y_guard_de_sucursal_no_se_omite()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        AgregarAviso(f, AvisoAlmacen.TipoValeVencido, vale.Id);
        await f.Db.SaveChangesAsync();
        var sucursal = Guid.NewGuid();
        var documentos = new Documentos(new DocumentoSucursales(vale.Id, [sucursal]));
        var acceso = new Acceso("almacen.salidas.leer-todas");
        var fuente = Fuente(f, acceso, documentos);
        (await fuente.ConsultarAsync(Contexto, default)).Should().ContainSingle().Which.Categoria.Should().Be("Informativo");
        (await fuente.ConsultarAsync(Contexto with { SucursalId = Guid.NewGuid() }, default)).Should().BeEmpty();
        var excluida = Fuente(f, acceso, documentos, (_, _, _) => throw new ForbiddenException("SIN_ALCANCE", "Otra sucursal"));
        (await excluida.ConsultarAsync(Contexto, default)).Should().BeEmpty();
        var sinSucursal = Fuente(f, acceso, new Documentos(new DocumentoSucursales(vale.Id, [])),
            (s, _, _) => s is null ? throw new ForbiddenException("SIN_SUCURSAL", "Sin sucursal verificable") : Task.CompletedTask);
        (await sinSucursal.ConsultarAsync(Contexto, default)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("almacen.avisos.vale-vencido")]
    [InlineData("almacen.salidas.leer-propias")]
    [InlineData("almacen.salidas.por-vale")]
    public async Task Permiso_sin_lectura_real_no_concede_destino_del_vale(string permiso)
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        AgregarAviso(f, AvisoAlmacen.TipoValeVencido, vale.Id);
        await f.Db.SaveChangesAsync();
        var fuente = Fuente(f, new Acceso(permiso), new Documentos(new DocumentoSucursales(vale.Id, [Guid.NewGuid()])));
        (await fuente.ConsultarAsync(Contexto, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ajuste_aprobado_es_informativo_y_exige_todas_las_sucursales_del_conteo()
    {
        await using var f = new P1Fixture();
        var conteo = new ConteoInventario(Guid.NewGuid(), Contexto.EmpresaId, TipoConteo.Rotativo, P1Fixture.Fecha, Contexto.UsuarioId);
        var linea = new LineaConteo(Guid.NewGuid(), conteo.Id, f.ArticuloId, f.SubId, f.BinId, 1, 10);
        conteo.AgregarLinea(linea);
        conteo.Iniciar(new(10, 100, 1000, 5000));
        linea.Capturar(2, Contexto.UsuarioId);
        conteo.EnviarAConciliacion();
        conteo.Aprobar(Guid.NewGuid());
        f.Db.Conteos.Add(conteo);
        AgregarAviso(f, AvisoAlmacen.TipoAjusteMayor, conteo.Id);
        await f.Db.SaveChangesAsync();
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid();
        var docs = new Documentos(new DocumentoSucursales(conteo.Id, [s1, s2]));
        var acceso = new Acceso(AvisosInternosNotificacionService.PermisoAjusteMayor, "almacen.inventarios.leer");
        var fuente = Fuente(f, acceso, docs);
        var aviso = (await fuente.ConsultarAsync(Contexto, default)).Should().ContainSingle().Subject;
        aviso.Categoria.Should().Be("Informativo");
        aviso.Destino.Should().Be($"/almacen/inventarios/{conteo.Id}");
        var excluida = Fuente(f, acceso, docs, (s, _, _) => s == s2
            ? throw new ForbiddenException("SIN_ALCANCE", "Otra sucursal") : Task.CompletedTask);
        (await excluida.ConsultarAsync(Contexto, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Contexto_falso_y_fallo_de_fuente_no_se_convierten_en_cero()
    {
        await using var f = new P1Fixture();
        var fuente = Fuente(f, new Acceso(Revisar));
        await Assert.ThrowsAsync<ForbiddenException>(() => fuente.ConsultarAsync(Contexto with { UsuarioId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => fuente.ConsultarAsync(Contexto with { EmpresaId = Guid.NewGuid() }, default));
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        AgregarAviso(f, AvisoAlmacen.TipoValeVencido, vale.Id);
        await f.Db.SaveChangesAsync();
        var falla = Fuente(f, new Acceso("almacen.salidas.por-vale", "almacen.salidas.leer-todas"), new Documentos(new DocumentoSucursales(vale.Id, [Guid.NewGuid()])),
            (_, _, _) => throw new InvalidOperationException("fuente caída"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => falla.ConsultarAsync(Contexto, default));
    }

    [Fact]
    public async Task Avisos_ajenos_no_se_filtran_y_no_pueden_marcarse_leidos()
    {
        await using var f = new P1Fixture();
        var acta = AgregarActa(f);
        var otraEmpresa = new AvisoAlmacen(Guid.NewGuid(), Contexto.UsuarioId, AvisoAlmacen.TipoActaPorRevisar, acta.Id, "Privado", "Privado", null);
        var otroUsuario = new AvisoAlmacen(Contexto.EmpresaId, Guid.NewGuid(), AvisoAlmacen.TipoActaPorRevisar, acta.Id, "Privado", "Privado", null);
        f.Db.AvisosAlmacen.AddRange(otraEmpresa, otroUsuario);
        await f.Db.SaveChangesAsync();
        var fuente = Fuente(f, new Acceso(Revisar));
        var avisos = await fuente.ConsultarAsync(Contexto, default);
        avisos.Should().ContainSingle().Which.Titulo.Should().Be("Aviso");
        await fuente.MarcarLeidoAsync(Contexto, avisos[0], default);
        otraEmpresa.LeidoAt.Should().BeNull(); otroUsuario.LeidoAt.Should().BeNull();
    }

    private static ActaCierreMes AgregarActa(P1Fixture f, int anio = 2026, int mes = 9)
    {
        var periodo = new PeriodoCerrado(Guid.NewGuid(), Contexto.EmpresaId, anio, mes, Contexto.UsuarioId);
        var acta = new ActaCierreMes(periodo, "{\"almacenes\":[],\"conteos\":[],\"vales\":[]}");
        f.Db.PeriodosCerrados.Add(periodo); f.Db.ActasCierre.Add(acta);
        AgregarAviso(f, AvisoAlmacen.TipoActaPorRevisar, acta.Id);
        return acta;
    }

    private static void AgregarAviso(P1Fixture f, string tipo, Guid id) => f.Db.AvisosAlmacen.Add(
        new AvisoAlmacen(Contexto.EmpresaId, Contexto.UsuarioId, tipo, id, "Aviso", "Motivo", "/ruta-no-confiable"));

    private static AlmacenFuenteAvisos Fuente(P1Fixture f, Acceso acceso, Documentos? documentos = null,
        Func<Guid?, string, CancellationToken, Task>? guard = null) => new(f.Db, f.Context, f.Context,
        acceso, acceso, documentos ?? new Documentos(), guard ?? ((_, _, _) => Task.CompletedTask), new Reloj());

    private sealed class Nombres : IUsuarioReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "Usuario"));
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Acceso(params string[] permisos) : ICurrentUserPermissions, IDestinatariosAvisoReadPort
    {
        public HashSet<string> Permisos { get; } = [.. permisos];
        public bool Activo { get; set; } = true;
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(Permisos.Contains(permiso));
        public Task<IReadOnlyList<Guid>> UsuariosConPermisoAsync(Guid empresaId, string permiso, Guid? sucursalId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Guid>>(Activo && empresaId == Contexto.EmpresaId && Permisos.Contains(permiso) ? [Contexto.UsuarioId] : []);
    }
    private sealed class Documentos(params DocumentoSucursales[] documentos) : IAlmacenSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DocumentoSucursales>>(documentos);
    }
}
