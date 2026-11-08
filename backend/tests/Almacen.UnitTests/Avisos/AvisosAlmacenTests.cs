using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Domain.Avisos;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Avisos;

/// <summary>86e3tac2y: avisos dentro del ERP (vale vencido, ajuste mayor, acta por revisar) y acta del cierre mensual.</summary>
public class AvisosAlmacenTests
{
    private static readonly Guid Jefe = Guid.NewGuid();
    private static readonly Guid Finanzas = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly Guid Aprobador = Guid.NewGuid();

    private static (AvisosInternosNotificacionService Servicio, Correo Correo, Destinatarios Destinatarios) Armar(P1Fixture f)
    {
        var correo = new Correo();
        var destinatarios = new Destinatarios();
        var opciones = Options.Create(new AvisosAlmacenOptions { Finanzas = ["finanzas@millet.example"], JefeAlmacen = ["jefe@millet.example"] });
        var servicio = new AvisosInternosNotificacionService(f.Db, destinatarios, new Nombres(),
            new CorreoNotificacionService(correo, opciones, NullLogger<CorreoNotificacionService>.Instance));
        return (servicio, correo, destinatarios);
    }

    [Fact]
    public async Task Vale_vencido_avisa_al_jefe_de_la_sucursal_y_al_responsable_una_sola_vez()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var (servicio, correo, destinatarios) = Armar(f);
        var sucursal = await f.Db.Almacenes.Select(a => a.SucursalId).SingleAsync();

        await servicio.NotificarValeSinRegularizarAsync(vale.Id, vale.Folio!, null, DateTimeOffset.UtcNow, 1, default);
        await servicio.NotificarValeSinRegularizarAsync(vale.Id, vale.Folio!, null, DateTimeOffset.UtcNow.AddDays(1), 2, default);

