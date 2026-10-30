using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth;
using Millet.Api.Seed;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>Datos y usuarios de las pruebas HTTP de Producto Terminado; todo con identificadores únicos <c>PT-INT-&lt;guid&gt;</c>.</summary>
internal sealed class PtDatos(WebApplicationFactory<Program> factory)
{
    public static readonly Guid Empresa = DemoSesionSeedHostedService.EmpresaId;
    private static readonly Guid Cliente = DemoSesionSeedHostedService.Id("DEMO-CLI-EXP-1");

    public static string Nuevo() => "PT-INT-" + Guid.NewGuid().ToString("N");

    /// <summary>Dos sucursales reales distintas de la empresa demo.</summary>
    public async Task<(Guid A, Guid B)> SucursalesAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var ids = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales
            .Where(x => x.EmpresaId == Empresa).OrderBy(x => x.Clave).Select(x => x.Id).Take(2).ToListAsync();
        Assert.True(ids.Count == 2, "Se requieren al menos dos sucursales en la empresa demo.");
        return (ids[0], ids[1]);
    }

    /// <summary>Cliente HTTP autenticado con un usuario nuevo, los permisos dados y las sucursales asociadas.</summary>
    public async Task<HttpClient> ClienteAsync(string[] permisos, params Guid[] sucursales)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var id = Guid.NewGuid();
        var usuario = new Usuario(id, id.ToString(), $"pt-{id:N}@example.invalid", "PT-INT usuario");
        var rol = new Rol(Guid.NewGuid(), "PT-INT-" + id.ToString("N")[..8], "PT-INT rol");
        db.Usuarios.Add(usuario);
        db.Roles.Add(rol);
        foreach (var p in PermisosCanonicos.Todos.Where(p => permisos.Contains(p.Codigo)))
            db.RolPermisos.Add(new RolPermiso(Guid.NewGuid(), rol.Id, p.Id));
        db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuario.Id, Empresa, rol.Id));
        foreach (var s in sucursales)
            db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuario.Id, s, Empresa));
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(usuario, Empresa);
        var client = factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    /// <summary>Pedido local de PT con una posición de <paramref name="esperadas"/> piezas.</summary>
    public async Task<string> PedidoAsync(Guid sucursalVenta, int esperadas = 5)
    {
        var numero = Nuevo();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
        var p = PedidoPt.Crear(Empresa, Guid.NewGuid(), numero, sucursalVenta, null, 1);
        p.ActualizarPosiciones(1, [(1, null, "PT-INT vidrio", esperadas, "H87")]);
        db.PedidosPt.Add(p);
        await db.SaveChangesAsync();
        return numero;
    }

    /// <summary>Pieza en PT en <paramref name="sucursalFisica"/> con su escaneo Alta.</summary>
    public async Task PiezaAsync(string numeroPedido, Guid sucursalFisica, int numeroPieza)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
        var pedido = await db.PedidosPt.SingleAsync(x => x.EmpresaId == Empresa && x.NumeroPedido == numeroPedido);
        var etiqueta = Nuevo();
        var registrado = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);
        var e = EscaneoPt.Registrar(Empresa, "Demo", etiqueta, numeroPedido, 1, null, numeroPieza.ToString(), 2821, null, registrado, DateTimeOffset.UtcNow.AddMinutes(-5), null, sucursalFisica, DateTimeOffset.UtcNow);
        var pieza = PiezaPt.Alta(Empresa, pedido, 1, numeroPieza, etiqueta, sucursalFisica, e.RegistradoUtc, e.Id);
        e.MarcarAlta(pieza.Id);
        db.Escaneos.Add(e);
        db.Piezas.Add(pieza);
        await db.SaveChangesAsync();
    }

    /// <summary>Escaneo abierto en bandeja. <paramref name="resultado"/>: Pospuesto, Excepcion o Reposicion.</summary>
    public async Task<(Guid Id, int Version, string Pedido)> EscaneoAsync(string resultado, Guid? sucursal, string? pedido = null, int numeroPieza = 1, string motivo = MotivosPt.PedidoInexistente, int intentos = 0)
    {
        pedido ??= Nuevo();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
        var registrado = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-3), DateTimeKind.Unspecified);
        var e = EscaneoPt.Registrar(Empresa, "Demo", Nuevo(), pedido, 1, null, numeroPieza.ToString(), 2821, null, registrado, DateTimeOffset.UtcNow.AddMinutes(-3), null, sucursal, DateTimeOffset.UtcNow);
        switch (resultado)
        {
            case "Pospuesto": e.MarcarPospuesto(motivo); break;
            case "Reposicion": e.MarcarReposicion(); break;
            default: e.MarcarExcepcion(motivo); break;
        }
        for (var i = 0; i < intentos; i++) e.Intentar(99);
        db.Escaneos.Add(e);
        await db.SaveChangesAsync();
        return (e.Id, e.Version, pedido);
    }

    /// <summary>Crea en Facturación el pedido A+W con una posición de <paramref name="cantidad"/>.</summary>
    public async Task FacturacionAsync(string numeroPedido, Guid sucursal, decimal cantidad)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var p = PedidoFacturable.ImportarDesdeAw(Empresa, numeroPedido, sucursal, Cliente, "PT-INT cliente", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, "PT-INT pedido", 1, "15");
        p.AgregarLinea(null, "PT-INT vidrio", "30171700", "H87", cantidad, 100, 0, false, tasaIva: 0);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        await db.SaveChangesAsync();
    }

    public async Task<EscaneoPt> LeerEscaneoAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>().Escaneos.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    public async Task<int> PiezasPorEtiquetaAsync(string etiqueta)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>().Piezas.CountAsync(x => x.Etiqueta == etiqueta);
    }
}
