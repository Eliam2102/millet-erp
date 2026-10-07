using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// <c>ICuentaContableReadPort</c> inyectado desde el contenedor real (§7, §12): activa/afectable aceptada;
/// título, inactiva, inexistente, de otra empresa, pendiente de validación y control manual rechazadas.
/// </summary>
public class PuertoLecturaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CrearCuentaCommand Nueva(string codigo, TipoCuenta? tipo = TipoCuenta.Afectable, NaturalezaCuenta? nat = NaturalezaCuenta.Deudora,
        CuentaControl control = CuentaControl.Ninguna, Guid? padre = null) => new(codigo, "FIX cuenta", padre, nat, tipo, control, null, null);

    [Fact]
    public async Task Matriz_de_validacion_para_movimiento()
    {
        var suf = Sufijo();
        try
        {
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var puerto = scope.ServiceProvider.GetRequiredService<ICuentaContableReadPort>();

            // P19: la raíz (nivel 1) acumula; sus hijas sin hijas son afectables (el tipo enviado se ignora).
            var titulo = await m.Send(Nueva(Codigo(suf, "1"), TipoCuenta.Afectable));
            Assert.Equal(TipoCuenta.Titulo, titulo.Tipo);
            var raiz = titulo.Id;
            var ok = await m.Send(Nueva(Codigo(suf, "1.1"), TipoCuenta.Titulo, padre: raiz));
            Assert.Equal(TipoCuenta.Afectable, ok.Tipo);
            var inactiva = await m.Send(Nueva(Codigo(suf, "1.3"), padre: raiz));
            await m.Send(new DesactivarCuentaCommand(inactiva.Id, inactiva.Version));
            var sinTipo = await m.Send(Nueva(Codigo(suf, "1.4"), tipo: null, padre: raiz));
            var pendienteNat = await m.Send(Nueva(Codigo(suf, "1.5"), nat: null, padre: raiz));
            var clientes = await m.Send(Nueva(Codigo(suf, "1.6"), control: CuentaControl.Clientes, padre: raiz));
            var proveedores = await m.Send(Nueva(Codigo(suf, "1.7"), control: CuentaControl.Proveedores, padre: raiz));
            var deudores = await m.Send(Nueva(Codigo(suf, "1.8"), control: CuentaControl.Deudores, padre: raiz));
            var acreedores = await m.Send(Nueva(Codigo(suf, "1.9"), control: CuentaControl.Acreedores, padre: raiz));

            // acepta activa + afectable (por código y por id; el código se normaliza)
            var v = await puerto.ValidarParaMovimientoAsync(Codigo(suf, "1.1").ToLowerInvariant(), OrigenMovimiento.Manual, default);
            Assert.True(v.Valida);
            Assert.Null(v.Motivo);
            Assert.Equal(ok.Id, v.Cuenta!.Id);
            Assert.True((await puerto.ValidarParaMovimientoAsync(ok.Id, OrigenMovimiento.Manual, default)).Valida);

            Assert.Equal(MotivoRechazoCuenta.Titulo, (await puerto.ValidarParaMovimientoAsync(titulo.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.Inactiva, (await puerto.ValidarParaMovimientoAsync(inactiva.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.True((await puerto.ValidarParaMovimientoAsync(sinTipo.Id, OrigenMovimiento.Manual, default)).Valida); // el tipo ya no queda pendiente
            Assert.Equal(MotivoRechazoCuenta.PendienteValidacion, (await puerto.ValidarParaMovimientoAsync(pendienteNat.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.NoExiste, (await puerto.ValidarParaMovimientoAsync(Guid.NewGuid(), OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.NoExiste, (await puerto.ValidarParaMovimientoAsync(Codigo(suf, "99"), OrigenMovimiento.Manual, default)).Motivo);

            // una inactiva sigue legible (saldos e históricos)
            var leida = await puerto.ObtenerAsync(inactiva.Id, default);
            Assert.NotNull(leida);
            Assert.False(leida!.Activa);

            // cuenta de control: solo por su auxiliar (R10)
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(clientes.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(clientes.Id, OrigenMovimiento.AuxiliarCxP, default)).Motivo);
            Assert.True((await puerto.ValidarParaMovimientoAsync(clientes.Id, OrigenMovimiento.AuxiliarCxC, default)).Valida);
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(proveedores.Id, OrigenMovimiento.AuxiliarCxC, default)).Motivo);
            Assert.True((await puerto.ValidarParaMovimientoAsync(proveedores.Id, OrigenMovimiento.AuxiliarCxP, default)).Valida);

            // P23 (supuesto por defecto): deudores por CxC, acreedores por CxP; la captura manual siempre se rechaza.
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(deudores.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(deudores.Id, OrigenMovimiento.AuxiliarCxP, default)).Motivo);
            Assert.True((await puerto.ValidarParaMovimientoAsync(deudores.Id, OrigenMovimiento.AuxiliarCxC, default)).Valida);
            Assert.Equal(MotivoRechazoCuenta.ControlSoloAuxiliar, (await puerto.ValidarParaMovimientoAsync(acreedores.Id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.True((await puerto.ValidarParaMovimientoAsync(acreedores.Id, OrigenMovimiento.AuxiliarCxP, default)).Valida);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Cuenta_de_otra_empresa_responde_NoExiste_y_no_se_filtra_su_lectura()
    {
        var suf = Sufijo();
        try
        {
            Guid id;
            using (var a = factory.ConEmpresa(EmpresaBootstrapId))
            using (var sa = a.Services.CreateScope())
                id = (await sa.ServiceProvider.GetRequiredService<IMediator>().Send(Nueva(Codigo(suf, "1")))).Id;

            using var b = factory.ConEmpresa(Guid.NewGuid());
            using var sb = b.Services.CreateScope();
            var puerto = sb.ServiceProvider.GetRequiredService<ICuentaContableReadPort>();
            Assert.Equal(MotivoRechazoCuenta.NoExiste, (await puerto.ValidarParaMovimientoAsync(id, OrigenMovimiento.Manual, default)).Motivo);
            Assert.Equal(MotivoRechazoCuenta.NoExiste, (await puerto.ValidarParaMovimientoAsync(Codigo(suf, "1"), OrigenMovimiento.Manual, default)).Motivo);
            Assert.Null(await puerto.ObtenerAsync(id, default));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Endpoint_validar_movimiento_usa_el_mismo_puerto_y_exige_solo_leer()
    {
        var suf = Sufijo();
        try
        {
            var admin = await LoginAsync(factory);
            var raiz = await CrearCuenta(admin, Codigo(suf, "2"));
            var cuenta = await CrearCuenta(admin, Codigo(suf, "2.1"), padreId: raiz.GetProperty("id").GetGuid());
            var consulta = await ClienteConPermisosAsync(factory, Millet.Identidad.Domain.PermisosCanonicos.ContabilidadCatalogoLeer);

            var ok = await Json(await consulta.PostAsJsonAsync($"{Base}/cuentas/validar-movimiento", new { codigo = Codigo(suf, "2.1"), origen = "Manual" }));
            Assert.True(ok.GetProperty("valida").GetBoolean());
            Assert.Equal(cuenta.GetProperty("id").GetGuid(), ok.GetProperty("cuenta").GetProperty("id").GetGuid());
            var tit = await Json(await consulta.PostAsJsonAsync($"{Base}/cuentas/validar-movimiento", new { codigo = Codigo(suf, "2"), origen = "Manual" }));
            Assert.False(tit.GetProperty("valida").GetBoolean());
            Assert.Equal("Titulo", tit.GetProperty("motivo").GetString());
            Assert.Equal("NoExiste", (await Json(await consulta.PostAsJsonAsync($"{Base}/cuentas/validar-movimiento", new { cuentaId = Guid.NewGuid(), origen = "Manual" }))).GetProperty("motivo").GetString());

            var sinPermiso = await ClienteConPermisosAsync(factory, Millet.Identidad.Domain.PermisosCanonicos.ContabilidadCatalogoImportar);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.PostAsJsonAsync($"{Base}/cuentas/validar-movimiento", new { codigo = Codigo(suf, "1"), origen = "Manual" })).StatusCode);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Permisos_canonicos_sembrados_por_la_migracion_de_Identidad()
    {
        var n = await Escalar<long>(factory.Services,
            "SELECT count(*) FROM identidad.permisos WHERE id IN ('0000000d-0001-0000-0000-000000000001','0000000d-0001-0000-0000-000000000002','0000000d-0001-0000-0000-000000000003') AND codigo LIKE 'contabilidad.catalogo.%'");
        Assert.Equal(3, n);
    }
}
