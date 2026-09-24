using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Millet.Api.Auth.Options;
using Millet.Api.Auth.Provisioning;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>Contrato HTTP real de Graph, sin tocar el tenant ni enviar correos.</summary>
public sealed class GraphColaboradorAdaptersTests
{
    private const string Upn = "persona@millet.mx";
    private const string EmpleadoId = "79f2570f-7760-43a5-ac2c-7041ba673724";
    private const string Oid = "b0b0919c-f777-4a19-b513-57d6c35b4acc";

    [Fact]
    public void Graph_Con_Correo_Local_Permite_Probar_Sin_SenderEmail_Solo_En_Development()
    {
        var configuration = ConfiguracionGraph("SandboxLocal");
        var services = new ServiceCollection();

        services.AddGraphColaboradores(configuration, isDevelopment: true);

        Assert.Contains(services, d => d.ServiceType == typeof(ICorreoSalientePort)
            && d.ImplementationType == typeof(CorreoSandboxLocal));
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddGraphColaboradores(configuration, isDevelopment: false));
    }

    [Fact]
    public void Graph_Con_Correo_Real_Sigue_Exigiendo_SenderEmail()
    {
        var configuration = ConfiguracionGraph("Graph");

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddGraphColaboradores(configuration, isDevelopment: true));
    }

    [Fact]
    public void Graph_Con_Correo_Real_Usa_El_Adaptador_Graph()
    {
        var configuration = ConfiguracionGraph("Graph", senderEmail: "qa@millet.mx");
        var services = new ServiceCollection();

        services.AddGraphColaboradores(configuration, isDevelopment: false);

        Assert.Contains(services, d => d.ServiceType == typeof(ICorreoSalientePort)
            && d.ImplementationType == typeof(GraphCorreoColaboradores));
    }

    [Fact]
    public void Graph_Rechaza_Proveedor_De_Correo_Desconocido()
    {
        var configuration = ConfiguracionGraph("NoOp");

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddGraphColaboradores(configuration, isDevelopment: true));
    }

    [Fact]
    public void Graph_Con_Correo_Local_Rechaza_Smtp_Externo()
    {
        var configuration = ConfiguracionGraph("SandboxLocal", host: "smtp.example.com");

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddGraphColaboradores(configuration, isDevelopment: true));
    }

    private static IConfiguration ConfiguracionGraph(string correoProveedor, string host = "127.0.0.1",
        string? senderEmail = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EntraId:TenantId"] = "tenant-test",
            ["Auth:EntraId:ClientId"] = "client-test",
            ["Auth:EntraId:ClientSecret"] = "secret-test",
            ["Auth:EntraId:SenderEmail"] = senderEmail,
            ["Entra:Correo:Proveedor"] = correoProveedor,
            ["Entra:Simulacion:CorreoSandbox:Host"] = host,
            ["Entra:Simulacion:CorreoSandbox:Puerto"] = "1025",
            ["Entra:DominiosPermitidos:0"] = "millet.mx",
            ["Entra:Provision:Disabled"] = "false",
            ["Entra:UrlInicioSesion"] = "https://erp.example.com"
        }).Build();

    [Fact]
    public async Task Crear_Cuenta_Nueva_Exige_Cambio_Y_Usa_Clave_De_Empleado()
    {
        var handler = new GraphHandler((request, body) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"Request_ResourceNotFound\",\"message\":\"Not found\"}}");
            Assert.Equal(HttpMethod.Post, request.Method);
            using var json = JsonDocument.Parse(body!);
            var root = json.RootElement;
            Assert.Equal(Upn, root.GetProperty("userPrincipalName").GetString());
            Assert.Equal(EmpleadoId, root.GetProperty("employeeId").GetString());
            Assert.True(root.GetProperty("passwordProfile").GetProperty("forceChangePasswordNextSignIn").GetBoolean());
            Assert.True(root.GetProperty("passwordProfile").GetProperty("password").GetString()!.Length >= 16);
            return Json(HttpStatusCode.Created,
                $"{{\"id\":\"{Oid}\",\"userPrincipalName\":\"{Upn}\",\"displayName\":\"Persona\",\"accountEnabled\":true}}");
        });
        var port = Directorio(handler);

        var creada = await port.CrearCuentaAsync(
            new SolicitudCuentaEntra(Upn, "Persona", "contacto@example.com", EmpleadoId), CancellationToken.None);

        Assert.Equal(Oid, creada.Cuenta.ObjectId);
        Assert.Equal(Upn, creada.Cuenta.Upn);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Reintento_Adopta_Solo_Cuenta_Del_Mismo_Empleado_Y_Restablece_Clave()
    {
        var handler = new GraphHandler((request, body) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK,
                    $"{{\"id\":\"{Oid}\",\"userPrincipalName\":\"{Upn}\",\"employeeId\":\"{EmpleadoId}\",\"accountEnabled\":true}}");
            Assert.Equal(HttpMethod.Patch, request.Method);
            using var json = JsonDocument.Parse(body!);
            Assert.True(json.RootElement.GetProperty("passwordProfile")
                .GetProperty("forceChangePasswordNextSignIn").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var creada = await Directorio(handler).CrearCuentaAsync(
            new SolicitudCuentaEntra(Upn, "Persona", "contacto@example.com", EmpleadoId), CancellationToken.None);

        Assert.Equal(Oid, creada.Cuenta.ObjectId);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Cuenta_Ajena_No_Se_Adopta()
    {
        var handler = new GraphHandler((_, _) => Json(HttpStatusCode.OK,
            $"{{\"id\":\"{Oid}\",\"userPrincipalName\":\"{Upn}\",\"employeeId\":\"otro-empleado\"}}"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Directorio(handler).CrearCuentaAsync(
            new SolicitudCuentaEntra(Upn, "Persona", "contacto@example.com", EmpleadoId), CancellationToken.None));

        Assert.Equal("ENTRA_UPN_EN_USO", ex.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Correo_Aceptado_Por_Graph_Incluye_Clave_Y_Destinatario()
    {
        var handler = new GraphHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("sendMail", request.RequestUri!.AbsolutePath);
            Assert.Contains("personal@example.com", body);
            Assert.Contains("ClaveTemporal!789", body);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        await Correo(handler).EnviarAccesoColaboradorAsync(
            new CorreoAccesoColaborador("personal@example.com", "Persona", Upn,
                "ClaveTemporal!789", "https://erp.millet.mx"), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Correo_Rechazado_Propaga_El_Fallo()
    {
        var handler = new GraphHandler((_, _) => Json(HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"Mail.Send missing\"}}"));

        await Assert.ThrowsAnyAsync<Exception>(() => Correo(handler).EnviarAccesoColaboradorAsync(
            new CorreoAccesoColaborador("personal@example.com", "Persona", Upn,
                "ClaveTemporal!789", "https://erp.millet.mx"), CancellationToken.None));
    }

    private static GraphDirectorioColaboradores Directorio(GraphHandler handler) => new(
        Client(handler), new StaticOptionsMonitor<EntraDirectorioOptions>(new EntraDirectorioOptions
        {
            DominiosPermitidos = ["millet.mx"]
        }));

    private static GraphCorreoColaboradores Correo(GraphHandler handler) => new(
        Client(handler), Options.Create(new EntraIdOptions { SenderEmail = "erp@millet.mx" }));

    private static GraphServiceClient Client(GraphHandler handler) => new(
        new HttpClient(handler), new FakeCredential(), ["https://graph.microsoft.com/.default"]);

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class GraphHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request, body);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
            ValueTask.FromResult(GetToken(context, ct));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
