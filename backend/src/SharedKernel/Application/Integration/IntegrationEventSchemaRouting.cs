namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Enrutamiento canónico de un <see cref="IntegrationEvent"/> hacia el
/// schema Postgres del módulo dueño de su outbox.
///
/// <para>
/// <b>Por qué existe (P9-H7):</b> el <c>IIntegrationEventBuffer</c> es
/// scoped y compartido entre TODOS los DbContext de la request. Antes, el
/// <c>OutboxSaveChangesInterceptor</c> drenaba el buffer COMPLETO en el
/// primer <c>SaveChanges</c> del scope e insertaba las filas en la tabla
/// <c>integration_events_outbox</c> de ese DbContext — aunque el evento
/// perteneciera a otro módulo. Si un handler encolaba un evento del módulo A
/// y otro DbContext B guardaba antes que A, el evento terminaba en el outbox
/// de B (topic equivocado, o peor: un schema sin worker publisher → pérdida
/// total). Caso real: NC de amortización (<c>facturacion.*</c>) escritas en
/// <c>integraciones_fiscal.integration_events_outbox</c> por el SaveChanges
/// del timbrado de la NC siguiente.
/// </para>
/// <para>
/// La clave de ruteo es el <see cref="IntegrationEvent.EventType"/>
/// (convención <c>módulo.recurso.acción.vN</c>, ver CLAUDE.md). El
/// <b>match es por prefijo más largo primero</b> para desambiguar
/// <c>integraciones.aw.</c> de <c>integraciones.fiscal.</c>, que comparten
/// el primer segmento.
/// </para>
/// <para>
/// Los prefijos <c>admin.</c> e <c>identidad.</c> NO se listan a propósito:
/// esos módulos no tienen outbox propio (ni worker publisher) todavía. Sus
/// eventos quedan "sin ruta" y el interceptor los trata con el fallback
/// legacy (los absorbe el primer SaveChanges de la request). Ver
/// PLATFORM-TODO(&lt;OutboxRutaAdminIdentidad&gt;) en el interceptor.
/// </para>
/// </summary>
public static class IntegrationEventSchemaRouting
{
    // Prefijo de EventType → schema Postgres dueño del outbox del módulo.
    // Cada schema debe coincidir con el HasDefaultSchema del DbContext dueño
    // (que es de donde el interceptor obtiene la clave al drenar).
    private static readonly (string Prefix, string Schema)[] Rutas =
        new (string, string)[]
        {
            ("admin.", "compartido"),
            ("integraciones.aw.", "integraciones_aw"),
            ("integraciones.fiscal.", "integraciones_fiscal"),
            ("compras.", "compras"),
            ("almacen.", "almacen"),
            ("cuentas_por_pagar.", "cuentas_por_pagar"),
            ("cuentas_por_cobrar.", "cuentas_por_cobrar"),
            ("facturacion.", "facturacion"),
            ("tesoreria.", "tesoreria"),
        }
        // Prefijo más largo primero: garantiza que "integraciones.aw." gane
        // frente a cualquier prefijo más corto que pudiera introducirse.
        .OrderByDescending(r => r.Item1.Length)
        .ToArray();

    /// <summary>
    /// Resuelve el schema del módulo dueño del outbox para
    /// <paramref name="eventType"/>. Devuelve <c>false</c> si el evento no
    /// mapea a ningún módulo con outbox propio (p. ej. <c>admin.*</c>,
    /// <c>identidad.*</c>, o eventos de prueba <c>test.*</c>).
    /// </summary>
    public static bool TryResolveSchema(string eventType, out string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        foreach (var (prefix, s) in Rutas)
        {
            if (eventType.StartsWith(prefix, StringComparison.Ordinal))
            {
                schema = s;
                return true;
            }
        }

        schema = string.Empty;
        return false;
    }
}
