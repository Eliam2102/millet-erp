namespace Millet.Contabilidad.Application.Cierre;

// CON-12 (86e3jvvme): puertos de CONSUMIDOR con los que el checklist de cierre pregunta a cada módulo dueño si su parte del mes está
// hecha. Los implementan Tesorería, Almacén, Activos Fijos y CxP en su Infrastructure/PublicAdapters y los cablea Millet.Api; ninguno
// escribe ni lee tablas de otro módulo.

/// <summary>Cuenta bancaria cuyo mes no está conciliado: sin conciliación, abierta o con movimientos sin conciliar.</summary>
public sealed record CuentaBancariaSinConciliar(Guid CuentaBancariaId, string Cuenta, string Motivo, int MovimientosSinConciliar);

/// <summary>Tesorería (T9.2): cuentas activas con movimientos o saldo en el mes cuya conciliación del mes no está cerrada.</summary>
public interface IConciliacionBancariaCierreReadPort
{
    Task<IReadOnlyList<CuentaBancariaSinConciliar>> PendientesAsync(int anio, int mes, CancellationToken ct);
}

/// <summary>Almacén (cierre mensual de inventario): ¿el mes ya está cerrado?</summary>
public interface IInventarioCierreReadPort
{
    Task<bool> MesCerradoAsync(int anio, int mes, CancellationToken ct);
}

/// <summary>Activos Fijos (AF11.4): si hay activos que deprecian en el mes y si la corrida del mes está aplicada (no anulada).</summary>
public sealed record EstadoDepreciacionMes(bool HayActivosDepreciables, bool Ejecutada);

public interface IDepreciacionCierreReadPort
{
    Task<EstadoDepreciacionMes> EstadoAsync(int anio, int mes, CancellationToken ct);
}

/// <summary>CxP (CXP-12): si ya se corrió la revaluación del mes y cuántas facturas en moneda extranjera tenían saldo al cierre.</summary>
public sealed record EstadoRevaluacionProveedores(bool Ejecutada, int FacturasMonedaExtranjera);

public interface IRevaluacionProveedoresCierreReadPort
{
    Task<EstadoRevaluacionProveedores> EstadoAsync(int anio, int mes, CancellationToken ct);
}
