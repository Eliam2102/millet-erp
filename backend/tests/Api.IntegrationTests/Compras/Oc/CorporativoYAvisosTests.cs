using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task Corporativo_IncluyeSucursalNueva_YTransferenciaRestringeSinCambiarRol()
    {
        var client = await CreateSuperAdminClientAsync();
        var usuarioId = Guid.NewGuid();
        var empleadoId = Guid.NewGuid();
        Guid sucursalId;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<IdentidadDbContext>();
            var maestros = sp.GetRequiredService<CompartidoDbContext>();
            sucursalId = await maestros.Sucursales.Where(s => s.EmpresaId == EmpresaInicialId).Select(s => s.Id).FirstAsync();
            db.Usuarios.Add(new Usuario(usuarioId, "corp-" + usuarioId, usuarioId + "@example.invalid", "Prueba corporativo"));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuarioId, EmpresaInicialId,
                Guid.Parse("00000002-0003-0000-0000-000000000004")));
            db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuarioId, sucursalId, EmpresaInicialId));
            await db.SaveChangesAsync();
            maestros.Empleados.Add(new Empleado(empleadoId, EmpresaInicialId, "CORP-" + usuarioId.ToString("N")[..8], "Prueba corporativo",
                sucursalId: sucursalId, usuarioId: usuarioId));
            await maestros.SaveChangesAsync();
        }
        var url = $"/api/v1/admin/usuarios/{usuarioId}/alcance";
        (await client.PutAsJsonAsync(url, new { EsCorporativo = true, Sucursales = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        var consulta = await ReadJsonAsync(await client.GetAsync(url));
        Assert.True(consulta.GetProperty("esCorporativo").GetBoolean());
        Assert.Equal(0, consulta.GetProperty("sucursales").GetArrayLength());
        Guid nuevaId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var maestros = sp.GetRequiredService<CompartidoDbContext>();
            var db = sp.GetRequiredService<IdentidadDbContext>();
            Assert.Null((await maestros.Empleados.SingleAsync(e => e.Id == empleadoId)).SucursalId);
            maestros.Sucursales.Add(new Sucursal(nuevaId, EmpresaInicialId, "CORP" + nuevaId.ToString("N")[..6], "Nueva corporativa",
                TipoSucursal.Taller, "Calle", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "MX"));
            await maestros.SaveChangesAsync();
            var port = sp.GetRequiredService<IUsuarioSucursalReadPort>();
            Assert.True(await port.EsCorporativoAsync(usuarioId, default));
            Assert.Contains(nuevaId, await port.ListarIdsAsync(usuarioId, default));
            Assert.True(await port.EstaAsociadoAsync(usuarioId, nuevaId, default));
            Assert.Equal(1, await db.UsuarioEmpresaRoles.CountAsync(r => r.UsuarioId == usuarioId));
        }
        var usuarioClient = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(usuarioClient, "corp-" + usuarioId, usuarioId + "@example.invalid", "Prueba corporativo");
        usuarioClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var miAlcance = await ReadJsonAsync(await usuarioClient.GetAsync("/api/auth/alcance"));
        Assert.True(miAlcance.GetProperty("esCorporativo").GetBoolean());
        var miSelector = await ReadJsonAsync(await usuarioClient.GetAsync("/api/auth/sucursales"));
        Assert.Contains(nuevaId, miSelector.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));

        // Transferir la adscripción laboral sincroniza el alcance operativo y sale de corporativo.
        (await client.PatchAsJsonAsync($"/api/v1/admin/empleados/{empleadoId}", new { SucursalId = nuevaId })).EnsureSuccessStatusCode();
        consulta = await ReadJsonAsync(await client.GetAsync(url));
        Assert.False(consulta.GetProperty("esCorporativo").GetBoolean());
        Assert.Equal(nuevaId, Assert.Single(consulta.GetProperty("sucursales").EnumerateArray()).GetGuid());
        var invalida = await client.PutAsJsonAsync(url, new { EsCorporativo = false, Sucursales = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalida.StatusCode);
        var despues = await ReadJsonAsync(await client.GetAsync(url));
        Assert.Equal(nuevaId, Assert.Single(despues.GetProperty("sucursales").EnumerateArray()).GetGuid());
    }

    [Fact]
    public async Task Avisos_SoloPendientesFirmables_ExcluyenCapturistaYSegundaFirmaPropia()
    {
        var client = await CreateSuperAdminClientAsync();
        var creada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { SinRequisicionPrevia = true, MotivoSinRequisicion = "Avisos" });
        creada.EnsureSuccessStatusCode();
        var propiaId = (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<Millet.Compras.Infrastructure.ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == propiaId);
            AgregarLineaP2(oc);
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var (ajenaId, _) = await SembrarP2(client, recepcion: false, autorizar: false);
        var url = EndpointBase + "/pendientes-autorizacion?soloAccionables=true&pageSize=200";
        var antes = await ReadJsonAsync(await client.GetAsync(url));
        var ids = antes.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();
        Assert.DoesNotContain(propiaId, ids);
        Assert.Contains(ajenaId, ids);
        (await client.PostAsJsonAsync($"{EndpointBase}/{ajenaId}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        var despues = await ReadJsonAsync(await client.GetAsync(url));
        Assert.DoesNotContain(ajenaId, despues.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }
    [Fact]
    public async Task FlujoOc_Corporativo_Transmite_Avisa_DobleFirma_YBloqueaAutoautorizacion()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursalDestino = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var maestros = sp.GetRequiredService<CompartidoDbContext>();
            maestros.Sucursales.Add(new Sucursal(sucursalDestino, EmpresaInicialId,
                "OC" + sucursalDestino.ToString("N")[..8], "Destino de prueba", TipoSucursal.Taller,
                "Calle", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "MX"));
            await maestros.SaveChangesAsync();
        }
        var creada = await admin.PostAsJsonAsync(EndpointBase, ValidBody() with {
            SucursalDestinoId = sucursalDestino, SucursalCodigo = "MTY",
            SinRequisicionPrevia = true, MotivoSinRequisicion = "Prueba completa de autorización" });
        creada.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<Millet.Compras.Infrastructure.ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            AgregarLineaP2(oc);
            var tipos = await db.TiposDocumentoOc.Where(t => t.Clave == "cotizacion" || t.Clave == "correo_autorizacion").ToListAsync();
            foreach (var tipo in tipos)
                oc.AdjuntarDocumento(Guid.NewGuid(), tipo.Id, tipo.Clave + ".pdf", "prueba-" + tipo.Clave,
                    "application/pdf", 10, DateTimeOffset.UtcNow, oc.CompradorTitularId);
            await db.SaveChangesAsync();
        }
        (await admin.PostAsync($"{EndpointBase}/{ocId}/transmitir", null)).EnsureSuccessStatusCode();
        var rechazo = await admin.PostAsJsonAsync($"{EndpointBase}/{ocId}/autorizaciones", new { Nivel = 1 });
        await AssertCodigoP2(rechazo, HttpStatusCode.UnprocessableEntity, "OC_AUTOAUTORIZACION");
        var detalle = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{ocId}"));
        Assert.Equal(1, detalle.GetProperty("estado").GetInt32());
        Assert.Equal(0, detalle.GetProperty("autorizaciones").GetArrayLength());

        var (jefe, jefeId) = await CrearAutorizadorDePruebaAsync("compras.ordenes.autorizar-nivel1");
        var (direccion, direccionId) = await CrearAutorizadorDePruebaAsync("compras.ordenes.autorizar-nivel2");
        var avisos = EndpointBase + "/pendientes-autorizacion?soloAccionables=true&pageSize=200";
        Assert.Equal(HttpStatusCode.Forbidden, (await jefe.GetAsync($"{EndpointBase}/{ocId}")).StatusCode);
        var sinAlcance = await ReadJsonAsync(await jefe.GetAsync(avisos));
        Assert.DoesNotContain(ocId, sinAlcance.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        foreach (var id in new[] { jefeId, direccionId })
            (await admin.PutAsJsonAsync($"/api/v1/admin/usuarios/{id}/alcance",
                new { EsCorporativo = true, Sucursales = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        (await jefe.GetAsync($"{EndpointBase}/{ocId}")).EnsureSuccessStatusCode();
        var avisoJefe = await ReadJsonAsync(await jefe.GetAsync(avisos));
        Assert.Contains(ocId, avisoJefe.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        var antesDireccion = await ReadJsonAsync(await direccion.GetAsync(avisos));
        Assert.DoesNotContain(ocId, antesDireccion.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        (await jefe.PostAsJsonAsync($"{EndpointBase}/{ocId}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await jefe.PostAsJsonAsync($"{EndpointBase}/{ocId}/autorizaciones", new { Nivel = 2 })).StatusCode);
        var avisoDireccion = await ReadJsonAsync(await direccion.GetAsync(avisos));
        Assert.Contains(ocId, avisoDireccion.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        (await direccion.PostAsJsonAsync($"{EndpointBase}/{ocId}/autorizaciones", new { Nivel = 2 })).EnsureSuccessStatusCode();
        detalle = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{ocId}"));
        Assert.Equal(3, detalle.GetProperty("estado").GetInt32());
        var firmas = detalle.GetProperty("autorizaciones").EnumerateArray().ToArray();
        Assert.Equal(2, firmas.Length);
        Assert.Equal(2, firmas.Select(f => f.GetProperty("usuarioId").GetGuid()).Distinct().Count());
        foreach (var client in new[] { jefe, direccion })
        {
            var resuelta = await ReadJsonAsync(await client.GetAsync(avisos));
            Assert.DoesNotContain(ocId, resuelta.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        }
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CrearAutorizadorDePruebaAsync(string permisoFirma)
    {
        var id = Guid.NewGuid();
        var oid = "prueba-autorizador-" + id;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<IdentidadDbContext>();
            var rol = new Rol(Guid.NewGuid(), "PRU-" + id.ToString("N")[..10], "Autorizador de prueba");
            db.Roles.Add(rol);
            foreach (var codigo in new[] { "compras.ordenes.leer", permisoFirma })
            {
                var permiso = await db.Permisos.SingleAsync(p => p.Codigo == codigo);
                db.RolPermisos.Add(new RolPermiso(Guid.NewGuid(), rol.Id, permiso.Id));
            }
            db.Usuarios.Add(new Usuario(id, oid, id + "@example.invalid", "Autorizador de prueba"));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), id, EmpresaInicialId, rol.Id));
            db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), id, SucursalIdFija, EmpresaInicialId));
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, id + "@example.invalid", "Autorizador de prueba");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, id);
    }

}
