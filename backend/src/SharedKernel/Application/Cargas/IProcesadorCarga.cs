namespace Millet.SharedKernel.Application.Cargas;

/// <summary>
/// M-047 (86e3jvvmf): puerto que implementa el módulo dueño de cada tipo de carga (p. ej. Contabilidad para
/// <c>contabilidad.saldos-iniciales</c>, M-166). El lote común no conoce las reglas del módulo: le pasa las filas
/// ya mapeadas y guarda lo que le regresa. El módulo nunca lee las tablas del lote.
/// <para>Vive en SharedKernel para que cada módulo (Contabilidad, etc.) lo implemente sin referenciar Compartido.</para>
/// </summary>
public interface IProcesadorCarga
{
    /// <summary>Clave del tipo de carga, p. ej. <c>contabilidad.saldos-iniciales</c>.</summary>
    string Tipo { get; }
    /// <summary>Nombre para la pantalla, p. ej. «Saldos iniciales contables».</summary>
    string Nombre { get; }
    /// <summary>Columnas que el usuario mapea desde su archivo (M-167).</summary>
    IReadOnlyList<ColumnaLoteCarga> Columnas { get; }
    /// <summary>Columnas sin las que no se puede validar.</summary>
    IReadOnlyList<string> ColumnasObligatorias { get; }
    /// <summary>El tipo pide fecha de corte al subir.</summary>
    bool RequiereFechaCorte { get; }
    /// <summary>Permiso para subir, validar y aplicar.</summary>
    string PermisoSubir { get; }
    /// <summary>Permiso para autorizar o rechazar (otra persona).</summary>
    string PermisoAutorizar { get; }

    /// <summary>
    /// Parámetros del tipo que se capturan al subir (p. ej. sucursal por omisión y tipo de póliza en los
    /// saldos iniciales). Sin parámetros por omisión.
    /// </summary>
    Task<IReadOnlyList<ParametroCarga>> ParametrosAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParametroCarga>>([]);

    Task<ValidacionCarga> ValidarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken);
    Task<AplicacionCarga> AplicarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken);
}

/// <summary>
/// Lo que el lote le pasa al módulo. Lleva los nombres de quien subió y de quien autorizó (el módulo no lee
/// usuarios) y los parámetros capturados al subir.
/// </summary>
/// <summary>Columna que el usuario mapea desde su archivo; <see cref="Alias"/> sirve para sugerir el mapeo.</summary>
public sealed record ColumnaLoteCarga(string Nombre, string Ayuda, params string[] Alias);

public sealed record LoteCargaContexto(
    Guid LoteId, Guid EmpresaId, string Huella, DateOnly? FechaCorte, Guid SubidoPorId, Guid? AutorizadoPorId,
    string SubidoPorNombre, string? AutorizadoPorNombre, IReadOnlyDictionary<string, string?> Parametros);

/// <summary>Parámetro del tipo de carga; con <see cref="Opciones"/> la pantalla muestra un selector.</summary>
public sealed record ParametroCarga(string Nombre, string Etiqueta, string Ayuda, bool Obligatorio, string? PorOmision,
    IReadOnlyList<OpcionParametroCarga>? Opciones = null);

public sealed record OpcionParametroCarga(string Valor, string Etiqueta);

/// <summary>Fila del archivo ya mapeada a las columnas del tipo; <see cref="Fila"/> es el renglón real del archivo.</summary>
public sealed record FilaCarga(int Fila, IReadOnlyDictionary<string, string> Valores);

/// <summary>Error de validación; <see cref="Fila"/> null = del archivo completo.</summary>
public sealed record ErrorCarga(int? Fila, string Campo, string Codigo, string Mensaje);

public sealed record ValidacionCarga(bool PuedeAplicar, IReadOnlyList<ErrorCarga> Errores, IReadOnlyDictionary<string, string> Resumen);

public sealed record AplicacionCarga(string Referencia, string? Folio, bool YaAplicada, IReadOnlyDictionary<string, string> Resumen);
