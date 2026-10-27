using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Domain;

public sealed partial class Activo
{
    public DateOnly? CorteCarga { get; private set; }
    public decimal AcumuladaCarga { get; private set; }
    public DateOnly? FechaBaja { get; private set; }
    public void CargarLibro(DateOnly corte, decimal acumulada)
    {
        // El saldo inicial SAP admite custodia incompleta; no es una capitalización nueva.
        if (Estado != EstadoActivo.PorCapitalizar || EnTraslado || InicioUso == null || ValorOriginal <= 0
            || (OrdenCompraId != null && FacturaProveedorId == null))
            throw new BusinessRuleException("AF_CARGA_INVALIDA", "Complete inicio de uso y valor antes de cargar el saldo inicial.");
        if (corte < InicioUso || acumulada < 0 || acumulada > ValorActual)
            throw new BusinessRuleException("AF_CARGA_INVALIDA", "Revise corte y depreciación acumulada.");
        CorteCarga = corte; AcumuladaCarga = acumulada; DepreciacionAcumulada = acumulada; Estado = EstadoActivo.Activo;
    }
    public void Depreciar(decimal importe)
    {
        if (Estado != EstadoActivo.Activo || DepreciacionAcumulada + importe < 0 || DepreciacionAcumulada + importe > ValorActual)
            throw new BusinessRuleException("AF_DEPRECIACION_INVALIDA", "La acumulada debe estar entre cero y el valor del activo.");
        DepreciacionAcumulada += importe;
    }
    public void Revalorizar(decimal importe)
    {
        if (Estado != EstadoActivo.Activo || ValorActual + importe < DepreciacionAcumulada)
            throw new BusinessRuleException("AF_REVALORIZACION_INVALIDA", "La revalorización no puede dejar valor neto negativo.");
        ValorActual += importe;
    }
    public void DarDeBaja(DateOnly fecha)
    {
        if (Estado != EstadoActivo.Activo || fecha < InicioUso || EnTraslado)
            throw new BusinessRuleException("AF_BAJA_INVALIDA", "El activo debe estar vigente y sin traslado pendiente.");
        Estado = EstadoActivo.DadoDeBaja; FechaBaja = fecha;
    }
    public string? MotivoBaja { get; private set; }
    /// <summary>Baja sin capitalizar porque se anuló la recepción que lo originó (idempotente; la fila se conserva).</summary>
    public void DarDeBajaPorRecepcionAnulada(DateOnly fecha, string motivo)
    {
        if (Estado == EstadoActivo.DadoDeBaja) return;
        if (Estado != EstadoActivo.PorCapitalizar || FacturaProveedorId != null || PolizaAltaId != null || EnTraslado)
            throw new BusinessRuleException("AF_BAJA_INVALIDA", "Solo se anula un activo por capitalizar, sin factura conciliada ni traslado pendiente.");
        Estado = EstadoActivo.DadoDeBaja; FechaBaja = fecha; MotivoBaja = motivo;
    }
    public void Reintegrar() { Estado = EstadoActivo.Activo; FechaBaja = null; }
}

