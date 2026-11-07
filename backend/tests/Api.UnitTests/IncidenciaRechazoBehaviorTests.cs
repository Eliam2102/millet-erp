using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Api.UnitTests;

/// <summary>U-4 (86e3yzemw): el rechazo con incidencia la abre fuera del handler y la respuesta 422 la nombra.</summary>
public sealed class IncidenciaRechazoBehaviorTests
{
    private static readonly Guid Usuario = Guid.NewGuid();

    private sealed record Peticion : IRequest<int>;

    private sealed class Usuarios(Guid? id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "prueba";
    }

    private sealed class Puerto(bool falla = false) : IIncidenciasPort
    {
        public List<AbrirIncidenciaSolicitud> Abiertas { get; } = [];
        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            if (falla) throw new InvalidOperationException("sin conexión");
            Abiertas.Add(solicitud);
            return Task.FromResult(new IncidenciaAbierta(Guid.NewGuid(), 41, false));
        }
        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static IncidenciaRechazoException Rechazo(Guid responsable) => new("ENTRADA_SIN_ASIGNACION", "El artículo no está asignado.",
        new AbrirIncidenciaSolicitud("almacen", "articulo", Guid.NewGuid(), "Artículo en ubicación R1", PasosIncidencia.AlmUbicacionesAsignacion,
            "El artículo no está asignado.", responsable));

    private static IncidenciaRechazoBehavior<Peticion, int> Behavior(IIncidenciasPort? puerto, Guid? usuario)
    {
        var services = new ServiceCollection();
        if (puerto is not null) services.AddSingleton(puerto);
        return new(services.BuildServiceProvider(), new Usuarios(usuario), NullLogger<IncidenciaRechazoBehavior<Peticion, int>>.Instance);
    }

    [Fact]
    public async Task Abre_la_incidencia_a_cargo_del_usuario_y_conserva_el_rechazo()
    {
        var puerto = new Puerto();
        var rechazo = Rechazo(Guid.Empty);

        var ex = await Assert.ThrowsAsync<IncidenciaRechazoException>(() =>
            Behavior(puerto, Usuario).Handle(new Peticion(), () => throw rechazo, CancellationToken.None));

        Assert.Same(rechazo, ex);
        var abierta = Assert.Single(puerto.Abiertas);
        Assert.Equal(Usuario, abierta.ResponsableUsuarioId);
        Assert.Equal("ENTRADA_SIN_ASIGNACION", abierta.CodigoMotivo);
        Assert.Equal(41, ex.Incidencia!.Numero);
    }

    [Fact]
    public async Task Si_el_puerto_falla_el_usuario_recibe_el_rechazo_original()
    {
        var ex = await Assert.ThrowsAsync<IncidenciaRechazoException>(() =>
            Behavior(new Puerto(falla: true), Usuario).Handle(new Peticion(), () => throw Rechazo(Guid.Empty), CancellationToken.None));

        Assert.Equal("ENTRADA_SIN_ASIGNACION", ex.Code);
        Assert.Null(ex.Incidencia);
    }

    [Fact]
    public async Task Sin_usuario_ni_responsable_no_abre()
    {
        var puerto = new Puerto();
        await Assert.ThrowsAsync<IncidenciaRechazoException>(() =>
            Behavior(puerto, null).Handle(new Peticion(), () => throw Rechazo(Guid.Empty), CancellationToken.None));
        Assert.Empty(puerto.Abiertas);
    }

    [Fact]
    public async Task El_problem_details_422_nombra_la_incidencia_abierta()
    {
        var rechazo = Rechazo(Usuario);
        var id = Guid.NewGuid();
        rechazo.Incidencia = new IncidenciaAbierta(id, 7, false);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, rechazo, CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("ENTRADA_SIN_ASIGNACION", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(id, json.RootElement.GetProperty("incidenciaId").GetGuid());
        Assert.Equal(7, json.RootElement.GetProperty("incidenciaNumero").GetInt64());
    }
}
