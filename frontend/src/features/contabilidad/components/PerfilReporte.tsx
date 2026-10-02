import { Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type { Perfil } from '../api/types';
import { perfilSinContenido } from '../lib/archivo';
import { etiquetaDato, nombreColumna, severidadLegible, tituloHallazgo } from '../lib/textos';

function Escalares({ titulo, datos }: { titulo: string; datos: Record<string, unknown> }) {
  const filas = Object.entries(datos)
    .filter(([k, v]) => (typeof v === 'number' || typeof v === 'string' || typeof v === 'boolean') && etiquetaDato(k) !== null);
  if (filas.length === 0) return null;
  return (
    <section aria-label={titulo} className="space-y-1">
      <h3 className="text-sm font-semibold">{titulo}</h3>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        {filas.map(([k, v]) => (
          <div key={k}><dt className="text-xs text-ink-muted">{etiquetaDato(k)}</dt><dd>{typeof v === 'boolean' ? (v ? 'Sí' : 'No') : String(v)}</dd></div>
        ))}
      </dl>
    </section>
  );
}

function descargar(perfil: Perfil, desplazamiento: number) {
  const url = URL.createObjectURL(new Blob([perfilSinContenido(perfil, desplazamiento)], { type: 'application/json' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = 'perfilado-catalogo.json';
  a.click();
  URL.revokeObjectURL(url);
}

/** Reporte de perfilado (solo lectura): errores agrupados por código, «qué se reabre» y conteos. */
export function PerfilReporte({ perfil, desplazamiento = 0 }: { perfil: Perfil; desplazamiento?: number }) {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-ink-muted">
          Reporte de solo lectura: no se guardó nada. La descarga incluye únicamente conteos y números de fila (sin contenido de cuentas).
        </p>
        <Button variant="outline" size="sm" onClick={() => descargar(perfil, desplazamiento)}>
          <Download className="mr-1 size-4" aria-hidden="true" />Descargar reporte
        </Button>
      </div>

      <Escalares titulo="Resumen" datos={perfil.resumen} />
      <Escalares titulo="Estructura" datos={perfil.estructura} />
      <Escalares titulo="Pendientes de validación" datos={perfil.pendientesValidacion} />

      <section aria-label="Errores y avisos" className="space-y-1">
        <h3 className="text-sm font-semibold">Errores y avisos encontrados</h3>
        {perfil.porCodigoError.length === 0 ? (
          <p className="text-sm text-ink-muted">Sin errores ni avisos por fila.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <tr><th className="py-1">Hallazgo</th><th>Tipo</th><th>Filas</th><th>Ejemplos (fila · columna)</th></tr>
            </thead>
            <tbody>
              {perfil.porCodigoError.map((e) => (
                <tr key={`${e.codigo}-${e.severidad}`} className="border-t border-line-row">
                  <td className="py-1" title={e.codigo}>{tituloHallazgo(e.codigo)}</td>
                  <td>{severidadLegible(e.severidad)}</td>
                  <td>{e.conteo}</td>
                  <td className="text-xs">{e.ejemplos.map((x) => `${x.fila + desplazamiento}${x.columna ? ` · ${nombreColumna(x.columna)}` : ''}`).join(', ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {perfil.columnasSinMapeo.length > 0 && (
        <section aria-label="Columnas que no se cargan" className="space-y-1">
          <h3 className="text-sm font-semibold">Columnas que no se cargan</h3>
          <ul className="text-sm">
            {perfil.columnasSinMapeo.map((c) => (
              <li key={c.columna}>«{nombreColumna(c.columna)}»: {c.filasConValor} filas con valor, {c.valoresDistintos} valores distintos</li>
            ))}
          </ul>
        </section>
      )}

      <section aria-label="Qué revisar" className="space-y-1">
        <h3 className="text-sm font-semibold">Qué revisar</h3>
        {perfil.queSeReabre.length === 0 ? (
          <p className="text-sm text-ink-muted">Nada: el archivo cumple con el formato esperado.</p>
        ) : (
          <ul className="list-disc pl-5 text-sm">
            {perfil.queSeReabre.map((r) => (
              <li key={r.hallazgo}>{r.hallazgo} <span className="text-ink-muted">— {r.decision}</span></li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
