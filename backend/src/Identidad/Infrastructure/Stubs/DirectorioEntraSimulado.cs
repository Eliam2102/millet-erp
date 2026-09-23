using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Infrastructure.Stubs;

/// <summary>
/// Directorio de Entra ID simulado en memoria (plan 15, F2; ADR-0015).
/// Es el wiring por defecto mientras no haya permisos de Graph: permite
/// recorrer el alta unificada completa (buscar, crear, vincular OID)
/// sin tocar el tenant.
///
/// <list type="bullet">
///   <item>Arranca con <c>Entra:Simulacion:CuentasExistentes</c>. Su OID
///         se deriva del correo, así que es el mismo entre reinicios.</item>
///   <item>Las cuentas creadas viven solo mientras el proceso está
///         arriba; al reiniciar desaparecen (el ERP conserva el OID).</item>
///   <item>Reproduce las reglas que aplicará Graph: UPN único, dominio
///         permitido, idempotencia por clave.</item>
/// </list>
///
/// Singleton: el estado se comparte entre requests.
/// </summary>
public sealed class DirectorioEntraSimulado : IEntraDirectorioPort
{
    // PLATFORM-TODO(<EntraDirectorio>): reemplazar por el adaptador de
    // Microsoft Graph cuando TI entregue User.ReadWrite.All (plan 15 §8).

    private const string CaracteresMayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string CaracteresMinusculas = "abcdefghijkmnopqrstuvwxyz";
    private const string CaracteresDigitos = "23456789";
    private const string CaracteresSimbolos = "!@#$%*?";

    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;
    private readonly ConcurrentDictionary<string, CuentaEntra> _cuentasPorUpn =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _upnPorClaveIdempotencia = new();
    private readonly object _lockCreacion = new();

    public DirectorioEntraSimulado(IOptionsMonitor<EntraDirectorioOptions> options)
    {
        _options = options;
        foreach (var semilla in options.CurrentValue.Simulacion.CuentasExistentes)
        {
            if (string.IsNullOrWhiteSpace(semilla.Correo)) continue;
            var upn = semilla.Correo.Trim();
            _cuentasPorUpn[upn] = new CuentaEntra(
                string.IsNullOrWhiteSpace(semilla.ObjectId) ? OidEstable(upn) : semilla.ObjectId,
                upn,
                string.IsNullOrWhiteSpace(semilla.Nombre) ? upn : semilla.Nombre,
                semilla.Habilitada);
        }
    }

    public Task<CuentaEntra?> BuscarPorCorreoAsync(string correo, CancellationToken ct)
    {
        _cuentasPorUpn.TryGetValue(correo.Trim(), out var cuenta);
        return Task.FromResult(cuenta);
    }

    public Task<CuentaEntraCreada> CrearCuentaAsync(SolicitudCuentaEntra solicitud, CancellationToken ct)
    {
        var upn = solicitud.Upn.Trim();
        if (!_options.CurrentValue.PermiteCorreo(upn))
        {
            throw new BusinessRuleException(
                "ENTRA_DOMINIO_NO_PERMITIDO",
                $"El dominio de '{upn}' no está permitido para cuentas corporativas.");
        }

        CuentaEntra cuenta;
        lock (_lockCreacion)
        {
            if (_upnPorClaveIdempotencia.TryGetValue(solicitud.ClaveIdempotencia, out var upnPrevio))
            {
                // Reintento: misma cuenta, contraseña nueva (como "Reenviar acceso").
                cuenta = _cuentasPorUpn[upnPrevio];
            }
            else
            {
                if (_cuentasPorUpn.ContainsKey(upn))
                {
                    throw new ConflictException(
                        "ENTRA_UPN_EN_USO",
                        $"Ya existe una cuenta Microsoft con el correo '{upn}'.");
                }

                cuenta = new CuentaEntra(
                    Guid.NewGuid().ToString(), upn, solicitud.NombreMostrado.Trim(), Habilitada: true);
                _cuentasPorUpn[upn] = cuenta;
                _upnPorClaveIdempotencia[solicitud.ClaveIdempotencia] = upn;
            }
        }

        return Task.FromResult(new CuentaEntraCreada(cuenta, GenerarContrasenaTemporal()));
    }

    public Task<string> RestablecerContrasenaTemporalAsync(string objectId, CancellationToken ct)
    {
        if (!_cuentasPorUpn.Values.Any(c => c.ObjectId == objectId))
        {
            throw new EntityNotFoundException(
                "ENTRA_CUENTA_NO_ENCONTRADA",
                $"No existe una cuenta Microsoft con el OID '{objectId}'.");
        }
        return Task.FromResult(GenerarContrasenaTemporal());
    }

    /// <summary>
    /// OID con forma de GUID derivado del correo: estable entre reinicios y
    /// nunca "pendiente" según <c>Usuario.EsOidPendiente</c>.
    /// </summary>
    public static string OidEstable(string correo)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(correo.Trim().ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    /// <summary>
    /// 16 caracteres con mayúsculas, minúsculas, dígitos y símbolos: cumple
    /// la política de complejidad por defecto de Entra.
    /// </summary>
    public static string GenerarContrasenaTemporal()
    {
        const string todos = CaracteresMayusculas + CaracteresMinusculas + CaracteresDigitos + CaracteresSimbolos;
        var caracteres = new char[16];
        caracteres[0] = Elegir(CaracteresMayusculas);
        caracteres[1] = Elegir(CaracteresMinusculas);
        caracteres[2] = Elegir(CaracteresDigitos);
        caracteres[3] = Elegir(CaracteresSimbolos);
        for (var i = 4; i < caracteres.Length; i++) caracteres[i] = Elegir(todos);
        RandomNumberGenerator.Shuffle(caracteres.AsSpan());
        return new string(caracteres);

        static char Elegir(string fuente) => fuente[RandomNumberGenerator.GetInt32(fuente.Length)];
    }
}
