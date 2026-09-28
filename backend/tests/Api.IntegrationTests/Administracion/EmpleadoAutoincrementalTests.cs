using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

public class EmpleadoAutoincrementalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SiguienteClaveEndpoint = "/api/v1/admin/empleados/siguiente-clave";
    private const string ColaboradoresEndpoint = "/api/v1/admin/colaboradores";

    private readonly WebApplicationFactory<Program> _factory;

    public EmpleadoAutoincrementalTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SiguienteClave_Retorna_Prefijo_EMP_Y_Formato_Tres_Digitos()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(SiguienteClaveEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var clave = body.GetProperty("siguienteClave").GetString();
        Assert.NotNull(clave);
        Assert.Matches(@"^EMP-\d{3,}$", clave);
    }

    [Fact]
    public async Task Alta_Sin_Clave_Genera_Clave_Autoincremental_Consecutiva()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await ColaboradoresEndpointsTests.CrearOrganizacionAsync(client);

        // Primer empleado sin clave
        var resp1 = await PostAltaEmpleadoAsync(client, org, clave: null);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var clave1 = (await ReadJsonAsync(resp1)).GetProperty("empleado").GetProperty("clave").GetString();
        Assert.NotNull(clave1);
        Assert.Matches(@"^EMP-\d{3,}$", clave1);

        // Segundo empleado sin clave debe ser consecutivo
        var resp2 = await PostAltaEmpleadoAsync(client, org, clave: null);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        var clave2 = (await ReadJsonAsync(resp2)).GetProperty("empleado").GetProperty("clave").GetString();
        Assert.NotNull(clave2);
        Assert.Matches(@"^EMP-\d{3,}$", clave2);

        var num1 = int.Parse(clave1.Replace("EMP-", string.Empty));
        var num2 = int.Parse(clave2.Replace("EMP-", string.Empty));
        Assert.Equal(num1 + 1, num2);
    }

    [Fact]
    public async Task Clave_Duplicada_Retorna_Conflict_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await ColaboradoresEndpointsTests.CrearOrganizacionAsync(client);
        var claveRepetida = $"EMP-DUP-{ColaboradoresEndpointsTests.RandomSufijo()}";

        // Primer registro con la clave
        var resp1 = await PostAltaEmpleadoAsync(client, org, clave: claveRepetida);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        // Segundo intento con la misma clave debe fallar con 409 EMPLEADO_CLAVE_DUPLICADA
        var resp2 = await PostAltaEmpleadoAsync(client, org, clave: claveRepetida);

        await ColaboradoresEndpointsTests.AssertProblemAsync(
            resp2, HttpStatusCode.Conflict, "EMPLEADO_CLAVE_DUPLICADA");
    }

    private static Task<HttpResponseMessage> PostAltaEmpleadoAsync(
        HttpClient client,
        ColaboradoresEndpointsTests.Organizacion org,
        string? clave = null)
        => client.PostAsJsonAsync(ColaboradoresEndpoint, new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = "Colaborador Autoincremental",
            org.SucursalId,
            org.DepartamentoId,
            org.PuestoId,
            Acceso = 0,
            EmailContacto = "contacto.personal@gmail.com",
        });

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await ColaboradoresEndpointsTests.FakeLoginAsync(
            client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
