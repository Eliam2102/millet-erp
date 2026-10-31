using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.Conciliaciones.Emparejamiento;

/// <summary>
/// Motor de emparejamiento automático en 3 pasos (§2 / T9.2 / P9.2):
/// Paso 1: Referencia exacta + importe exacto.
/// Paso 2: Importe exacto + fecha exacta.
/// Paso 3: Tolerancia configurable (±$1 en ventana de 3 días).
/// Invariante: si una línea tiene más de una opción candidata, NO se confirma sola (va a revisión).
/// </summary>
public static class MotorEmparejamiento
{
    public static ResultadoEmparejamiento Ejecutar(
        IReadOnlyList<ExtractoLinea> lineas,
        IReadOnlyList<MovimientoBancario> movimientos,
        EmparejamientoOptions options,
        Guid usuarioId,
        DateTimeOffset ahora)
    {
        // Pool de movimientos no conciliados disponibles para emparejar
        var disponibles = movimientos
            .Where(m => m.EstadoConciliacion == EstadoConciliacionMovimiento.NoConciliado)
            .ToList();

        var matchedMovIds = new HashSet<Guid>();
        int emp1 = 0, emp2 = 0, emp3 = 0;

        var lineasAProcesar = lineas
            .Where(l => l.Estado == EstadoExtractoLinea.Pendiente || l.Estado == EstadoExtractoLinea.EnRevision)
            .OrderBy(l => l.NumeroLinea)
            .ToList();

        // --------------------------------------------------------------------
        // PASO 1: Referencia exacta + importe exacto
        // --------------------------------------------------------------------
        if (options.HabilitarPaso1ReferenciaExacta)
        {
            foreach (var linea in lineasAProcesar.Where(l => l.Estado == EstadoExtractoLinea.Pendiente))
            {
                if (string.IsNullOrWhiteSpace(linea.Referencia))
                    continue;

                string refNorm = linea.Referencia.Trim().ToUpperInvariant();

                var candidatos = disponibles
                    .Where(m => !matchedMovIds.Contains(m.Id)
                                && m.Sentido == linea.Sentido
                                && m.Monto == linea.Monto
                                && !string.IsNullOrWhiteSpace(m.ReferenciaBancaria)
                                && string.Equals(m.ReferenciaBancaria.Trim(), refNorm, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (candidatos.Count == 1)
                {
                    var mov = candidatos[0];
                    linea.Emparejar(mov.Id, MatchTipo.AutoReferenciaImporte, linea.Monto, usuarioId, ahora);
                    mov.MarcarConciliado();
                    matchedMovIds.Add(mov.Id);
                    emp1++;
                }
                else if (candidatos.Count > 1)
                {
                    // Ambigüedad: va a revisión con los candidatos encontrados
                    linea.MarcarEnRevision(candidatos.Select(c => c.Id));
                }
            }
        }

        // --------------------------------------------------------------------
        // PASO 2: Importe exacto y fecha exacta
        // --------------------------------------------------------------------
        if (options.HabilitarPaso2ImporteFecha)
        {
            foreach (var linea in lineasAProcesar.Where(l => l.Estado == EstadoExtractoLinea.Pendiente))
            {
                var candidatos = disponibles
                    .Where(m => !matchedMovIds.Contains(m.Id)
                                && m.Sentido == linea.Sentido
                                && m.Monto == linea.Monto
                                && m.FechaValor == linea.Fecha)
                    .ToList();

                if (candidatos.Count == 1)
                {
                    var mov = candidatos[0];
                    linea.Emparejar(mov.Id, MatchTipo.AutoImporteFecha, linea.Monto, usuarioId, ahora);
                    mov.MarcarConciliado();
                    matchedMovIds.Add(mov.Id);
                    emp2++;
                }
                else if (candidatos.Count > 1)
                {
                    // Ambigüedad: va a revisión
                    linea.MarcarEnRevision(candidatos.Select(c => c.Id));
                }
            }
        }

        // --------------------------------------------------------------------
        // PASO 3: Tolerancia configurable (±$1 en ventana de 3 días)
        // --------------------------------------------------------------------
        if (options.HabilitarPaso3Tolerancia)
        {
            foreach (var linea in lineasAProcesar.Where(l => l.Estado == EstadoExtractoLinea.Pendiente))
            {
                var candidatos = disponibles
                    .Where(m => !matchedMovIds.Contains(m.Id)
                                && m.Sentido == linea.Sentido
                                && Math.Abs(m.Monto - linea.Monto) <= options.ToleranciaImporte
                                && Math.Abs(m.FechaValor.DayNumber - linea.Fecha.DayNumber) <= options.VentanaDias)
                    .ToList();

                if (candidatos.Count == 1)
                {
                    var mov = candidatos[0];
                    linea.Emparejar(mov.Id, MatchTipo.AutoTolerancia, mov.Monto, usuarioId, ahora);
                    mov.MarcarConciliado();
                    matchedMovIds.Add(mov.Id);
                    emp3++;
                }
                else if (candidatos.Count > 1)
                {
                    // Ambigüedad: va a revisión
                    linea.MarcarEnRevision(candidatos.Select(c => c.Id));
                }
            }
        }

        int enRev = lineasAProcesar.Count(l => l.Estado == EstadoExtractoLinea.EnRevision);
        int pend = lineasAProcesar.Count(l => l.Estado == EstadoExtractoLinea.Pendiente);

        return new ResultadoEmparejamiento(
            lineasAProcesar.Count,
            emp1,
            emp2,
            emp3,
            enRev,
            pend);
    }
}
