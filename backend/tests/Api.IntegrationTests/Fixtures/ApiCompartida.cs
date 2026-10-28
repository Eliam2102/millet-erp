using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests;

/// <summary>
/// Un solo arranque del host para la suite: la base ya es compartida y las pruebas
/// se ejecutan en serie mediante DisableTestParallelization.
/// Todo hijo de WithWebHostBuilder (también mediante helpers) debe cerrarse con
/// using/await using al terminar la prueba; su cliente se usa con using.
/// Si el hijo pertenece a un fixture de clase, ese fixture debe desecharlo al
/// terminar la clase. Desechar sólo el cliente no cierra el host hijo.
/// El padre compartido se desecha únicamente al terminar la colección.
/// </summary>
[CollectionDefinition(Nombre)]
public sealed class ApiCompartida : ICollectionFixture<WebApplicationFactory<Program>>
{
    public const string Nombre = "ApiCompartida";
}
