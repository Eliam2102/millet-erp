using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;
using Npgsql;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// ADM-BIT (86e3xd4ut): consultar y exportar la bitácora. Permiso propio de exportar (criterio 1), renglón de control con el
/// total (criterio 2), alcance por sucursal (criterio 3) y bitácora inmutable en la base (criterio 4).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class BitacoraAdmBitTests(WebApplicationFactory<Program> factory)
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/auditoria";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Fact]
    public async Task Con_permiso_de_consultar_y_sin_el_de_exportar_ve_resultados_y_la_exportacion_responde_403()
    {
        var marcador = $"ADMBIT-403-{Guid.NewGuid():N}";
        await SembrarAsync(marcador, 3, sucursalId: null);
        var (client, _) = await CrearUsuarioAsync(PermisosCanonicos.AdminAuditoriaLeer, PermisosCanonicos.AdminAuditoriaLeerTodasSucursales);
        var filtro = Rango() + $"&recurso={marcador}";

        var consulta = await client.GetAsync($"{EndpointBase}?{filtro}");
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        Assert.Equal(3, (await LeerJsonAsync(consulta)).GetProperty("total").GetInt32());

        var exportar = await client.GetAsync($"{EndpointBase}/exportar?{filtro}");
        Assert.Equal(HttpStatusCode.Forbidden, exportar.StatusCode);
    }

    [Fact]
    public async Task La_exportacion_trae_los_renglones_mas_el_renglon_de_control_con_el_mismo_total_y_queda_en_la_bitacora()
    {
        var marcador = $"ADMBIT-CSV-{Guid.NewGuid():N}";
        await SembrarAsync(marcador, 137, sucursalId: null, modulo: "Compras");
        var client = await SuperAdminAsync();
        var filtro = Rango() + $"&modulo=Compras&recurso={marcador}";

        var total = (await LeerJsonAsync(await client.GetAsync($"{EndpointBase}?{filtro}"))).GetProperty("total").GetInt32();
        Assert.Equal(137, total);

        var respuesta = await client.GetAsync($"{EndpointBase}/exportar?{filtro}");
        respuesta.EnsureSuccessStatusCode();
        var csv = System.Text.Encoding.UTF8.GetString(await respuesta.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var renglones = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // Encabezado + 137 registros + renglón de control.
        Assert.Equal(1 + 137 + 1, renglones.Length);
        Assert.Equal(137, renglones.Count(r => r.Contains(marcador, StringComparison.Ordinal) && !r.StartsWith("\"Total", StringComparison.Ordinal)));
        var control = renglones[^1];
        Assert.StartsWith("\"Total 137\"", control, StringComparison.Ordinal);
        Assert.Contains("Rango ", control, StringComparison.Ordinal);
        Assert.Contains("modulo=Compras", control, StringComparison.Ordinal);
        Assert.Contains($"recurso={marcador}", control, StringComparison.Ordinal);
        Assert.Contains("Exportó ", control, StringComparison.Ordinal);
        Assert.Contains("Generado ", control, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var desde = DateTimeOffset.UtcNow.AddMinutes(-10);
        var exportacion = Assert.Single(await db.AuditLog.AsNoTracking()
            .Where(x => x.Operacion == "exportar" && x.Entidad == "Bitacora" && x.Timestamp >= desde)
            .ToListAsync(), x => x.Cambios.Contains(marcador, StringComparison.Ordinal));
        Assert.Equal("Exportó 137 registros de auditoría en CSV", exportacion.Resumen);
    }

    [Fact]
    public async Task Un_usuario_con_alcance_de_una_sola_sucursal_no_ve_registros_de_otras()
    {
        var admin = await SuperAdminAsync();
        var propia = await CrearSucursalAsync(admin, "CNK");
        var ajena = await CrearSucursalAsync(admin, "OTR");
        var marcador = $"ADMBIT-SUC-{Guid.NewGuid():N}";
        await SembrarAsync(marcador, 2, propia);
        await SembrarAsync(marcador, 4, ajena);
        await SembrarAsync(marcador, 1, sucursalId: null);
        var (client, usuarioId) = await CrearUsuarioAsync(PermisosCanonicos.AdminAuditoriaLeer, PermisosCanonicos.AdminAuditoriaExportar);
        using (var scope = factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            identidad.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, propia, Empresa));
            await identidad.SaveChangesAsync();
        }
        var filtro = Rango() + $"&recurso={marcador}&limit=200";

        var consulta = await LeerJsonAsync(await client.GetAsync($"{EndpointBase}?{filtro}"));
        Assert.Equal(2, consulta.GetProperty("total").GetInt32());
        Assert.All(consulta.GetProperty("items").EnumerateArray(),
            i => Assert.Equal(propia, i.GetProperty("sucursalId").GetGuid()));

        var csv = await (await client.GetAsync($"{EndpointBase}/exportar?{filtro}")).Content.ReadAsStringAsync();
        Assert.Contains("\"Total 2\"", csv, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{EndpointBase}?{filtro}&sucursalId={ajena}")).StatusCode);
    }

    [Theory]
    [InlineData("UPDATE core.audit_log SET resumen = 'alterado' WHERE entidad = {0}")]
    [InlineData("DELETE FROM core.audit_log WHERE entidad = {0}")]
    public async Task Modificar_o_borrar_un_registro_directo_en_la_base_se_rechaza(string sql)
    {
        var marcador = $"ADMBIT-INM-{Guid.NewGuid():N}";
        await SembrarAsync(marcador, 1, sucursalId: null);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, marcador));
        Assert.Contains("inmutable", ex.MessageText, StringComparison.Ordinal);
        var registro = await db.AuditLog.AsNoTracking().SingleAsync(a => a.Entidad == marcador);
        Assert.Equal("Registro de prueba", registro.Resumen);
    }

    [Fact]
    public async Task Vaciar_la_bitacora_o_una_de_sus_particiones_se_rechaza_y_toda_particion_lleva_el_bloqueo()
    {
        _ = factory.CreateClient(); // el arranque crea la partición del mes y tres futuras
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var mes = DateTime.UtcNow;
        var particion = $"core.audit_log_y{mes:yyyy}m{mes:MM}";

        foreach (var tabla in new[] { "core.audit_log", particion })
        {
            // Dentro de una transacción que siempre se deshace: si el bloqueo faltara, no se vacía la bitácora compartida.
            await using var tx = await db.Database.BeginTransactionAsync();
            // TRUNCATE pide el candado exclusivo antes de que el disparador lo rechace: sin límite podría esperar
            // indefinidamente detrás de otra transacción abierta sobre la bitácora (pruebas en paralelo).
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '15s'");
#pragma warning disable EF1003 // DDL: el nombre de la tabla sale del mes del reloj, no de un request.
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE " + tabla));
#pragma warning restore EF1003
            Assert.Contains("inmutable", ex.MessageText, StringComparison.Ordinal);
            await tx.RollbackAsync();
        }

        var sinBloqueo = await db.Database.SqlQueryRaw<string>("""
            SELECT i.inhrelid::regclass::text AS "Value" FROM pg_inherits i
            WHERE i.inhparent = 'core.audit_log'::regclass
              AND NOT EXISTS (SELECT 1 FROM pg_trigger t WHERE t.tgrelid = i.inhrelid AND t.tgname = 'trg_audit_log_sin_truncate')
            """).ToListAsync();
        Assert.Empty(sinBloqueo);
    }

    private static string Rango()
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        return $"desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy.AddDays(1):yyyy-MM-dd}";
    }

    private async Task SembrarAsync(string marcador, int cantidad, Guid? sucursalId, string modulo = "QA")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        for (var i = 0; i < cantidad; i++)
        {
            db.AuditLog.Add(new AuditLogEntry
            {
                Id = Guid.CreateVersion7(), Timestamp = DateTimeOffset.UtcNow, EmpresaId = Empresa,
                Modulo = modulo, Entidad = marcador, Operacion = "crear", Cambios = "{}",
                ActorNombre = "ADM-BIT", ActorTipo = "usuario", EntidadEtiqueta = marcador,
                Resumen = "Registro de prueba", CorrelationId = Guid.NewGuid(),
                Metadatos = sucursalId is null ? null : JsonSerializer.Serialize(new { sucursalId }),
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> SuperAdminAsync()
    {
        var client = factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev"));
        return client;
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CrearUsuarioAsync(params string[] permisos)
    {
        var oid = $"admbit-{Guid.NewGuid():N}"[..20];
        var usuarioId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, oid, "Rol de prueba ADM-BIT"));
            foreach (var codigo in permisos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }
            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario ADM-BIT"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }
        var client = factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario ADM-BIT"));
        return (client, usuarioId);
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefijo)
    {
        var clave = $"{prefijo}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = clave, Nombre = $"Sucursal {clave}" });
        response.EnsureSuccessStatusCode();
        return (await LeerJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = nombre, EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        return (await LeerJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }
}
