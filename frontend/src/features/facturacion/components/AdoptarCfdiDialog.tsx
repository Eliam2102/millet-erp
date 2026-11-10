import { useAdoptarCfdi } from '@/features/facturacion/api/useFacturas';
import type { CampoCotejado } from '@/features/facturacion/api/types';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { mensajeErrorAccion } from '@/lib/api/error';
import { Check, Loader2, X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

/** Tope del XML (igual que el servidor). */
const TAMANO_MAXIMO = 2 * 1024 * 1024;

export interface AdoptarCfdiDialogProps {
  comprobanteId: string;
  folio?: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * M-284 · «Adoptar CFDI timbrado»: el PAC sí timbró pero la respuesta no llegó. Se carga el XML
 * descargado del portal del PAC, se muestra el cotejo contra el comprobante (folio, total, RFC,
 * tipo) y, si todo coincide, se adopta sin volver a timbrar.
 */
export function AdoptarCfdiDialog({ comprobanteId, folio, open, onOpenChange }: AdoptarCfdiDialogProps) {
  const adoptar = useAdoptarCfdi();
  const [xml, setXml] = useState<string | null>(null);
  const [archivo, setArchivo] = useState<string | null>(null);
  const [cotejo, setCotejo] = useState<CampoCotejado[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  function reiniciar() {
    setXml(null);
    setArchivo(null);
    setCotejo(null);
    setError(null);
    adoptar.reset();
  }

  async function cargar(file: File | undefined) {
    reiniciar();
    if (!file) return;
    if (file.size > TAMANO_MAXIMO) {
      setError('El XML excede 2 MB; no parece un CFDI.');
      return;
    }
    const texto = await file.text();
    setArchivo(file.name);
    setXml(texto);
    adoptar.mutate(
      { id: comprobanteId, xml: texto, soloRevisar: true, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => setCotejo(r.cotejo),
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  function confirmar() {
    if (!xml) return;
    setError(null);
    adoptar.mutate(
      { id: comprobanteId, xml, soloRevisar: false, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          toast.success(`CFDI adoptado: ${r.folio} quedó timbrado con UUID ${r.uuid}.`);
          reiniciar();
          onOpenChange(false);
        },
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  const coincideTodo = cotejo != null && cotejo.every((c) => c.coincide);

  return (
    <Dialog
      open={open}
      onOpenChange={(v) => {
        if (!v) reiniciar();
        onOpenChange(v);
      }}
    >
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Adoptar CFDI timbrado{folio ? ` · ${folio}` : ''}</DialogTitle>
          <DialogDescription>
            Úsalo solo si el PAC sí timbró este comprobante y la respuesta no llegó. Descarga el XML del
            portal del PAC y cárgalo aquí: se revisa contra el comprobante y no se vuelve a timbrar.
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-3 text-sm">
          <div className="space-y-1">
            <Label htmlFor="adoptar-cfdi-xml">XML del CFDI timbrado</Label>
            <Input
              id="adoptar-cfdi-xml"
              type="file"
              accept=".xml,application/xml,text/xml"
              onChange={(e) => void cargar(e.target.files?.[0])}
              disabled={adoptar.isPending}
            />
            {archivo && <p className="text-xs text-ink-muted">{archivo}</p>}
          </div>

          {adoptar.isPending && cotejo == null && (
            <p className="flex items-center gap-1 text-ink-muted">
              <Loader2 className="h-3 w-3 animate-spin" /> Revisando el XML…
            </p>
          )}

          {cotejo != null && (
            <Table className="w-full text-sm">
              <TableHeader>
                <TableRow>
                  <TableHead>Dato</TableHead>
                  <TableHead>Comprobante</TableHead>
                  <TableHead>XML</TableHead>
                  <TableHead className="w-8" aria-label="Coincide" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {cotejo.map((c) => (
                  <TableRow key={c.campo} className={c.coincide ? undefined : 'bg-danger-bg'}>
                    <TableCell>{c.campo}</TableCell>
                    <TableCell className="font-mono text-xs">{c.erp}</TableCell>
                    <TableCell className="font-mono text-xs">{c.xml}</TableCell>
                    <TableCell>
                      {c.coincide ? (
                        <Check className="h-4 w-4 text-success-fg" aria-label="Coincide" />
                      ) : (
                        <X className="h-4 w-4 text-danger-fg" aria-label="No coincide" />
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {cotejo != null && !coincideTodo && (
            <p className="text-danger-fg">
              El XML no corresponde a este comprobante. Revisa que descargaste el CFDI con la misma
              serie y folio.
            </p>
          )}
          {error && (
            <p role="alert" className="text-danger-fg">
              {error}
            </p>
          )}
        </DialogBody>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button type="button" onClick={confirmar} disabled={!coincideTodo || adoptar.isPending}>
            {adoptar.isPending && cotejo != null && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
            Adoptar CFDI
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