        var avisos = await f.Db.AvisosAlmacen.AsNoTracking().ToListAsync();
        avisos.Select(a => a.UsuarioDestinoId).Should().BeEquivalentTo([Jefe, vale.RegistradoPor!.Value]);
        avisos.Should().OnlyContain(a => a.Tipo == AvisoAlmacen.TipoValeVencido && a.ReferenciaId == vale.Id && a.Mensaje.Contains(vale.Folio!));
        destinatarios.Consultas.Should().OnlyContain(c => c.Permiso == AvisosInternosNotificacionService.PermisoValeVencido && c.SucursalId == sucursal);
        correo.Enviados.Should().ContainSingle().Which.Para.Should().BeEquivalentTo(["jefe@millet.example"]);
    }

    [Fact]
    public async Task Vale_vencido_con_persona_destinataria_resuelve_correo_avisos_y_lo_incluye()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var correo = new Correo();
        var destinatarios = new Destinatarios();
        var receptorId = Guid.NewGuid();
        var portAvisos = new FakeAvisosPort(new Dictionary<Guid, string> { [receptorId] = "destinatario.vale@millet.com.mx" });
        var opciones = Options.Create(new AvisosAlmacenOptions { JefeAlmacen = ["jefe@millet.example"] });
        var correoService = new CorreoNotificacionService(correo, opciones, NullLogger<CorreoNotificacionService>.Instance, portAvisos);
        var servicio = new AvisosInternosNotificacionService(f.Db, destinatarios, new Nombres(), correoService);

        await servicio.NotificarValeSinRegularizarAsync(vale.Id, vale.Folio!, receptorId, DateTimeOffset.UtcNow, 1, default);

        correo.Enviados.Should().ContainSingle();
        correo.Enviados[0].Para.Should().BeEquivalentTo(["jefe@millet.example", "destinatario.vale@millet.com.mx"]);
    }

    [Fact]
    public async Task Ajuste_nivel_3_avisa_a_Finanzas_con_folio_y_monto_una_sola_vez()
    {
        await using var f = new P1Fixture();
        var (servicio, correo, _) = Armar(f);
        var conteoId = Guid.NewGuid();
        var aviso = new AvisoAjusteNivel3(P1Fixture.EmpresaId, conteoId, 12000m, Aprobador, DateTimeOffset.UtcNow, [new(Guid.NewGuid(), Guid.NewGuid())]);

        await servicio.NotificarAjusteNivel3AprobadoAsync(aviso, default);
        await servicio.NotificarAjusteNivel3AprobadoAsync(aviso, default);

        var unico = await f.Db.AvisosAlmacen.AsNoTracking().SingleAsync();
        unico.UsuarioDestinoId.Should().Be(Finanzas);
        unico.Tipo.Should().Be(AvisoAlmacen.TipoAjusteMayor);
        unico.Mensaje.Should().Contain(AvisosInternosNotificacionService.FolioConteo(conteoId)).And.Contain("12,000.00").And.Contain("Jefe Mérida");
        correo.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Sin_destinatarios_con_el_permiso_no_se_crea_aviso_ni_se_manda_correo()
    {
        await using var f = new P1Fixture();
        var (servicio, correo, destinatarios) = Armar(f);
        destinatarios.Vacio = true;

        await servicio.NotificarAjusteNivel3AprobadoAsync(
            new AvisoAjusteNivel3(P1Fixture.EmpresaId, Guid.NewGuid(), 12000m, Aprobador, DateTimeOffset.UtcNow, []), default);

        (await f.Db.AvisosAlmacen.CountAsync()).Should().Be(0);
        correo.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cierre_genera_el_acta_por_revisar_avisa_al_revisor_y_solo_se_revisa_una_vez()
    {
        await using var f = new P1Fixture();
        var (servicio, _, _) = Armar(f);

        var resp = await new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context, servicio).Handle(new(2026, 9), default);

        var acta = await f.Db.ActasCierre.AsNoTracking().SingleAsync();
        resp.ActaId.Should().Be(acta.Id);
        acta.Estado.Should().Be(ActaCierreMes.PorRevisar);
        (acta.Anio, acta.Mes, acta.EjecutadoPor).Should().Be((2026, 9, f.Context.UserId!.Value));
        acta.RevisadaPor.Should().BeNull();
        var aviso = await f.Db.AvisosAlmacen.AsNoTracking().SingleAsync();
        (aviso.UsuarioDestinoId, aviso.Tipo, aviso.ReferenciaId).Should().Be((Director, AvisoAlmacen.TipoActaPorRevisar, acta.Id));

        var revisar = new RevisarActaCierreHandler(f.Db, f.Context, new Nombres());
        var revisada = await revisar.Handle(new(acta.Id), default);
        revisada.Estado.Should().Be(ActaCierreMes.Revisada);
        revisada.RevisadaPorId.Should().Be(f.Context.UserId);
        revisada.RevisadaAt.Should().NotBeNull();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => revisar.Handle(new(acta.Id), default));
        ex.Code.Should().Be("ACTA_YA_REVISADA");

        var lista = await new ListarActasCierreHandler(f.Db, new Nombres()).Handle(new(), default);
        lista.Should().ContainSingle().Which.Estado.Should().Be(ActaCierreMes.Revisada);
    }

    private sealed class Correo : ICorreoSaliente
    {
        public List<MensajeCorreo> Enviados { get; } = [];
        public bool EsSimulado => true;
        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            Enviados.Add(mensaje);
            return Task.FromResult(ResultadoCorreoSaliente.Simulado);
        }
    }

    private sealed class Destinatarios : IDestinatariosAvisoReadPort
    {
        public List<(string Permiso, Guid? SucursalId)> Consultas { get; } = [];
        public bool Vacio { get; set; }
        public Task<IReadOnlyList<Guid>> UsuariosConPermisoAsync(Guid empresaId, string permiso, Guid? sucursalId, CancellationToken cancellationToken)
        {
            Consultas.Add((permiso, sucursalId));
            IReadOnlyList<Guid> ids = Vacio ? [] : permiso switch
            {
                AvisosInternosNotificacionService.PermisoValeVencido => [Jefe],
                AvisosInternosNotificacionService.PermisoAjusteMayor => [Finanzas],
                AvisosInternosNotificacionService.PermisoRevisarActa => [Director],
                _ => [],
            };
            return Task.FromResult(ids);
        }
    }

    private sealed class Nombres : IUsuarioReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(usuarioIds.ToDictionary(i => i, i => i == Aprobador ? "Jefe Mérida" : "Usuario"));
    }

    private sealed class FakeAvisosPort(Dictionary<Guid, string> mapa) : IUsuarioCorreoAvisosPort
    {
        public Task<string?> ObtenerCorreoAvisosAsync(Guid usuarioId, CancellationToken cancellationToken)
            => Task.FromResult(mapa.TryGetValue(usuarioId, out var c) ? c : null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerCorreosAvisosAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(mapa);
    }
}
