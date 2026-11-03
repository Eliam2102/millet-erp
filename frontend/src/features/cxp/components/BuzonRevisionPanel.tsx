import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import {
  useBuzonEjecuciones,
  useBuzonRevision,
  useDescartarBuzonRevision,
} from '@/features/cxp/api/useCfdis';
import { BuzonMotivoLabels } from '@/features/cxp/api/types';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';

/**
 * Bandeja de revisión del buzón de correo (P8.1-a): correos que no se
 * convirtieron en CFDI recibido (sin XML, XML inválido, receptor ajeno...)
 * + resumen de la última lectura. No se muestra si el buzón nunca corrió y
 * no hay correos por revisar.
 */
export function BuzonRevisionPanel({ puedeDescartar }: { puedeDescartar: boolean }) {
  const revision = useBuzonRevision();
  const ejecuciones = useBuzonEjecuciones();
  const descartar = useDescartarBuzonRevision();
  const idempotencyKey = useFormIdempotencyKey();

  const items = revision.data?.items ?? [];
  const ultima = ejecuciones.data?.[0];
  if (items.length === 0 && !ultima) return null;

  function onDescartar(id: string) {
    descartar.mutate(
      { id, motivo: 'Revisado y descartado desde la bandeja del buzón', idempotencyKey },
      {
        onSuccess: () => toast.success('Correo descartado'),
        onError: (e) =>
          toast.error(
            esApiError(e) ? `No se pudo descartar (${codigoSoporte(e)})` : 'No se pudo descartar',
          ),
      },
    );
  }

  return (
    <section aria-label="Buzón de correo" className="space-y-2 rounded-md border p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-semibold">
          Buzón de correo: por revisar ({items.length})
        </h2>
        {ultima && (
          <p className="text-xs text-muted-foreground">
            Última lectura <DateTimeDisplay value={ultima.inicioUtc} variant="corta" />
            {ultima.error
              ? ` · falló (${ultima.error})`
              : ` · ${ultima.ingresados} ingresados, ${ultima.duplicados} duplicados, ${ultima.aRevision} a revisión`}
          </p>
        )}
      </div>
      {items.length > 0 && (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Recibido</th>
                <th className="px-3 py-2 text-left">Remitente</th>
                <th className="px-3 py-2 text-left">Asunto</th>
                <th className="px-3 py-2 text-left">Motivo</th>
                <th className="w-8 px-3 py-2" aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {items.map((b) => (
                <tr key={b.id} className="border-t">
                  <td className="whitespace-nowrap px-3 py-2 text-xs">
                    <DateTimeDisplay value={b.recibidoEn} variant="corta" />
                  </td>
                  <td className="px-3 py-2 text-xs">{b.remitente}</td>
                  <td className="px-3 py-2">{b.asunto}</td>
                  <td className="px-3 py-2">
                    {BuzonMotivoLabels[b.motivo] ?? b.motivo}
                  </td>
                  <td className="px-3 py-2">
                    {puedeDescartar && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={descartar.isPending}
                        onClick={() => onDescartar(b.id)}
                        aria-label={`Descartar correo ${b.asunto}`}
                      >
                        Descartar
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
