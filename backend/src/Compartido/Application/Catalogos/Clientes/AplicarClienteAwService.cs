using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

// Comparación de RFC normalizado traducible por EF a SQL.
#pragma warning disable CA1304, CA1311, CA1862
namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Lectura de un cliente A+W ya normalizada por el lector (ADM-06, doc
/// integration/05). Los campos "de alta" (RazonSocial…Domicilio extranjero)
/// solo se usan al crear; nunca sobrescriben un cliente existente. Condición
/// y moneda llegan ya resueltas (o null = sin coincidencia / desconocida).
/// </summary>
public sealed record AplicarClienteAwSnapshot(
    string ReferenciaExterna,
    string RazonSocial,
    DateTime LeidoEnUtc,
    string VersionContrato,
    string VersionMapeo,
    // Fiscales: al crear, y en un cliente existente solo si el local está vacío.
    string? Rfc = null,
    string? CodigoPostalFiscal = null,
    // Solo alta.
    string? UsoCfdiDefault = null,
    string? FormaPagoDefault = null,
    string? MetodoPagoDefault = null,
    string? MonedaDefault = null,
    string? NumRegIdTrib = null,
    string? PaisResidencia = null,
    string? DomicilioExtranjeroCalle = null,
    string? DomicilioExtranjeroEstado = null,
    string? DomicilioExtranjeroCodigoPostal = null,
    // Comercial: se aplica al cliente solo si el local está vacío.
    string? Telefono = null,
    string? Email = null,
    // Registro de origen (solo registra).
    int? MandantOrigen = null,
    string? NombreComercialOrigen = null,
    string? DomicilioCalle = null,
    string? DomicilioCiudad = null,
    string? DomicilioCp = null,
    string? DomicilioProvincia = null,
    string? DomicilioPais = null,
    string? CandidatoFiscalUstId = null,
    string? CandidatoFiscalSteuernummer = null,
    string? Telefono2Origen = null,
    string? CondicionCodigoOrigen = null,
    int? CondicionNumeroOrigen = null,
    int? DiasNominalesOrigen = null,
    string? MonedaCodigoOrigen = null,
    string? MonedaNormalizada = null,
    decimal? CreditoReferenciaLimite = null,
    decimal? CreditoReferenciaLimite1 = null,
    double? CreditoReferenciaNet = null,
    int? EstadoOrigenCrudo = null,
    int? BloqueoOrigenCrudo = null,
    DateOnly? FechaOrigen = null,
    DateTime? TransaccionOrigenUtc = null,
    Guid? EjecucionId = null,
    // Auto-provisión de pedidos: si el cliente ya existe no se toca nada.
    bool SoloCrear = false);

/// <summary><c>NoCreado</c>: alta omitida por moneda sin equivalencia (Cliente = null).</summary>
public enum AplicarClienteAwAccion { Creado, Actualizado, SinCambios, Conflicto, NoCreado }

public sealed record AplicarClienteAwResultado(
    Cliente? Cliente,
    AplicarClienteAwAccion Accion,
    ResultadoSincronizacionAw Resultado);

/// <summary>
/// Algoritmo único de aplicación de un cliente A+W (sincronización y
/// auto-provisión de pedidos): buscar por <c>ReferenciaExterna</c> → crear
/// Cliente + registro de origen en una sola transacción, o actualizar el
/// registro (+ Telefono/Email/Rfc/CP fiscal solo si vacíos). Nunca sobrescribe
/// un Rfc o CP fiscal ya capturado, ni toca Régimen, RazonSocial, LineaCredito
/// La clasificación y los datos del ERP se conservan; V52 solo se aplica con mapeo aprobado.
/// </summary>
public sealed class AplicarClienteAwService
{
    /// <summary>Versión de la normalización previa al hash; subirla cambia todos los hashes.</summary>
    public const string VersionNormalizacionHash = "n2";

    private const string PgUniqueViolation = "23505";
    private const string IxReferenciaExterna = "ix_clientes_referencia_externa";
    private const string IxClave = "ix_clientes_clave";

    private readonly CompartidoDbContext _db;
    private readonly MapeoEstatusClientesAwOptions _mapeoEstatus;

    public AplicarClienteAwService(CompartidoDbContext db, IOptions<MapeoEstatusClientesAwOptions>? mapeoEstatus = null)
    {
        _db = db;
        _mapeoEstatus = mapeoEstatus?.Value ?? new();
        if (!_mapeoEstatus.EsValido)
            throw new OptionsValidationException(MapeoEstatusClientesAwOptions.SectionName,
                typeof(MapeoEstatusClientesAwOptions), ["El mapeo de estatus de clientes A+W requiere una versión de 1 a 8 caracteres alfanuméricos, guion o guion bajo."]);
    }

    public async Task<AplicarClienteAwResultado> AplicarAsync(
        AplicarClienteAwSnapshot snap, CancellationToken ct)
    {
        _ = VersionMapeoEfectiva(snap);
        await using var scope = await ClienteRfcScope.AbrirAsync(_db, Clave(snap), ct);
        var resultado = await AplicarCoreAsync(snap, ct);
        await scope.ConfirmarAsync(ct);
        return resultado;
    }

    private async Task<AplicarClienteAwResultado> AplicarCoreAsync(AplicarClienteAwSnapshot snap, CancellationToken ct)
    {
        // Máx. 2 vueltas: la segunda solo ocurre si perdimos la carrera de alta.
        var reintentoConcurrencia = false;
        for (var intento = 0; ; intento++)
        {
            var cliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.ReferenciaExterna == snap.ReferenciaExterna, ct);
            if (cliente is not null)
            {
                try
                {
                    return await ActualizarAsync(cliente, snap, ct);
                }
                catch (DbUpdateConcurrencyException ex) when (!reintentoConcurrencia)
                {
                    // Otro escritor actualizó el mismo registro: soltar lo rastreado, releer y reevaluar (un solo reintento).
                    reintentoConcurrencia = true;
                    foreach (var e in ex.Entries) e.State = EntityState.Detached;
                    DesacoplarCambios();
                    _db.Entry(cliente).State = EntityState.Detached;
                    intento--; // no consume el límite de la carrera de alta
                    continue;
                }
            }

            // Moneda sin equivalencia validada: no se inventa MXN; no se crea hasta resolver el mapeo (doc 05 §13).
            // La auto-provisión de pedidos (SoloCrear) no trae moneda de origen y conserva su default.
            if (!snap.SoloCrear && snap.MonedaDefault is null)
                return new(null, AplicarClienteAwAccion.NoCreado, ResultadoSincronizacionAw.Pendiente);

            // Clave de ligado = RFC o UST_ID normalizado (M2.2). Solo contra clientes manuales: el ligado entre clientes de A+W está diferido hasta aclarar con Millet (ver aclaracion-rfc-repetido.md).
            var rfc = Clave(snap);
            if (rfc is not null && await _db.Clientes.AnyAsync(c => c.Origen == OrigenMaster.Manual && c.Rfc != null && c.Rfc.Trim().ToUpper() == rfc, ct))
            {
                var conflicto = await _db.Set<ClienteLigadoAw>().SingleOrDefaultAsync(c => c.ReferenciaExterna == snap.ReferenciaExterna, ct);
                if (conflicto is null) _db.Add(new ClienteLigadoAw(Guid.CreateVersion7(), snap.ReferenciaExterna, rfc, System.Text.Json.JsonSerializer.Serialize(snap)));
                else if (conflicto.RevisadoEn is null) conflicto.Actualizar(rfc, System.Text.Json.JsonSerializer.Serialize(snap));
                await _db.SaveChangesAsync(ct);
                return new(null, AplicarClienteAwAccion.Conflicto, ResultadoSincronizacionAw.Conflicto);
            }
            // Sin coincidencia vigente: un conflicto pendiente de esta referencia quedó obsoleto (p. ej. el manual cambió su RFC).
            var obsoleto = await _db.Set<ClienteLigadoAw>().SingleOrDefaultAsync(l => l.ReferenciaExterna == snap.ReferenciaExterna && l.RevisadoEn == null, ct);
            if (obsoleto is not null) _db.Remove(obsoleto);
            // Nunca dos clientes con el mismo campo RFC (también en modo demo, donde A+W sí copia el RFC). Si otro cliente ya tiene ese RFC se crea sin él (el ligado entre clientes de A+W está diferido); no cambia el hash ni el registro de origen.
            var rfcCampo = RfcCliente.Normalizar(snap.Rfc);
            var rfcOcupado = RfcCliente.EsLigable(rfcCampo) && await _db.Clientes.AnyAsync(c => c.Rfc != null && c.Rfc.Trim().ToUpper() == rfcCampo, ct);
            // Misma clave fiscal en otro cliente de A+W: se crea igual (no se fusionan) y queda un conflicto visible (ADM-06).
            var ocupantes = rfc is null ? [] : await ClientesPorRfc.BuscarAsync(_db, [rfc], null, ct);
            var nuevo = CrearCliente(rfcOcupado ? snap with { Rfc = null } : snap);
            AplicarEstatus(nuevo, snap);
            var registro = new ClienteSincronizacionAw(Guid.CreateVersion7(), nuevo.Id, snap.ReferenciaExterna);
            var hash = CalcularHash(snap);
            if (ocupantes.Count > 0)
            {
                var otros = string.Join(", ", ocupantes.Select(o => o.Cliente.Clave));
                var causa = $"Dos clientes de A+W con la misma clave fiscal {rfc}: {snap.ReferenciaExterna} y {otros}. No se fusionan.";
                _db.Add(new CambioMaestroAw(Guid.CreateVersion7(), "Cliente", nuevo.Id, snap.ReferenciaExterna,
                    "RFC repetido entre clientes de A+W", otros, rfc, MatrizCampoDuenoAw.DuenoAw, TipoCambioMaestroAw.RfcCompartido,
                    EstadoCambioMaestroAw.Pendiente, causa.Length <= 500 ? causa : causa[..500], hash, MatrizCampoDuenoAw.Version,
                    snap.EjecucionId, DateTime.UtcNow));
            }
            var resultado = ResultadoDe(snap);
            AplicarRegistro(registro, snap, hash, resultado);
            _db.Clientes.Add(nuevo);
            _db.Set<ClienteSincronizacionAw>().Add(registro);
            try
            {
                await _db.SaveChangesAsync(ct);
                return new(nuevo, AplicarClienteAwAccion.Creado, resultado);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation } pg)
            {
                _db.Entry(nuevo).State = EntityState.Detached;
                _db.Entry(registro).State = EntityState.Detached;
                DesacoplarCambios();
                // La clave AW-{ref} es determinista: en la carrera Postgres puede reportar IxClave;
                // si ya existe un cliente con esa referencia es la misma carrera, no un duplicado real.
                if (pg.ConstraintName == IxClave
                    && (intento > 0 || !await _db.Clientes.AsNoTracking()
                        .AnyAsync(c => c.ReferenciaExterna == snap.ReferenciaExterna, ct)))
                    throw new BusinessRuleException("CLIENTE_CLAVE_DUPLICADA",
                        $"La clave '{nuevo.Clave}' ya existe en otro cliente; no se creó el cliente A+W {snap.ReferenciaExterna}.");
                if ((pg.ConstraintName != IxReferenciaExterna && pg.ConstraintName != IxClave) || intento > 0)
                    throw;
                // Carrera de alta por la misma referencia: recargar al ganador y aplicar como actualización.
            }
        }
    }

    private async Task<AplicarClienteAwResultado> ActualizarAsync(
        Cliente cliente, AplicarClienteAwSnapshot snap, CancellationToken ct)
    {
        // Un cliente manual con la misma referencia no se apropia (doc 05 §2).
        if (cliente.Origen != OrigenMaster.Aw)
        {
            var rfcManual = RfcCliente.Normalizar(cliente.Rfc);
            if (RfcCliente.EsLigable(rfcManual) && rfcManual == Clave(snap))
            {
                var conflicto = await _db.Set<ClienteLigadoAw>().SingleOrDefaultAsync(c => c.ReferenciaExterna == snap.ReferenciaExterna, ct);
                if (conflicto is null) _db.Add(new ClienteLigadoAw(Guid.CreateVersion7(), snap.ReferenciaExterna, rfcManual, System.Text.Json.JsonSerializer.Serialize(snap)));
                else if (conflicto.RevisadoEn is null) conflicto.Actualizar(rfcManual, System.Text.Json.JsonSerializer.Serialize(snap));
                await _db.SaveChangesAsync(ct);
            }
            return new(cliente, AplicarClienteAwAccion.Conflicto, ResultadoSincronizacionAw.Conflicto);
        }
        if (snap.SoloCrear)
            return new(cliente, AplicarClienteAwAccion.SinCambios, ResultadoSincronizacionAw.SinCambios);

        var registro = await _db.Set<ClienteSincronizacionAw>()
            .FirstOrDefaultAsync(r => r.ClienteId == cliente.Id, ct);
        var hash = CalcularHash(snap);

        // Lectura atrasada (evento fuera de orden): la más reciente ya aplicada es la vigente y no se pisa.
        // Orden = LeidoEnUtc; una versión monotónica de A+W lo reemplazaría (doc 05 §7, PENDIENTE).
        if (registro is not null && snap.LeidoEnUtc < registro.LeidoEnUtc)
            return new(cliente, AplicarClienteAwAccion.SinCambios, registro.Resultado);

        var estatusAntes = cliente.Estatus;
        AplicarEstatus(cliente, snap);
        var cambios = new List<CambioMaestroAw>();
        if (cliente.Estatus != estatusAntes)
        {
            var baja = cliente.Estatus == EstatusCatalogo.Inactivo;
            cambios.Add(NuevoCambio(cliente.Id, snap, hash, "Estatus", estatusAntes.ToString(), cliente.Estatus.ToString(),
                baja ? TipoCambioMaestroAw.Baja : TipoCambioMaestroAw.Reactivacion,
                baja ? $"A+W bloqueó al cliente (mapeo {VersionMapeoEfectiva(snap)})."
                     : "A+W desbloqueó un bloqueo que vino de A+W; se reactiva sin crear otra identidad."));
        }
        if (registro is not null && registro.HashOrigen == hash
            && registro.VersionMapeo == VersionMapeoEfectiva(snap)
            && registro.Resultado != ResultadoSincronizacionAw.Error
            && registro.Resultado == ResultadoDe(snap))
        {
            registro.RegistrarComprobacion(snap.LeidoEnUtc, snap.EjecucionId);
            await AgregarCambiosAsync(cambios, snap.ReferenciaExterna, hash, ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // El latido es best-effort: otro escritor ya actualizó el registro y esta lectura no aplicó nada.
                foreach (var e in ex.Entries) e.State = EntityState.Detached;
                DesacoplarCambios();
            }
            return new(cliente, AplicarClienteAwAccion.SinCambios, registro.Resultado);
        }

        var diferencias = DiferenciaAplicacionAw.Serializar(Diferencias(cliente, snap));
        var telefono = string.IsNullOrWhiteSpace(cliente.Telefono) ? Vacio(snap.Telefono) : null;
        var email = string.IsNullOrWhiteSpace(cliente.Email) ? Vacio(snap.Email) : null;
        var rfc = string.IsNullOrWhiteSpace(cliente.Rfc) ? Vacio(snap.Rfc) : null;
        // Nunca dos clientes con el mismo RFC no genérico: no se completa un RFC que ya ocupa otro cliente.
        if (rfc is not null && RfcCliente.EsLigable(rfc) && await _db.Clientes.AnyAsync(c => c.Id != cliente.Id && c.Rfc != null && c.Rfc.Trim().ToUpper() == RfcCliente.Normalizar(rfc), ct)) rfc = null;
        var cpFiscal = string.IsNullOrWhiteSpace(cliente.CodigoPostalFiscal) ? Vacio(snap.CodigoPostalFiscal) : null;
        if (telefono is not null || email is not null || rfc is not null || cpFiscal is not null)
            cliente.ActualizarDatos(rfc: rfc, codigoPostalFiscal: cpFiscal, telefono: telefono, email: email);

        var resultado = ResultadoDe(snap);
        if (registro is null)
        {
            registro = new ClienteSincronizacionAw(Guid.CreateVersion7(), cliente.Id, snap.ReferenciaExterna);
            _db.Set<ClienteSincronizacionAw>().Add(registro);
        }
        if (registro.HashOrigen.Length > 0) cambios.AddRange(CambiosDeOrigen(cliente.Id, registro, snap, hash));
        AplicarRegistro(registro, snap, hash, resultado, diferencias);
        await AgregarCambiosAsync(cambios, snap.ReferenciaExterna, hash, ct);
        await _db.SaveChangesAsync(ct);
        return new(cliente, AplicarClienteAwAccion.Actualizado, resultado);
    }

    private static CambioMaestroAw NuevoCambio(Guid clienteId, AplicarClienteAwSnapshot snap, string hash, string campo,
        string? antes, string? despues, TipoCambioMaestroAw tipo, string causa) =>
        new(Guid.CreateVersion7(), "Cliente", clienteId, snap.ReferenciaExterna, campo, antes, despues,
            MatrizCampoDuenoAw.DuenoAw, tipo, EstadoCambioMaestroAw.Informativo, causa, hash,
            MatrizCampoDuenoAw.Version, snap.EjecucionId, DateTime.UtcNow);

    /// <summary>Un cambio por campo de A+W que difiere del registro de origen previo (comparación ordinal, vacío = nulo).</summary>
    private static List<CambioMaestroAw> CambiosDeOrigen(Guid clienteId, ClienteSincronizacionAw antes, AplicarClienteAwSnapshot s, string hash)
    {
        var lista = new List<CambioMaestroAw>();
        void Cmp(string campo, string? a, string? d)
        {
            a = Vacio(a); d = Vacio(d);
            if (!string.Equals(a, d, StringComparison.Ordinal))
                lista.Add(NuevoCambio(clienteId, s, hash, campo, a, d, TipoCambioMaestroAw.Aplicado, "Cambio recibido de A+W."));
        }
        static string? Domicilio(params string?[] partes) => string.Join(", ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        Cmp("Nombre de origen", antes.NombreComercialOrigen, s.NombreComercialOrigen);
        Cmp("Domicilio de origen",
            Domicilio(antes.DomicilioOrigenCalle, antes.DomicilioOrigenCiudad, antes.DomicilioOrigenCp, antes.DomicilioOrigenProvincia, antes.DomicilioOrigenPais),
            Domicilio(s.DomicilioCalle, s.DomicilioCiudad, s.DomicilioCp, s.DomicilioProvincia, s.DomicilioPais));
        Cmp("Condición de pago", antes.CondicionCodigoOrigen, s.CondicionCodigoOrigen);
        Cmp("Moneda de origen", antes.MonedaCodigoOrigen, s.MonedaCodigoOrigen);
        Cmp("Límite de crédito de referencia", antes.CreditoReferenciaLimite?.ToString("0.00", CultureInfo.InvariantCulture),
            s.CreditoReferenciaLimite?.ToString("0.00", CultureInfo.InvariantCulture));
        Cmp("Bloqueo A+W", antes.BloqueoOrigenCrudo?.ToString(CultureInfo.InvariantCulture), s.BloqueoOrigenCrudo?.ToString(CultureInfo.InvariantCulture));
        Cmp("Estado A+W", antes.EstadoOrigenCrudo?.ToString(CultureInfo.InvariantCulture), s.EstadoOrigenCrudo?.ToString(CultureInfo.InvariantCulture));
        return lista;
    }

    /// <summary>Idempotencia: una sola consulta trae lo ya registrado para esta referencia y versión de evento.</summary>
    private async Task AgregarCambiosAsync(List<CambioMaestroAw> cambios, string referencia, string hash, CancellationToken ct)
    {
        if (cambios.Count == 0) return;
        var existentes = await _db.CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.TipoEntidad == "Cliente" && c.ReferenciaExterna == referencia && c.VersionEvento == hash)
            .Select(c => new { c.Campo, c.Tipo }).ToListAsync(ct);
        foreach (var c in cambios.Where(c => !existentes.Any(e => e.Campo == c.Campo && e.Tipo == c.Tipo)))
            _db.Add(c);
    }

    /// <summary>Tras un choque de concurrencia las filas de bitácora pendientes se sueltan para no insertarlas dos veces.</summary>
    private void DesacoplarCambios()
    {
        foreach (var e in _db.ChangeTracker.Entries<CambioMaestroAw>().Where(e => e.State == EntityState.Added).ToList())
            e.State = EntityState.Detached;
    }

    private static Cliente CrearCliente(AplicarClienteAwSnapshot s)
    {
        // Clave determinista desde la referencia A+W: legible, única y ≤20 chars.
        var clave = $"AW-{s.ReferenciaExterna}";
        return new Cliente(
            id: Guid.CreateVersion7(),
            clave: clave.Length <= 20 ? clave : clave[..20],
            razonSocial: s.RazonSocial,
            origen: OrigenMaster.Aw,
            referenciaExterna: s.ReferenciaExterna,
            rfc: s.Rfc,
            codigoPostalFiscal: s.CodigoPostalFiscal,
            usoCfdiDefault: s.UsoCfdiDefault,
            formaPagoDefault: s.FormaPagoDefault,
            metodoPagoDefault: s.MetodoPagoDefault,
            monedaDefault: s.MonedaDefault ?? "MXN",
            email: Vacio(s.Email),
            telefono: s.Telefono,
            numRegIdTrib: s.NumRegIdTrib,
            paisResidencia: s.PaisResidencia,
            domicilioExtranjeroCalle: s.DomicilioExtranjeroCalle,
            domicilioExtranjeroEstado: s.DomicilioExtranjeroEstado,
            domicilioExtranjeroCodigoPostal: s.DomicilioExtranjeroCodigoPostal);
    }

    private string VersionMapeoEfectiva(AplicarClienteAwSnapshot snap)
    {
        // Se compone solo al persistir el registro; el snapshot del conflicto
        // conserva su versión de origen para poder revisarlo sin añadirla dos veces.
        var version = _mapeoEstatus.Habilitado ? $"{snap.VersionMapeo}|{_mapeoEstatus.Version}" : snap.VersionMapeo;
        if (version.Length > 20)
            throw new BusinessRuleException("CLIENTE_SYNC_VERSION_INVALIDA", "La versión del mapeo de clientes y estatus excede 20 caracteres.");
        return version;
    }

    private void AplicarRegistro(
        ClienteSincronizacionAw r, AplicarClienteAwSnapshot s, string hash, ResultadoSincronizacionAw resultado,
        string? diferencias = null) =>
        r.Aplicar(
            s.MandantOrigen, s.NombreComercialOrigen,
            s.DomicilioCalle, s.DomicilioCiudad, s.DomicilioCp, s.DomicilioProvincia, s.DomicilioPais,
            s.CandidatoFiscalUstId, s.CandidatoFiscalSteuernummer, s.Telefono2Origen,
            s.CondicionCodigoOrigen, s.CondicionNumeroOrigen, s.DiasNominalesOrigen,
            s.MonedaCodigoOrigen, s.MonedaNormalizada,
            s.CreditoReferenciaLimite, s.CreditoReferenciaLimite1, s.CreditoReferenciaNet,
            s.EstadoOrigenCrudo, s.BloqueoOrigenCrudo, s.FechaOrigen, s.TransaccionOrigenUtc,
            s.EjecucionId, s.LeidoEnUtc, DateTime.UtcNow,
            hash, s.VersionContrato, VersionMapeoEfectiva(s), resultado, diferencias: diferencias);

    /// <summary>Solo aplica señales de bloqueo/desbloqueo con equivalencia aprobada.</summary>
    private void AplicarEstatus(Cliente cliente, AplicarClienteAwSnapshot snap)
    {
        if (_mapeoEstatus.Resolver(snap.EstadoOrigenCrudo, snap.BloqueoOrigenCrudo) is { } bloqueado)
            cliente.AplicarBloqueoAw(bloqueado);
    }

    /// <summary>Moneda, condición o códigos de estado sin equivalencia quedan pendientes.</summary>
    private ResultadoSincronizacionAw ResultadoDe(AplicarClienteAwSnapshot s) =>
        (s.MonedaCodigoOrigen is not null && s.MonedaNormalizada is null)
        || (s.CondicionCodigoOrigen is not null && s.CondicionNumeroOrigen is null)
        || _mapeoEstatus.TieneCodigoSinMapeo(s.EstadoOrigenCrudo, s.BloqueoOrigenCrudo)
            ? ResultadoSincronizacionAw.Pendiente
            : ResultadoSincronizacionAw.Aplicado;

    /// <summary>Lo recibido que la política deja fuera del cliente existente (se ve en el detalle de origen).</summary>
    private static List<DiferenciaAplicacionAw> Diferencias(Cliente c, AplicarClienteAwSnapshot s)
    {
        var d = new List<DiferenciaAplicacionAw>();
        void Dif(string campo, string? recibido, string? local, string motivo)
        {
            recibido = Vacio(recibido);
            local = Vacio(local);
            if (recibido is not null && local is not null
                && !string.Equals(recibido, local, StringComparison.OrdinalIgnoreCase))
                d.Add(new(campo, recibido, local, motivo));
        }
        Dif("Razón social", s.RazonSocial, c.RazonSocial, "Se conserva la razón social local.");
        Dif("Moneda", s.MonedaDefault, c.MonedaDefault, "Se conserva la moneda del cliente existente.");
        Dif("RFC", s.Rfc, c.Rfc, "Solo se completa si el local está vacío.");
        Dif("CP fiscal", s.CodigoPostalFiscal, c.CodigoPostalFiscal, "Solo se completa si el local está vacío.");
        Dif("Teléfono", s.Telefono, c.Telefono, "Solo se completa si el local está vacío.");
        Dif("Correo", s.Email, c.Email, "Solo se completa si el local está vacío.");
        return d;
    }

    /// <summary>Clave de ligado del snapshot: RFC, o UST_ID si el RFC no sirve (M2.2).</summary>
    private static string? Clave(AplicarClienteAwSnapshot s) => RfcCliente.ClaveLigado(s.Rfc) ?? RfcCliente.ClaveLigado(s.CandidatoFiscalUstId);

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>
    /// SHA-256 (hex) de los campos consumidos + condición resuelta. Orden fijo,
    /// trim, vacío = nulo, prefijo de longitud (sin ambigüedad entre campos) y
    /// números en cultura invariante. Excluye control (Fecha/Transacción de
    /// origen, ejecución, lectura). Cambiar algo aquí exige subir
    /// <see cref="VersionNormalizacionHash"/> y VersionMapeo.
    /// </summary>
    internal static string CalcularHash(AplicarClienteAwSnapshot s)
    {
        var sb = new StringBuilder(VersionNormalizacionHash);
        void T(string? v)
        {
            v = Vacio(v);
            sb.Append('|').Append(v is null ? "~" : $"{v.Length}:{v}");
        }
        void N(IFormattable? v, string? fmt = null)
            => sb.Append('|').Append(v is null ? "~" : v.ToString(fmt, CultureInfo.InvariantCulture));

        N(s.MandantOrigen);
        T(s.NombreComercialOrigen);
        T(s.DomicilioCalle); T(s.DomicilioCiudad); T(s.DomicilioCp); T(s.DomicilioProvincia); T(s.DomicilioPais);
        T(s.CandidatoFiscalUstId); T(s.CandidatoFiscalSteuernummer);
        T(s.Rfc); T(s.CodigoPostalFiscal);
        T(s.Telefono); T(s.Email); T(s.Telefono2Origen);
        T(s.CondicionCodigoOrigen); N(s.CondicionNumeroOrigen); N(s.DiasNominalesOrigen);
        T(s.MonedaCodigoOrigen); T(s.MonedaNormalizada);
        N(s.CreditoReferenciaLimite is { } l ? Math.Round(l, 4) : null, "0.0000");
        N(s.CreditoReferenciaLimite1 is { } l1 ? Math.Round(l1, 4) : null, "0.0000");
        N(s.CreditoReferenciaNet, "R");
        N(s.EstadoOrigenCrudo); N(s.BloqueoOrigenCrudo);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
