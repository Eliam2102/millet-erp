import { Copy } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { useOrigenClienteAw } from '@/modules/datos-maestros/api/clientes';
import { textoSolicitudAw } from '@/modules/datos-maestros/lib/solicitud-aw';

/** Cómo llegó una referencia de A+W, qué le falta y la solicitud de corrección para el equipo de A+W. */
export function DetalleOrigenAw({ referencia, fecha }: { referencia: string; fecha: string }) {
  const q = useOrigenClienteAw(referencia, true);
  if (q.isPending)
    return (
      <p role="status" aria-busy className="text-xs text-ink-muted">
        Leyendo A+W…
      </p>
    );
  if (q.isError || !q.data)
    return (
      <p role="alert" className="text-xs text-danger-fg">
        No se pudo leer la referencia en A+W.
      </p>
    );
  const d = q.data;
  const faltan = new Set(d.faltantes.map((f) => f.campo));

  async function copiar() {
    try {
      await navigator.clipboard.writeText(textoSolicitudAw(d, fecha));
      toast.success('Solicitud copiada. Pégala en el correo o ticket para A+W.');
    } catch {
      toast.error('No se pudo copiar al portapapeles.');
    }
  }

  return (
    <div className="mt-2 grid gap-4 rounded-md border border-line bg-surface-card p-3 md:grid-cols-2">
      <table className="w-full text-xs">
        <caption className="mb-1 text-left font-semibold">Cómo llegó de A+W</caption>
        <tbody>
          {d.campos.map((c) => (
            <tr key={c.campo} className={faltan.has(c.campo) ? 'bg-danger-bg text-danger-fg' : undefined}>
              <th scope="row" className="py-0.5 pr-3 text-left font-normal">
                <span className="font-mono">{c.campo}</span>{' '}
                <span className="text-ink-muted">{c.etiqueta}</span>
              </th>
              <td className="py-0.5 break-words">
                {c.valor ?? (faltan.has(c.campo) ? '(vacío) · falta' : '—')}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="space-y-3 text-xs">
        <div>
          <p className="font-semibold">Qué falta</p>
          {d.faltantes.length === 0 ? (
            <p className="text-ink-muted">Hoy A+W ya trae los datos obligatorios; reintenta la lectura.</p>
          ) : (
            <ul className="list-disc pl-4">
              {d.faltantes.map((f) => (
                <li key={f.campo}>
                  <span className="font-mono">{f.campo}</span> ({f.etiqueta}): {f.regla}
                </li>
              ))}
            </ul>
          )}
        </div>
        {d.faltantes.length > 0 && (
          <div>
            <p className="font-semibold">Cómo debe quedar</p>
            <ul className="list-disc pl-4">
              {d.faltantes.map((f) => (
                <li key={f.campo}>
                  <span className="font-mono">{f.campo}</span>: {f.comoDebeQuedar}
                </li>
              ))}
            </ul>
          </div>
        )}
        {d.faltantes.length > 0 && (
          <Button type="button" variant="outline" size="sm" onClick={() => void copiar()}>
            <Copy aria-hidden className="mr-1.5 h-3.5 w-3.5" />
            Copiar solicitud para A+W
          </Button>
        )}
      </div>
    </div>
  );
}
