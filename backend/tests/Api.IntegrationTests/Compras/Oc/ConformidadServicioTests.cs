using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// 86e3tacbn: conformidad del servicio por OC (solo el departamento solicitante, con permiso; deshacer con motivo).
public partial class OrdenesCompraEndpointsTests
{
    private static readonly Guid DeptoServicio = Guid.Parse("00000004-0001-0000-0000-000000000001");

    private async Task<(Guid OcId, Guid UsuarioId, HttpClient Client)> SembrarOcServicioAsync(Guid? departamentoUsuario)
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false);
        var usuarioId = await UsuarioIdDeAsync(client);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
        db.Entry(oc.Lineas.Single()).Property(l => l.EsServicio).CurrentValue = true;
        db.Entry(oc.Lineas.Single()).Property(l => l.DepartamentoSolicitanteId).CurrentValue = DeptoServicio;
        await db.SaveChangesAsync();
        await AsignarDepartamentoAsync(usuarioId, departamentoUsuario);
        return (ocId, usuarioId, client);
    }

    private async Task AsignarDepartamentoAsync(Guid usuarioId, Guid? departamentoId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        (await db.Usuarios.SingleAsync(u => u.Id == usuarioId)).AsignarDepartamento(departamentoId);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Servicio_departamento_solicitante_confirma_y_deshace_con_motivo()
    {
        var (ocId, usuarioId, client) = await SembrarOcServicioAsync(DeptoServicio);
        try
        {
            var ok = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/confirmar-servicio", new { Comentario = "Entregado en sitio" });
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
            var detalle = await DetalleOcAsync(client, ocId);
            Assert.NotEqual(System.Text.Json.JsonValueKind.Null, detalle.GetProperty("servicioConfirmadoEn").ValueKind);
            Assert.Equal("Entregado en sitio", detalle.GetProperty("servicioConfirmadoComentario").GetString());

            var sinMotivo = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/deshacer-confirmacion-servicio", new { Motivo = "" });
            Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);

            var deshecha = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/deshacer-confirmacion-servicio", new { Motivo = "Falta el informe" });
            Assert.Equal(HttpStatusCode.NoContent, deshecha.StatusCode);
            detalle = await DetalleOcAsync(client, ocId);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, detalle.GetProperty("servicioConfirmadoEn").ValueKind);
            Assert.Equal("Falta el informe", detalle.GetProperty("servicioConfirmacionRevocadaMotivo").GetString());
        }
        finally { await AsignarDepartamentoAsync(usuarioId, null); }
    }

    [Fact]
    public async Task Servicio_usuario_de_otro_departamento_recibe_403_y_no_confirma()
    {
        var (ocId, usuarioId, client) = await SembrarOcServicioAsync(null);
        try
        {
            var response = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/confirmar-servicio", new { Comentario = (string?)null });
            await AssertCodigoP2(response, HttpStatusCode.Forbidden, "OC_SERVICIO_DEPARTAMENTO_NO_SOLICITANTE");
            Assert.Equal(System.Text.Json.JsonValueKind.Null, (await DetalleOcAsync(client, ocId)).GetProperty("servicioConfirmadoEn").ValueKind);
        }
        finally { await AsignarDepartamentoAsync(usuarioId, null); }
    }

    [Fact]
    public async Task Servicio_sin_permiso_403_y_con_pago_no_se_puede_deshacer()
    {
        var (ocId, usuarioId, client) = await SembrarOcServicioAsync(DeptoServicio);
        var lector = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);
        var lectorId = await UsuarioIdDeAsync(lector);
        try
        {
            var sinPermiso = await lector.PostAsJsonAsync($"{EndpointBase}/{ocId}/confirmar-servicio", new { Comentario = "x" });
            Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);

            (await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/confirmar-servicio", new { Comentario = "ok" })).EnsureSuccessStatusCode();
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
            {
                var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
                oc.RegistrarPago(10m, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
            var rechazo = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/deshacer-confirmacion-servicio", new { Motivo = "Error" });
            await AssertCodigoP2(rechazo, HttpStatusCode.UnprocessableEntity, "OC_SERVICIO_CONFIRMACION_CON_PAGO");
        }
        finally
        {
            await AsignarDepartamentoAsync(usuarioId, null);
            await LimpiarUsuarioAsync(lectorId);
        }
    }
}
