using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

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
    // Solo alta.
    string? Rfc = null,
    string? CodigoPostalFiscal = null,
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
/// registro (+ Telefono/Email solo si vacíos). Nunca toca Rfc, Régimen, CP
/// fiscal, RazonSocial, LineaCredito ni Estatus de un cliente existente.
/// </summary>
public sealed class AplicarClienteAwService
{
    /// <summary>Versión de la normalización previa al hash; subirla cambia todos los hashes.</summary>
    public const string VersionNormalizacionHash = "n1";

    private const string PgUniqueViolation = "23505";
    private const string IxReferenciaExterna = "ix_clientes_referencia_externa";
    private const string IxClave = "ix_clientes_clave";

    private readonly CompartidoDbContext _db;

    public AplicarClienteAwService(CompartidoDbContext db) => _db = db;

    public async Task<AplicarClienteAwResultado> AplicarAsync(
        AplicarClienteAwSnapshot snap, CancellationToken ct)
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
                    _db.Entry(cliente).State = EntityState.Detached;
                    intento--; // no consume el límite de la carrera de alta
                    continue;
                }
            }

            // Moneda sin equivalencia validada: no se inventa MXN; no se crea hasta resolver el mapeo (doc 05 §13).
            // La auto-provisión de pedidos (SoloCrear) no trae moneda de origen y conserva su default.
            if (!snap.SoloCrear && snap.MonedaDefault is null)
                return new(null, AplicarClienteAwAccion.NoCreado, ResultadoSincronizacionAw.Pendiente);

            var nuevo = CrearCliente(snap);
            var registro = new ClienteSincronizacionAw(Guid.CreateVersion7(), nuevo.Id, snap.ReferenciaExterna);
            var hash = CalcularHash(snap);
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
            return new(cliente, AplicarClienteAwAccion.Conflicto, ResultadoSincronizacionAw.Conflicto);
        if (snap.SoloCrear)
            return new(cliente, AplicarClienteAwAccion.SinCambios, ResultadoSincronizacionAw.SinCambios);

        var registro = await _db.Set<ClienteSincronizacionAw>()
            .FirstOrDefaultAsync(r => r.ClienteId == cliente.Id, ct);
        var hash = CalcularHash(snap);

        // Lectura atrasada (evento fuera de orden): la más reciente ya aplicada es la vigente y no se pisa.
        // Orden = LeidoEnUtc; una versión monotónica de A+W lo reemplazaría (doc 05 §7, PENDIENTE).
        if (registro is not null && snap.LeidoEnUtc < registro.LeidoEnUtc)
            return new(cliente, AplicarClienteAwAccion.SinCambios, registro.Resultado);

        if (registro is not null && registro.HashOrigen == hash
            && registro.VersionMapeo == snap.VersionMapeo
            && registro.Resultado != ResultadoSincronizacionAw.Error)
        {
            registro.RegistrarComprobacion(snap.LeidoEnUtc, snap.EjecucionId);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // El latido es best-effort: otro escritor ya actualizó el registro y esta lectura no aplicó nada.
                foreach (var e in ex.Entries) e.State = EntityState.Detached;
            }
            return new(cliente, AplicarClienteAwAccion.SinCambios, registro.Resultado);
        }

        var diferencias = DiferenciaAplicacionAw.Serializar(Diferencias(cliente, snap));
        var telefono = string.IsNullOrWhiteSpace(cliente.Telefono) ? Vacio(snap.Telefono) : null;
        var email = string.IsNullOrWhiteSpace(cliente.Email) ? Vacio(snap.Email) : null;
        if (telefono is not null || email is not null)
            cliente.ActualizarDatos(telefono: telefono, email: email);

        var resultado = ResultadoDe(snap);
        if (registro is null)
        {
            registro = new ClienteSincronizacionAw(Guid.CreateVersion7(), cliente.Id, snap.ReferenciaExterna);
            _db.Set<ClienteSincronizacionAw>().Add(registro);
        }
        AplicarRegistro(registro, snap, hash, resultado, diferencias);
        await _db.SaveChangesAsync(ct);
        return new(cliente, AplicarClienteAwAccion.Actualizado, resultado);
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

    private static void AplicarRegistro(
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
            hash, s.VersionContrato, s.VersionMapeo, resultado, diferencias: diferencias);

    /// <summary>
    /// Pendiente (sin baja/activación/conversión automática) si hay moneda o
    /// condición de origen sin resolver, o un estado crudo sin mapeo aprobado.
    /// </summary>
    private static ResultadoSincronizacionAw ResultadoDe(AplicarClienteAwSnapshot s) =>
        (s.MonedaCodigoOrigen is not null && s.MonedaNormalizada is null)
        || (s.CondicionCodigoOrigen is not null && s.CondicionNumeroOrigen is null)
        || s.EstadoOrigenCrudo is not null
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
        Dif("Teléfono", s.Telefono, c.Telefono, "Solo se completa si el local está vacío.");
        Dif("Correo", s.Email, c.Email, "Solo se completa si el local está vacío.");
        return d;
    }

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