public sealed class CorridaDepreciacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public DateOnly Periodo { get; set; }
    public int Revision { get; set; }
    public Guid? PolizaId { get; set; }
    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public List<DetalleDepreciacion> Detalles { get; set; } = [];
    public CorridaDepreciacion() : base(Guid.CreateVersion7()) { }
}
/// <summary>Renglón inmutable de una corrida; la auditoría vive en <see cref="CorridaDepreciacion"/> (ADR-0008).</summary>
public sealed class DetalleDepreciacion : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public Guid EmpresaId { get; set; }
    public Guid CorridaId { get; set; }
    public Guid ActivoId { get; set; }
    public string Numero { get; set; } = "";
    public Guid ClaseId { get; set; }
    public Guid SucursalId { get; set; }
    public Guid CentroCostoId { get; set; }
    public Guid? EquipoId { get; set; }
    public string Clase { get; set; } = "";
    public string Sucursal { get; set; } = "";
    public string CentroCosto { get; set; } = "";
    public string? Equipo { get; set; }
    public decimal Base { get; set; }
    public decimal? Tasa { get; set; }
    public string? Observacion { get; set; }
    public decimal Importe { get; set; }
    public decimal AcumuladaAntes { get; set; }
    public decimal AcumuladaDespues => AcumuladaAntes + Importe;
    public DetalleDepreciacion() : base(Guid.CreateVersion7()) { }
}
public sealed class OperacionLibro : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; set; }
    public string Tipo { get; set; } = "";
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Motivo { get; set; } = "";
    public string SolicitadoPor { get; set; } = "";
    public string? Evidencia { get; set; }
    public Guid RegistradoPor { get; set; }
    public Guid? AutorizadoPor { get; set; }
    public Guid? PolizaId { get; set; }
    public Guid? FacturaId { get; set; }
    public bool Anulada { get; set; }
    public OperacionLibro() : base(Guid.CreateVersion7()) { }
    // El catálogo se exige al capturar; las bajas históricas conservan su motivo original.
    public void ValidarSolicitudBaja()
    {
        if (string.IsNullOrWhiteSpace(SolicitadoPor) || SolicitadoPor.Length > 200)
            throw new BusinessRuleException("AF_SOLICITANTE_REQUERIDO", "Capture el área solicitante (máximo 200 caracteres).");
        if (!Enum.TryParse<Millet.Facturacion.Domain.Activos.MotivoDisposicionActivo>(Motivo, out var motivo)
            || !Enum.IsDefined(motivo) || Motivo != motivo.ToString())
            throw new BusinessRuleException("AF_MOTIVO_REQUERIDO", "Seleccione siniestro, obsolescencia, premio o rifa, donación o venta.");
    }
    public void Validar(Guid autorizador, bool revalorizacion)
    {
        if (Tipo == "Baja" && (string.IsNullOrWhiteSpace(SolicitadoPor) || SolicitadoPor.Length > 200))
            throw new BusinessRuleException("AF_SOLICITANTE_REQUERIDO", "Capture el área solicitante (máximo 200 caracteres).");
        if (autorizador == RegistradoPor || autorizador == Guid.Empty)
            throw new BusinessRuleException("AF_AUTORIZADOR_DISTINTO", Tipo == "Baja"
                ? "Debe autorizar el Director de Administración y Finanzas, distinto del Contador General que registra."
                : "Debe autorizar otra persona.");
        if (string.IsNullOrWhiteSpace(Motivo) || (Tipo == "Baja" && string.IsNullOrWhiteSpace(Evidencia)))
            throw new BusinessRuleException("AF_EVIDENCIA_REQUERIDA", "Capture motivo y adjunto para la baja.");
        if (Tipo == "Revalorizacion" && !revalorizacion)
            throw new BusinessRuleException("AF_REVALORIZACION_APAGADA", "La revalorización está deshabilitada para esta empresa.");
    }
}
public sealed class ConfiguracionLibro : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public bool DepreciarMismoMes { get; set; }
    public bool InicioDepreciacionConfirmado { get; set; }
    public bool RevalorizacionHabilitada { get; set; }
    public Guid? CuentaUtilidadId { get; set; }
    public Guid? CuentaPerdidaId { get; set; }
    public Guid? CuentaVentaPuenteId { get; set; }
    public Guid? CuentaRevalorizacionId { get; set; }
    public ConfiguracionLibro() : base(Guid.CreateVersion7()) { }
}
public sealed class CargaLibro : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Huella { get; set; } = "";
    public string VersionArchivo { get; set; } = "";
    public DateOnly Corte { get; set; }
    public string CuadreJson { get; set; } = "";
    public CargaLibro() : base(Guid.CreateVersion7()) { }
}

public static class CalculoDepreciacion
{
    public static DateOnly PrimerMes(Activo a, bool mismoMes) => a.CorteCarga is DateOnly corte
        ? Mes(corte).AddMonths(1) : Mes(a.InicioUso!.Value).AddMonths(mismoMes ? 0 : 1);
    public static DateOnly Mes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);
    public static DateOnly Cierre(DateOnly fecha) => Mes(fecha).AddMonths(1).AddDays(-1);
    public static DetalleDepreciacion Calcular(Activo a, ClaseActivo clase, DateOnly periodo, decimal revalorizacion = 0, bool mismoMes = false)
    {
        var mes = Mes(periodo);
        var movimientos = a.Historial.Where(x => Mes(x.Fecha) < mes).Sum(x => x.Importe);
        var @base = a.ValorOriginal + movimientos + revalorizacion;
        var importe = clase.Depreciable && clase.TasaFiscalAnual is decimal tasa && a.Estado == EstadoActivo.Activo && a.InicioUso != null && PrimerMes(a, mismoMes) <= mes
            && (a.CorteCarga == null || mes > a.CorteCarga)
            ? Math.Min(a.ValorEnLibros, Math.Max(0, decimal.Round(@base * tasa / 1200m, 2, MidpointRounding.AwayFromZero))) : 0;
        // Reconstruye la custodia al cierre, incluso si hay traslados posteriores.
        var custodia = CustodiaAFecha(a, Cierre(mes));
        return new() { EmpresaId = a.EmpresaId, ActivoId = a.Id, Numero = a.Numero, ClaseId = a.ClaseId,
            SucursalId = custodia.SucursalId, CentroCostoId = custodia.CentroCostoId, EquipoId = custodia.EquipoId,
            Observacion = !clase.Depreciable ? "No depreciable" : clase.TasaFiscalAnual == null ? "Tasa por confirmar: capture la tasa" : !clase.TasaConfirmada ? "Tasa por confirmar con Millet" : null,
            Clase = clase.Clave, Base = @base, Tasa = clase.TasaFiscalAnual, Importe = importe, AcumuladaAntes = a.DepreciacionAcumulada };
    }
    public static CustodiaActivo CustodiaAFecha(Activo a, DateOnly corte)
    {
        var custodia = a.Custodia();
        var zona = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");
        foreach (var t in a.Traslados.Where(x => x.AceptadoEn != null).OrderByDescending(x => x.AceptadoEn))
            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.AceptadoEn!.Value, zona).DateTime) > corte) custodia = t.Origen;
        return custodia;
    }

}
