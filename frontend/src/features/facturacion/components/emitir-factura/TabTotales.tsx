import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AnticipoPicker } from '@/features/facturacion/components/AnticipoPicker';

/**
 * Pestaña "Totales" del form de emisión (FAC-UX-PR2): resumen de importes
 * (subtotal, IVA, retenciones, total — calculados en vivo desde las
 * posiciones) + anticipos a amortizar (relación 07). FAC-UX-PR4: los
 * anticipos se eligen con <c>&lt;AnticipoPicker/&gt;</c> (folio + saldo
 * del cliente) — cero GUIDs; al seleccionar se propone el saldo como
 * importe a amortizar.
 */
export interface AnticipoAmortizar {
  anticipoId: string;
  importe: number;
}

export interface TabTotalesProps {
  totales: { subtotal: number; iva: number; ret: number };
  total: number;
  moneda: string | undefined;
  /** Cliente de la factura — null deshabilita el picker de anticipos. */
  clienteId: string | null;
  anticipos: AnticipoAmortizar[];
  onAnticiposChange: (anticipos: AnticipoAmortizar[]) => void;
}

export function TabTotales({
  totales,
  total,
  moneda,
  clienteId,
  anticipos,
  onAnticiposChange,
}: TabTotalesProps) {
  return (
    <div className="space-y-5">
      <section className="space-y-2">
        <h3 className="text-sm font-medium">Resumen de importes</h3>
        <dl className="max-w-md space-y-1 rounded-md border px-4 py-3 text-sm tabular-nums">
          <FilaTotal etiqueta="Subtotal" valor={totales.subtotal} />
          <FilaTotal etiqueta="IVA trasladado" valor={totales.iva} />
          <FilaTotal etiqueta="Retenciones" valor={-totales.ret} />
          <div className="flex items-center justify-between border-t pt-1 font-semibold">
            <dt>Total</dt>
            <dd>
              {total.toFixed(2)} {moneda?.toUpperCase()}
            </dd>
          </div>
        </dl>
        <p className="text-xs text-muted-foreground">
          Los importes se calculan en vivo desde las posiciones; el backend
          recalcula y valida al emitir.
        </p>
      </section>

      {/* ── Anticipos a amortizar (relación 07, opcional) ───────── */}
      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">
            Anticipos a amortizar ({anticipos.length})
          </h3>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() =>
              onAnticiposChange([...anticipos, { anticipoId: '', importe: 0 }])
            }
          >
            <Plus className="mr-1 h-3 w-3" />
            Agregar anticipo
          </Button>
        </header>
        {anticipos.length === 0 ? (
          <p className="text-xs text-muted-foreground">
            Opcional. Si el cliente tiene anticipos abiertos, agrégalos para
            amortizarlos (el backend emite la NC de amortización al timbrar).
          </p>
        ) : (
          <div className="space-y-2">
            {anticipos.map((a, index) => (
              <div
                key={index}
                className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 sm:grid-cols-12"
              >
                <div className="sm:col-span-7">
                  <Label className="text-xs">Anticipo *</Label>
                  <AnticipoPicker
                    clienteId={clienteId}
                    value={a.anticipoId || null}
                    onChange={(item) =>
                      onAnticiposChange(
                        anticipos.map((x, i) =>
                          i === index
                            ? {
                                anticipoId: item?.anticipoId ?? '',
                                // Propone el saldo; editable abajo.
                                importe:
                                  item != null && x.importe === 0
                                    ? item.saldo
                                    : x.importe,
                              }
                            : x,
                        ),
                      )
                    }
                  />
                </div>
                <div className="sm:col-span-3">
                  <Label className="text-xs">Importe *</Label>
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    value={a.importe}
                    onChange={(e) =>
                      onAnticiposChange(
                        anticipos.map((x, i) =>
                          i === index
                            ? { ...x, importe: Number(e.target.value) || 0 }
                            : x,
                        ),
                      )
                    }
                  />
                </div>
                <div className="flex items-end justify-end sm:col-span-2">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:bg-destructive/10"
                    onClick={() =>
                      onAnticiposChange(anticipos.filter((_, i) => i !== index))
                    }
                  >
                    <Trash2 className="mr-1 h-3 w-3" />
                    Quitar
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function FilaTotal({ etiqueta, valor }: { etiqueta: string; valor: number }) {
  return (
    <div className="flex items-center justify-between">
      <dt className="text-muted-foreground">{etiqueta}</dt>
      <dd className="font-medium">{valor.toFixed(2)}</dd>
    </div>
  );
}
