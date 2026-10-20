import { DateTimeDisplay } from '@/components/erp';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { ResultadoCobranza, type SeguimientoCobranzaResponse } from '../api/types';
import { ETIQUETA_CANAL_COBRANZA, formatoMonto } from '../lib/glosario';
import { EstadoCxcBadge } from './EstadoCxcBadge';

/** Historial de gestiones en la tabla del listado; conserva el orden recibido. */
export function TimelineCobranza({
  items,
  nombreUsuario,
  moneda = 'MXN',
}: {
  items: readonly SeguimientoCobranzaResponse[];
  nombreUsuario: (id: string) => string;
  /** La gestión no incluye divisa; se conserva la etiqueta utilizada por la pantalla. */
  moneda?: string;
}) {
  return (
    <Table aria-label="Historial de gestiones">
      <TableHeader>
        <TableRow>
          <TableHead>Fecha / responsable</TableHead>
          <TableHead>Canal</TableHead>
          <TableHead>Gestión</TableHead>
          <TableHead>Resultado</TableHead>
          <TableHead className="text-right">Compromiso</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map((s) => (
          <TableRow key={s.id} className="h-12">
            <TableCell>
              <DateTimeDisplay value={s.fecha} />
              <p className="text-xs text-ink-muted">{nombreUsuario(s.usuarioId)}</p>
            </TableCell>
            <TableCell>{ETIQUETA_CANAL_COBRANZA[s.canal]}</TableCell>
            <TableCell className="whitespace-pre-wrap text-ink-secondary">{s.nota}</TableCell>
            <TableCell>
              <ChipResultadoCobranza resultado={s.resultado} />
            </TableCell>
            <TableCell className="text-right tabular-nums">
              {s.resultado === ResultadoCobranza.PromesaPago && s.montoComprometido != null ? (
                <>
                  <span>Compromiso: {formatoMonto(s.montoComprometido, moneda)}</span>
                  {s.fechaComprometida && (
                    <p className="text-xs text-ink-muted">para el {s.fechaComprometida}</p>
                  )}
                </>
              ) : (
                '—'
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
export function ChipResultadoCobranza({ resultado }: { resultado: ResultadoCobranza }) {
  return <EstadoCxcBadge entidad="cobranza" estado={resultado} />;
}
