using Millet.DatosMaestros.Application.Cargas;

namespace Millet.Compartido.Application.Cargas.Lotes;

/// <summary>
/// M-047 (86e3jvvmf): puerto que implementa el módulo dueño de cada tipo de carga (p. ej. Contabilidad para
/// <c>contabilidad.saldos-iniciales</c>, M-166). El lote común no conoce las reglas del módulo: le pasa las filas
/// ya mapeadas y guarda lo que le regresa. El módulo nunca lee las tablas del lote.
/// </summary>
public interface IProcesadorCarga
{
    /// <summary>Clave del tipo de carga, p. ej. <c>contabilidad.saldos-iniciales</c>.</summary>
    string Tipo { get; }
    /// <summary>Nombre para la pantalla, p. ej. «Saldos iniciales contables».</summary>
    string Nombre { get; }
    /// <summary>Columnas que el usuario mapea desde su archivo (M-167).</summary>
    IReadOnlyList<ColumnaCarga> Columnas { get; }
    /// <summary>Columnas sin las que no se puede validar.</summary>
    IReadOnlyList<string> ColumnasObligatorias { get; }
    /// <summary>El tipo pide fecha de corte al subir.</summary>
    bool RequiereFechaCorte { get; }
    /// <summary>Permiso para subir, validar y aplicar.</summary>
    string PermisoSubir { get; }
    /// <summary>Permiso para autorizar o rechazar (otra persona).</summary>
    string PermisoAutorizar { get; }

    Task<ValidacionCarga> ValidarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken);
    Task<AplicacionCarga> AplicarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken);
}

public sealed record LoteCargaContexto(
    Guid LoteId, Guid EmpresaId, string Huella, DateOnly? FechaCorte, Guid SubidoPorId, Guid? AutorizadoPorId);

/// <summary>Fila del archivo ya mapeada a las columnas del tipo; <see cref="Fila"/> es el renglón real del archivo.</summary>
public sealed record FilaCarga(int Fila, IReadOnlyDictionary<string, string> Valores);

/// <summary>Error de validación; <see cref="Fila"/> null = del archivo completo.</summary>
public sealed record ErrorCarga(int? Fila, string Campo, string Codigo, string Mensaje);

public sealed record ValidacionCarga(bool PuedeAplicar, IReadOnlyList<ErrorCarga> Errores, IReadOnlyDictionary<string, string> Resumen);

public sealed record AplicacionCarga(string Referencia, string? Folio, bool YaAplicada, IReadOnlyDictionary<string, string> Resumen);
