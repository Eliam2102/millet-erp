using System.Globalization;
using Millet.DatosMaestros.Application.Clientes;

namespace Millet.Integraciones.Aw.Application.Clientes;

/// <summary>Snapshot listo para aplicar, o motivo por el que la fila es inválida (error por fila).</summary>
public sealed record AwClienteMapeoResultado(AplicarClienteAwSnapshot? Snapshot, string? Error)
{
    public bool EsValido => Snapshot is not null;
}

/// <summary>
/// Mapper puro fila A+W → <see cref="AplicarClienteAwSnapshot"/> (doc 05 §4). Nunca deriva
/// <c>Rfc</c> de UST_ID/STEUERNUMMER (solo candidatos) ni aplica PLZ como CP fiscal; la
/// condición se resuelve solo con exactamente 1 coincidencia; la moneda se normaliza solo
/// si está en <paramref name="mapeoMoneda"/>.
/// </summary>
public static class AwClienteSnapshotMapper
{
    public const string VersionContrato = "1";
    public const string VersionMapeo = "0-borrador";

    public static AwClienteMapeoResultado Mapear(
        AwClienteOrigenFila f,
        IReadOnlyDictionary<string, string> mapeoMoneda,
        DateTime leidoEnUtc)
    {
        var referencia = f.Id.ToString(CultureInfo.InvariantCulture);
        var razonSocial = Limpiar(f.Name1);
        if (razonSocial is null)
            return new(null, $"NAME1 vacío (referencia {referencia}).");

        var unica = f.CondicionCoincidencias.Count == 1 ? f.CondicionCoincidencias[0] : null;

        var monedaCruda = Limpiar(f.Waehrung);
        string? monedaNormalizada = null;
        if (monedaCruda is not null && mapeoMoneda.TryGetValue(monedaCruda, out var iso))
            monedaNormalizada = Limpiar(iso);

        var nombreComercial = string.Join(' ',
            new[] { Limpiar(f.Name1), Limpiar(f.Name2), Limpiar(f.Name3) }.OfType<string>());

        var snapshot = new AplicarClienteAwSnapshot(
            referencia, razonSocial, leidoEnUtc, VersionContrato, VersionMapeo,
            MonedaDefault: monedaNormalizada,
            Telefono: Limpiar(f.Tlf1),
            Email: Limpiar(f.Mail),
            MandantOrigen: f.Mandant,
            NombreComercialOrigen: nombreComercial,
            DomicilioCalle: Limpiar(f.Strasse),
            DomicilioCiudad: Limpiar(f.Ort),
            DomicilioCp: Limpiar(f.Plz),
            DomicilioProvincia: Limpiar(f.Provinz),
            DomicilioPais: Limpiar(f.Land),
            CandidatoFiscalUstId: Limpiar(f.UstId),
            CandidatoFiscalSteuernummer: Limpiar(f.Steuernummer),
            Telefono2Origen: Limpiar(f.Tlf2),
            CondicionCodigoOrigen: Limpiar(f.Zahlbed),
            CondicionNumeroOrigen: unica?.Numero,
            DiasNominalesOrigen: unica?.DiasNominales,
            MonedaCodigoOrigen: monedaCruda,
            MonedaNormalizada: monedaNormalizada,
            CreditoReferenciaLimite: f.KreditLimit,
            CreditoReferenciaLimite1: f.KreditLimit1,
            CreditoReferenciaNet: f.KreditLimitNet,
            EstadoOrigenCrudo: f.KzStatus,
            BloqueoOrigenCrudo: f.KzGesperrt,
            FechaOrigen: f.Datum,
            // ponytail: zona horaria de A+W sin confirmar; se marca Utc solo para Npgsql, es dato de diagnóstico.
            TransaccionOrigenUtc: f.TransactionTime is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null);

        return new(snapshot, null);
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
