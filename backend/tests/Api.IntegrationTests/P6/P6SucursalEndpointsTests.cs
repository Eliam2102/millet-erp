using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.Auth.Models;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.P6;

/// <summary>Documentos propios/ajenos sobre PostgreSQL. Usa sucursales del seed; no modifica catálogos compartidos.</summary>
public sealed class P6SucursalEndpointsTests(P6SucursalEndpointsFactory factory) : IClassFixture<P6SucursalEndpointsFactory>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Propia = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly Guid Ajena = Guid.Parse("00000005-0003-0000-0000-000000000002");
    private static readonly Guid Proveedor = Guid.Parse("00000005-0001-0000-0000-000000000001");

    public static TheoryData<string, string, string> Escrituras => new()
    {
        { "rq", "PATCH", "" }, { "rq", "POST", "/transmitir" }, { "rq", "POST", "/autorizaciones" },
        { "rq", "POST", "/cancelar" }, { "rq", "POST", "/cerrar-manual" }, { "rq", "POST", "/rechazar" }, { "rq", "POST", "/eliminar" },
        { "rq", "POST", "/lineas" }, { "rq", "PATCH", "/lineas/{linea}" }, { "rq", "DELETE", "/lineas/{linea}" },
        { "rq", "PATCH", "/lineas/{linea}/notas" },
        { "oc", "POST", "/duplicar" }, { "oc", "PATCH", "" }, { "oc", "POST", "/lineas" }, { "oc", "PATCH", "/lineas/{linea}" },
        { "oc", "DELETE", "/lineas/{linea}" }, { "oc", "PATCH", "/lineas/{linea}/texto-adicional" },
        { "oc", "PATCH", "/referencia-proveedor" }, { "oc", "PATCH", "/contacto-proveedor" },
        { "oc", "PATCH", "/informacion-logistica" }, { "oc", "PATCH", "/informacion-importacion" },
        { "oc", "POST", "/transmitir" }, { "oc", "POST", "/cerrar-manual" }, { "oc", "POST", "/autorizaciones" },
        { "oc", "POST", "/cancelar" }, { "oc", "POST", "/resolver-cancelacion" }, { "oc", "POST", "/cancelar-con-recepciones" }, { "oc", "POST", "/rechazar" },
        { "oc", "PATCH", "/numero-pedimento" }, { "oc", "POST", "/lineas/desde-requisicion" },
        { "factura", "GET", "/evidencias" }, { "factura", "POST", "/evidencias" },
        { "cfdi", "POST", "/descartar" }, { "cfdi", "POST", "/marcar-duplicado" },
        { "movimiento_tc", "POST", "/disputar" }, { "movimiento_tc", "POST", "/resolver-disputa" },
        { "nota_cargo", "POST", "/autorizar" }, { "nota_cargo", "POST", "/aplicar" },
        { "nota_credito", "POST", "/vincular-factura" },
        { "comprobacion", "POST", "/enviar-revision" }, { "comprobacion", "POST", "/autorizar" },
        { "comprobacion", "POST", "/aplicar" }, { "comprobacion", "POST", "/autorizar-nivel1" },
        { "comprobacion", "POST", "/autorizar-nivel2" }, { "comprobacion", "POST", "/rechazar" },
        { "pago", "POST", "/revertir" }, { "movimiento", "POST", "/reclasificar" },
        { "factura", "PATCH", "" }, { "factura", "POST", "/enviar-revision" }, { "factura", "POST", "/liberar-revision" },
        { "factura", "POST", "/autorizar" }, { "factura", "POST", "/cancelar" },
        { "factura", "POST", "/aplicar-nc" }, { "factura", "POST", "/aplicar-anticipo" },
        { "deposito", "POST", "/confirmar" }, { "deposito", "POST", "/rechazar" },
    };

    [Theory]
    [MemberData(nameof(Escrituras))]
    public async Task Escritura_de_documento_ajeno_es_403_sin_cambio(string tipo, string metodo, string sufijo)
    {
        await using var datos = await PrepararAsync();
        var documento = datos.Ajenos[tipo];
        using var request = new HttpRequestMessage(new HttpMethod(metodo), Ruta(tipo, documento) + sufijo.Replace("{linea}", Guid.NewGuid().ToString()));
        request.Headers.Add("X-Expected-Version", "1");
        // El cuerpo debe ser válido para cada ruta: si no, el 400/415 del enlace del modelo ocultaría el control de sucursal.
        if (tipo == "factura" && sufijo == "/cancelar")
            request.Content = JsonContent.Create(new { motivo = (int)Millet.CuentasPorPagar.Domain.FacturaProveedor.MotivoCancelacion.ErrorCaptura, texto = "Prueba P6" });
        else if (tipo == "factura" && sufijo == "/evidencias")
        {
            var multipart = new MultipartFormDataContent();
            var archivo = new ByteArrayContent("%PDF-1.4 prueba P6"u8.ToArray());
            archivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            multipart.Add(archivo, "archivo", "evidencia-p6.pdf");
            multipart.Add(new StringContent(((int)default(Millet.CuentasPorPagar.Domain.Evidencias.TipoEvidencia)).ToString()), "tipo");
            multipart.Add(new StringContent("Prueba P6"), "comentario");
            multipart.Add(new StringContent(((int)default(Millet.CuentasPorPagar.Domain.Evidencias.EstadoFirmaFisica)).ToString()), "estadoFirmaFisica");
            request.Content = multipart;
        }
        else if (metodo is not ("DELETE" or "GET")) request.Content = JsonContent.Create(new { Nivel = 1, Motivo = "Prueba P6", MotivoTexto = "Prueba P6", Observaciones = "No cambiar", Descripcion = "No cambiar" });
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.SendAsync(request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(Ajena, await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor
            .Where(x => x.Id == datos.Ajenos["factura"]).Select(x => x.SucursalId).SingleAsync());
    }

    [Theory]
    [InlineData("oc", "autorizaciones", 1, PermisosCanonicos.ComprasOrdenesAutorizarNivel1, "AUTORIZAR_NIVEL_DENEGADO")]
    [InlineData("oc", "autorizaciones", 2, PermisosCanonicos.ComprasOrdenesAutorizarNivel2, "AUTORIZAR_NIVEL_DENEGADO")]
    [InlineData("oc", "cancelar-con-recepciones", 1, PermisosCanonicos.ComprasOrdenesAutorizarNivel1, "OC_CANCELAR_DOBLE_DENEGADO")]
    [InlineData("rq", "autorizaciones", 1, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, "AUTORIZAR_NIVEL_DENEGADO")]
    [InlineData("rq", "autorizaciones", 2, PermisosCanonicos.ComprasRequisicionesAutorizarNivel2, "AUTORIZAR_NIVEL_DENEGADO")]
    public async Task Permiso_del_paso_se_valida_antes_de_buscar_el_documento_o_su_sucursal(
        string tipo, string paso, int nivel, string permisoDenegado, string codigo)
    {
        await using var datos = await PrepararAsync();
        using var scope = factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var permisoId = await identidad.Permisos.Where(x => x.Codigo == permisoDenegado).Select(x => x.Id).SingleAsync();
        identidad.RolPermisos.RemoveRange(await identidad.RolPermisos
            .Where(x => x.RolId == datos.RolId && x.PermisoId == permisoId).ToListAsync());
        await identidad.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);

        foreach (var id in new[] { Guid.NewGuid(), datos.Propios[tipo], datos.Ajenos[tipo] })
        {
            var response = await datos.Operativo.PostAsJsonAsync(Ruta(tipo, id) + $"/{paso}", new { Nivel = nivel });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(codigo, await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("oc", 1)]
    [InlineData("oc", 2)]
    [InlineData("rq", 1)]
    [InlineData("rq", 2)]
    public async Task Con_permiso_del_nivel_el_documento_ajeno_sigue_bloqueado_por_sucursal(string tipo, int nivel)
    {
        await using var datos = await PrepararAsync();
        var response = await datos.Operativo.PostAsJsonAsync(Ruta(tipo, datos.Ajenos[tipo]) + "/autorizaciones", new { Nivel = nivel });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("rq", "")]
    [InlineData("rq", "/historico")]
    [InlineData("oc", "")]
    [InlineData("oc", "/origen")]
    [InlineData("oc", "/pdf")]
    [InlineData("oc", "/historico")]
    [InlineData("factura", "")]
    [InlineData("propuesta", "")]
    [InlineData("cfdi", "")]
    [InlineData("cfdi", "/xml")]
    [InlineData("cfdi", "/pdf")]
    [InlineData("cfdi", "/parseado")]
    [InlineData("nota_cargo", "")]
    [InlineData("nota_credito", "")]
    [InlineData("comprobacion", "")]
    [InlineData("movimiento", "")]
    [InlineData("rq", "/adjuntos")]
    [InlineData("factura", "/adjuntos")]
    public async Task Lectura_ajena_403_y_propia_y_corporativa_autorizadas(string tipo, string sufijo)
    {
        await using var datos = await PrepararAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(Ruta(tipo, datos.Ajenos[tipo]) + sufijo)).StatusCode);
        if (sufijo is not ("/pdf" or "/xml" or "/parseado"))
        {
            // «/origen» de una OC sin requisición responde 204: lo que importa es que no sea 403.
            var permitidos = sufijo == "/origen" ? new[] { HttpStatusCode.OK, HttpStatusCode.NoContent } : new[] { HttpStatusCode.OK };
            Assert.Contains((await datos.Operativo.GetAsync(Ruta(tipo, datos.Propios[tipo]) + sufijo)).StatusCode, permitidos);
            Assert.Contains((await datos.Corporativo.GetAsync(Ruta(tipo, datos.Ajenos[tipo]) + sufijo)).StatusCode, permitidos);
        }
    }

    [Fact]
    public async Task Preparacion_fallida_limpia_documentos_y_rol_temporal_y_conserva_roles_del_sistema()
    {
        async Task<(Guid[] Rq, Guid[] Oc, Guid[] Roles, Guid[] PermisosSistema)> EstadoAsync()
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            return (
                await compras.Requisiciones.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(),
                await compras.OrdenesCompra.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(),
                await identidad.Roles.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(),
                await identidad.RolPermisos.Where(x => identidad.Roles.Any(r => r.Id == x.RolId && r.EsDelSistema))
                    .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        }

        // El host ya arrancó antes de la foto: el bootstrap no entra en la comparación.
        var antes = await EstadoAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PrepararAsync(
            () => throw new InvalidOperationException("Fallo de preparación simulado P6")));
        Assert.Equal("Fallo de preparación simulado P6", error.Message);
        var despues = await EstadoAsync();
        Assert.Equal(antes.Rq, despues.Rq);
        Assert.Equal(antes.Oc, despues.Oc);
        Assert.Equal(antes.Roles, despues.Roles);
        Assert.Equal(antes.PermisosSistema, despues.PermisosSistema);
    }

    [Fact]
    public async Task Listados_filtran_antes_de_paginar_y_corporativo_ve_ambas_sucursales()
    {
        await using var datos = await PrepararAsync();
        foreach (var tipo in new[] { "rq", "oc", "factura", "propuesta", "deposito", "nota_cargo", "nota_credito", "anticipo", "comprobacion", "reposicion", "movimiento", "cfdi", "movimiento_tc" })
        {
            var path = Ruta(tipo, null) + "?limit=200";
            var operativo = await datos.Operativo.GetAsync(path); operativo.EnsureSuccessStatusCode();
            var corporativo = await datos.Corporativo.GetAsync(path); corporativo.EnsureSuccessStatusCode();
            var propio = datos.Propios[tipo].ToString(); var ajeno = datos.Ajenos[tipo].ToString();
            var textoOperativo = await operativo.Content.ReadAsStringAsync();
            var textoCorporativo = await corporativo.Content.ReadAsStringAsync();
            Assert.Contains(propio, textoOperativo); Assert.DoesNotContain(ajeno, textoOperativo);
            Assert.Contains(propio, textoCorporativo); Assert.Contains(ajeno, textoCorporativo);
        }
        var pasivos = await datos.Operativo.GetStringAsync("/api/v1/tesoreria/pasivos-pendientes?limit=200");
        Assert.Contains(datos.Propios["factura"].ToString(), pasivos);
        Assert.DoesNotContain(datos.Ajenos["factura"].ToString(), pasivos);
        var cartera = await datos.Operativo.GetStringAsync($"/api/v1/cuentas-por-cobrar/cartera/facturas-abiertas?clienteId={datos.ClienteId}");
        Assert.Contains(datos.Propios["cartera"].ToString(), cartera);
        Assert.DoesNotContain(datos.Ajenos["cartera"].ToString(), cartera);
    }

    [Fact]
    public async Task Desligar_aplicacion_ajena_exige_sucursal_antes_de_modificar()
    {
        await using var datos = await PrepararAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/tesoreria/pagos-cuenta/{datos.Ajenos["movimiento"]}/aplicaciones/{datos.Ajenos["pago"]}/desligar")
        { Content = JsonContent.Create(new { Motivo = "Prueba P6" }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("flujo-efectivo")]
    [InlineData("auxiliar-bancos")]
    public async Task Reportes_bancarios_ajenos_403_propios_y_corporativos_permitidos(string reporte)
    {
        await using var datos = await PrepararAsync();
        string RutaReporte(Guid cuenta) => $"/api/v1/tesoreria/reportes/{reporte}?cuentaBancariaId={cuenta}&desde=2026-01-01&hasta=2026-12-31";
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(RutaReporte(datos.Ajenos["cuenta"]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Operativo.GetAsync(RutaReporte(datos.Propios["cuenta"]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Corporativo.GetAsync(RutaReporte(datos.Ajenos["cuenta"]))).StatusCode);
    }

    [Theory]
    [InlineData("antiguedad-saldos")]
    [InlineData("cartera")]
    [InlineData("pasivos-obras")]
    [InlineData("auxiliar-proveedores")]
    public async Task Reportes_P8_conservan_filtro_y_bypass_de_sucursal(string reporte)
    {
        await using var datos = await PrepararAsync();
        string RutaReporte(Guid sucursal) => $"/api/v1/cuentas-por-pagar/reportes/{reporte}?sucursalId={sucursal}&fechaCorte=2026-12-31";
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(RutaReporte(Ajena))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Operativo.GetAsync(RutaReporte(Propia))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Corporativo.GetAsync(RutaReporte(Ajena))).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/v1/compras/requisiciones")]
    [InlineData("POST", "/api/v1/compras/ordenes")]
    [InlineData("POST", "/api/v1/compras/ordenes/desde-requisicion")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/facturas")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/anticipos")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/notas-credito")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/notas-cargo")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/comprobaciones/caja-chica")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/comprobaciones/aduanales")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/reposiciones-caja/emitir")]
    [InlineData("PUT", "/api/v1/cuentas-por-pagar/reposiciones-caja/configuracion")]
    [InlineData("POST", "/api/v1/cuentas-por-cobrar/propuestas-aplicacion")]
    [InlineData("POST", "/api/v1/cuentas-por-cobrar/cobranza")]
    [InlineData("POST", "/api/v1/tesoreria/pagos")]
    [InlineData("POST", "/api/v1/tesoreria/repp-recibidos")]
    [InlineData("POST", "/api/v1/tesoreria/movimientos")]
    [InlineData("POST", "/api/v1/tesoreria/pagos-cuenta")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/movimientos-tc/con-cfdi")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/movimientos-tc/sin-cfdi")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/movimientos-tc/refund")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/movimientos-tc/especial")]
    [InlineData("POST", "/api/v1/cuentas-por-pagar/estados-cuenta-tc")]
    public async Task Alta_con_referencia_a_sucursal_ajena_recibe_403(string metodo, string ruta)
    {
        await using var datos = await PrepararAsync();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var uuidCartera = await scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>().FacturasCartera
            .Where(x => x.Id == datos.Ajenos["cartera"]).Select(x => x.Uuid).SingleAsync();
        using var request = new HttpRequestMessage(new HttpMethod(metodo), ruta)
        {
            Content = JsonContent.Create(new {
                SucursalId = Ajena, SucursalDestinoId = Ajena, OrdenCompraId = datos.Ajenos["oc"], RequisicionId = datos.Ajenos["rq"],
                ClienteId = datos.ClienteId, ProveedorId = Proveedor, FacturaProveedorId = datos.Ajenos["factura"],
                CfdiRecibidoId = datos.Ajenos["cfdi"], MovimientoOriginalId = datos.Ajenos["movimiento_tc"],
                Moneda = "MXN", Serie = "FANT", UuidCfdi = Guid.NewGuid().ToString(), UuidRelacionCfdi = Guid.NewGuid().ToString(),
                Aplicaciones = new[] { new { FacturaProveedorId = datos.Ajenos["factura"], Importe = 10m } },
                Facturas = new[] { new { FacturaUuid = uuidCartera, MontoAplicar = 10m, Parcialidad = 1 } },
                FacturasProveedorIds = new[] { datos.Ajenos["factura"] },
            }),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Escritura_propia_y_corporativa_conservan_el_camino_valido()
    {
        await using var datos = await PrepararAsync();
        var propia = await datos.Operativo.PatchAsJsonAsync(Ruta("rq", datos.Propios["rq"]), new { Descripcion = "Cambio autorizado P6" });
        Assert.Equal(HttpStatusCode.NoContent, propia.StatusCode);
        var corporativa = await datos.Corporativo.PatchAsJsonAsync(Ruta("rq", datos.Ajenos["rq"]), new { Descripcion = "Cambio corporativo P6" });
        Assert.Equal(HttpStatusCode.NoContent, corporativa.StatusCode);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Equal("Cambio autorizado P6", await db.Requisiciones.Where(x => x.Id == datos.Propios["rq"]).Select(x => x.Descripcion).SingleAsync());
        Assert.Equal("Cambio corporativo P6", await db.Requisiciones.Where(x => x.Id == datos.Ajenos["rq"]).Select(x => x.Descripcion).SingleAsync());
    }

    [Theory]
    [InlineData("rq")]
    [InlineData("factura")]
    public async Task Adjuntos_exigen_permiso_del_tipo_aunque_la_sucursal_sea_propia(string tipo)
    {
        await using var datos = await PrepararAsync();
        using var scope = factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var codigo = tipo == "rq" ? PermisosCanonicos.ComprasRequisicionesAdjuntosVer : PermisosCanonicos.CuentasPorPagarFacturasAdjuntosVer;
        var permiso = await identidad.Permisos.SingleAsync(x => x.Codigo == codigo);
        identidad.RolPermisos.RemoveRange(identidad.RolPermisos.Where(x => x.RolId == datos.RolId && x.PermisoId == permiso.Id));
        await identidad.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(Ruta(tipo, datos.Propios[tipo]) + "/adjuntos")).StatusCode);
    }

    private static string Ruta(string tipo, Guid? id) => (tipo switch
    {
        "rq" => "/api/v1/compras/requisiciones", "oc" => "/api/v1/compras/ordenes",
        "factura" => "/api/v1/cuentas-por-pagar/facturas", "propuesta" => "/api/v1/cuentas-por-cobrar/propuestas-aplicacion",
        "deposito" => "/api/v1/tesoreria/depositos",
        "cfdi" => "/api/v1/cuentas-por-pagar/cfdis", "movimiento_tc" => "/api/v1/cuentas-por-pagar/movimientos-tc",
        "nota_cargo" => "/api/v1/cuentas-por-pagar/notas-cargo", "nota_credito" => "/api/v1/cuentas-por-pagar/notas-credito",
        "anticipo" => "/api/v1/cuentas-por-pagar/anticipos", "comprobacion" => "/api/v1/cuentas-por-pagar/comprobaciones",
        "reposicion" => "/api/v1/cuentas-por-pagar/reposiciones-caja", "movimiento" => "/api/v1/tesoreria/movimientos",
        "pago" => "/api/v1/tesoreria/pagos", _ => throw new ArgumentException(tipo),
    }) + (id is null ? "" : $"/{id}");

    private async Task<Datos> PrepararAsync(Action? despuesDeGuardarCompras = null)
    {
        var usuarioId = Guid.CreateVersion7(); var rolId = Guid.CreateVersion7(); var oid = $"p6-scope-{usuarioId:N}";
        var datos = new Datos(factory, usuarioId, rolId);
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                db.Roles.Add(new Rol(rolId, oid, "Operativo P6"));
                foreach (var permiso in await db.Permisos.Where(x => !x.Codigo.EndsWith("todas-sucursales")).ToListAsync())
                    db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permiso.Id));
                db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Operativo P6"));
                db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, null));
                db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, Propia, Empresa));
                await db.SaveChangesAsync();
            }
            datos.Operativo = await LoginAsync(oid, "Operativo P6");
            datos.Corporativo = await LoginAsync("dev-superadmin", "Super Admin Dev");
            foreach (var sucursal in new[] { Propia, Ajena })
            {
                using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var numero = Random.Shared.Next(100000, 999999); var prefix = sucursal == Propia ? "PAA" : "PBB";
                var rq = new Requisicion(Guid.CreateVersion7(), Empresa, Millet.Compras.Domain.Folio.Parse($"{prefix}2026-{numero}"), 2026,
                    Clasificacion.OrdenCompra, sucursal, Guid.Parse("00000005-0004-0000-0000-000000000001"), null, usuarioId, usuarioId, Prioridad.Normal, DateTimeOffset.UtcNow);
                var oc = new OrdenCompra(Guid.CreateVersion7(), Empresa, Millet.Compras.Domain.Oc.Folio.Parse($"OC-{prefix}2026-{numero}"), 2026,
                    Proveedor, sucursal, Guid.NewGuid(), Guid.NewGuid(), usuarioId, usuarioId, DateOnly.FromDateTime(DateTime.UtcNow));
                var mapa = sucursal == Propia ? datos.Propios : datos.Ajenos;
                mapa.Add("rq", rq.Id); mapa.Add("oc", oc.Id);
                compras.AddRange(rq, oc); await compras.SaveChangesAsync();
                despuesDeGuardarCompras?.Invoke();
                var cxp = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>(); var ahora = DateTimeOffset.UtcNow;
                var cfdi = CfdiRecibido.Ingresar(Empresa, UuidCfdi.Parse(Guid.NewGuid().ToString()), RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"),
                    TipoCfdi.Ingreso, $"{prefix}-{numero}", "P6", ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora,
                    "p6-blob-de-prueba", null, "p6-hash-de-prueba");
                cxp.CfdisRecibidos.Add(cfdi);
                mapa.Add("cfdi", cfdi.Id);
                var factura = FacturaProveedor.CapturarSinOc(Empresa, cfdi.Id, cfdi.UuidCfdi.Valor, Proveedor, sucursal, $"{prefix}-{numero}", null,
                    ahora, ahora, DateOnly.FromDateTime(ahora.DateTime).AddDays(30), "MXN", null, 100m, 0m, 16m, 0m, 116m, "Prueba P6", ahora);
                cxp.FacturasProveedor.Add(factura);
                mapa.Add("factura", factura.Id);
                var anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor.Capturar(Empresa, null, Guid.NewGuid().ToString(), Proveedor,
                    Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor.SerieEstandar, null, ahora, "MXN", null, 100m, oc.Id, usuarioId, ahora);
                var notaCredito = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(Empresa, null, Guid.NewGuid().ToString(), Proveedor, null, null,
                    ahora, "MXN", null, 10m, 0m, 0m, 10m, Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.TipoNotaCredito.Descuento,
                    Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), factura.Id, usuarioId, ahora);
                var notaCargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo.Crear(Empresa,
                    Millet.CuentasPorPagar.Domain.NotaCargo.FolioInternoNotaCargo.FromAnioSecuencial(2026, numero), Proveedor, sucursal, "Cargo de prueba", null, 10m, "MXN", null, factura.Id, null, usuarioId, ahora);
                var comprobacion = Millet.CuentasPorPagar.Domain.ComprobacionGastos.ComprobacionGastos.Crear(Empresa,
                    Millet.CuentasPorPagar.Domain.ComprobacionGastos.TipoComprobacionGastos.ReembolsoCajaChica, sucursal, usuarioId,
                    DateOnly.FromDateTime(ahora.DateTime), DateOnly.FromDateTime(ahora.DateTime), "MXN", "Prueba P6", ahora,
                    destinoReposicion: Millet.CuentasPorPagar.Domain.ComprobacionGastos.DestinoReposicionCaja.CuentaSucursal);
                var reposicion = Millet.CuentasPorPagar.Domain.ComprobacionGastos.ReposicionCajaChica.Emitir(Empresa, sucursal,
                    Millet.CuentasPorPagar.Domain.ComprobacionGastos.DestinoReposicionCaja.CuentaSucursal, sucursal, "MXN", 100m, 1, true, usuarioId, ahora);
                var tarjeta = Millet.CuentasPorPagar.Domain.TarjetaCredito.Tarjeta.Crear(Empresa, "Emisora de prueba P6", "AMEX_MX",
                    Millet.CuentasPorPagar.Domain.TarjetaCredito.NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"), $"Tarjeta P6 {prefix}-{numero}", usuarioId, Proveedor,
                    10000m, "MXN", 15, 20, new DateOnly(2026, 1, 1));
                var movimientoTc = Millet.CuentasPorPagar.Domain.TarjetaCredito.MovimientoTarjetaCredito.CapturarCompraConCfdi(Empresa, tarjeta, usuarioId,
                    DateOnly.FromDateTime(ahora.DateTime), 116m, "MXN", null, "Comercio de prueba P6", null, cfdi.Id, factura.Id, Proveedor, "Prueba P6");
                mapa.Add("tarjeta", tarjeta.Id); mapa.Add("movimiento_tc", movimientoTc.Id);
                mapa.Add("anticipo", anticipo.Id); mapa.Add("nota_credito", notaCredito.Id); mapa.Add("nota_cargo", notaCargo.Id);
                mapa.Add("comprobacion", comprobacion.Id); mapa.Add("reposicion", reposicion.Id);
                cxp.AddRange(anticipo, notaCredito, notaCargo, comprobacion, reposicion, tarjeta, movimientoTc); await cxp.SaveChangesAsync();
                var facturacion = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
                var venta = FacturaVenta.CrearBorrador(Empresa, $"{prefix}-{numero}", 1, sucursal, null, null,
                    new DatosFiscalesReceptor("XAXX010101000", "Prueba P6", "616", "97000", "S01", "MEX", true),
                    new DatosFiscalesEmisor("MIL010101AAA", "Prueba P6", "601", "97000"), "PUE", "01", "MXN", null, 2026, 10, 1,
                    ComportamientoFiscal.MostradorInmediato, null, null, null, false);
                mapa.Add("venta", venta.Id);
                facturacion.FacturasVenta.Add(venta); await facturacion.SaveChangesAsync();
                var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
                var cartera = FacturaCartera.Crear(Empresa, venta.Id, datos.ClienteId, "XAXX010101000", "Cliente prueba", Guid.NewGuid().ToString(), $"{prefix}-{numero}", 116m, "MXN", "PPD", ahora, ahora.AddDays(30));
                mapa.Add("cartera", cartera.Id);
                cxc.FacturasCartera.Add(cartera); await cxc.SaveChangesAsync();
                var propuesta = PropuestaAplicacionPago.Crear(Empresa, datos.ClienteId, $"{prefix}-{numero}", 116m, "MXN", "Remittance P6",
                    [(cartera.Uuid, cartera.Id, 116m, 1)], 1000m);
                mapa.Add("propuesta", propuesta.Id);
                cxc.PropuestasAplicacionPago.Add(propuesta); await cxc.SaveChangesAsync();
                var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
                var cuenta = new Millet.Tesoreria.Domain.Cuentas.CuentaBancaria(Empresa, "Banco de prueba P6",
                    Random.Shared.NextInt64(100000000000000000, 999999999999999999).ToString(System.Globalization.CultureInfo.InvariantCulture), null, "MXN");
                var movimiento = Millet.Tesoreria.Domain.Movimientos.MovimientoBancario.RegistrarPagoProveedor(Empresa, cuenta, Proveedor, 116m,
                    DateOnly.FromDateTime(ahora.DateTime), "Prueba P6", null, usuarioId, ahora);
                var pago = new Millet.Tesoreria.Domain.Movimientos.AplicacionPagoProveedor(movimiento.Id, factura.Id, Proveedor, 116m, ahora);
                tes.AddRange(cuenta, movimiento, pago);
                var deposito = DepositoConfirmacion.CrearDesdePropuesta(Empresa, propuesta.Id, datos.ClienteId, $"{prefix}-{numero}", 116m, "MXN", "[]");
                var pasivo = new PasivoPendientePago(Empresa, factura.Id, Proveedor, null, 116m, 116m, "MXN", null, DateOnly.FromDateTime(ahora.DateTime).AddDays(30), null, factura.FolioProveedor, ahora);
                mapa.Add("movimiento", movimiento.Id); mapa.Add("pago", pago.Id); mapa.Add("cuenta", cuenta.Id);
                mapa.Add("deposito", deposito.Id); mapa.Add("pasivo", pasivo.Id);
                tes.AddRange(deposito, pasivo); await tes.SaveChangesAsync();
            }
            return datos;
        }
        catch
        {
            // Una preparación fallida también debe limpiar lo ya persistido.
            await datos.DisposeAsync();
            throw;
        }
    }
    private async Task<HttpClient> LoginAsync(string oid, string nombre)
    {
        var client = factory.CreateClientWithIdempotency();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = oid == "dev-superadmin" ? "superadmin@dev.local" : $"{oid}@test.local", Nombre = nombre });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
    private sealed class Datos(WebApplicationFactory<Program> factory, Guid usuarioId, Guid rolId) : IAsyncDisposable
    {
        private HttpClient? operativo;
        private HttpClient? corporativo;
        public HttpClient Operativo { get => operativo ?? throw new InvalidOperationException("Falta iniciar sesión operativa del fixture P6."); set => operativo = value; }
        public HttpClient Corporativo { get => corporativo ?? throw new InvalidOperationException("Falta iniciar sesión corporativa del fixture P6."); set => corporativo = value; }
        public Guid UsuarioId => usuarioId; public Guid RolId => rolId; public Guid ClienteId { get; } = Guid.NewGuid();
        public Dictionary<string, Guid> Propios { get; } = []; public Dictionary<string, Guid> Ajenos { get; } = [];
        public async ValueTask DisposeAsync()
        {
            using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var mapas = new[] { Propios, Ajenos };
            Guid[] Ids(string tipo) => mapas.Where(x => x.ContainsKey(tipo)).Select(x => x[tipo]).ToArray();
            var depositoIds = Ids("deposito");
            var pasivoIds = Ids("pasivo");
            var propuestaIds = Ids("propuesta");
            var carteraIds = Ids("cartera");
            var ventaIds = Ids("venta");
            var facturaIds = Ids("factura");
            var ocIds = Ids("oc");
            var rqIds = Ids("rq");
            var anticipoIds = Ids("anticipo");
            var nota_creditoIds = Ids("nota_credito");
            var nota_cargoIds = Ids("nota_cargo");
            var comprobacionIds = Ids("comprobacion");
            var reposicionIds = Ids("reposicion");
            var movimientoIds = Ids("movimiento");
            var pagoIds = Ids("pago");
            var cuentaIds = Ids("cuenta");
            var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            await tes.AplicacionesPagoProveedor.Where(x => pagoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await tes.MovimientosBancarios.Where(x => movimientoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await tes.CuentasBancarias.Where(x => cuentaIds.Contains(x.Id)).ExecuteDeleteAsync();
            var cfdiIds = Ids("cfdi"); var tarjetaIds = Ids("tarjeta"); var movimientosTcIds = Ids("movimiento_tc");
            var cxp = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await cxp.MovimientosTarjetaCredito.Where(x => movimientosTcIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.TarjetasCredito.Where(x => tarjetaIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.AnticiposProveedor.Where(x => anticipoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.NotasCreditoProveedor.Where(x => nota_creditoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.NotasCargo.Where(x => nota_cargoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.ComprobacionesGastos.Where(x => comprobacionIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.ReposicionesCajaChica.Where(x => reposicionIds.Contains(x.Id)).ExecuteDeleteAsync();
            await tes.DepositosConfirmacion.Where(x => depositoIds.Contains(x.Id)).ExecuteDeleteAsync();
            await tes.PasivosPendientesPago.Where(x => pasivoIds.Contains(x.Id)).ExecuteDeleteAsync();
            var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
            await cxc.PropuestasAplicacionPago.Where(x => propuestaIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxc.FacturasCartera.Where(x => carteraIds.Contains(x.Id)).ExecuteDeleteAsync();
            var facturacion = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            // FacturaVenta usa TPT: la eliminación rastreada respeta toda la jerarquía.
            facturacion.FacturasVenta.RemoveRange(await facturacion.FacturasVenta.Where(x => ventaIds.Contains(x.Id)).ToListAsync());
            await facturacion.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.Where(x => facturaIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cxp.CfdisRecibidos.Where(x => cfdiIds.Contains(x.Id)).ExecuteDeleteAsync();
            var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            await compras.OrdenesCompra.Where(x => ocIds.Contains(x.Id)).ExecuteDeleteAsync();
            await compras.Requisiciones.Where(x => rqIds.Contains(x.Id)).ExecuteDeleteAsync();
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            await identidad.UsuarioSucursales.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await identidad.UsuarioEmpresaRoles.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await identidad.UsuarioPreferencias.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await identidad.Usuarios.Where(x => x.Id == usuarioId).ExecuteDeleteAsync();
            await identidad.RolPermisos.Where(x => x.RolId == rolId).ExecuteDeleteAsync();
            await identidad.Roles.Where(x => x.Id == rolId).ExecuteDeleteAsync();
            operativo?.Dispose(); corporativo?.Dispose();
        }
    }
}

/// <summary>Host compartido solo por esta suite: el aislamiento de periodos no afecta a las pruebas P8.</summary>
public sealed class P6SucursalEndpointsFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Este fixture mide alcance territorial; el candado contable se prueba en P8.
            services.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            services.AddScoped<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
