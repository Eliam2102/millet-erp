import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '@/components/ui/dialog';
import { useConservacion, type DocumentoActivo, type Vencimiento } from '../api/conservacion';
import { useGuardarActivo } from '../api/hooks';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
export function ResolverAviso({ aviso }: { aviso: Vencimiento }) {
  const [abierto, setAbierto] = useState(false);
  const [resolucion, setResolucion] = useState('Cerrado');
  const documentos = useConservacion<DocumentoActivo[]>(
    `activos/${aviso.activoId}/documentos`,
    abierto,
  );
  const guardar = useGuardarActivo();
  const permiso = useHasPermission(P.ActivosFijosDocumentosGestionar);
  if (!permiso) return null;
  return (
    <>
      <Button variant="outline" onClick={() => setAbierto(true)}>
        Resolver
      </Button>
      <Dialog open={abierto} onOpenChange={setAbierto}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Resolver vencimiento</DialogTitle>
          </DialogHeader>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const f = new FormData(e.currentTarget);
              guardar.mutate(
                {
                  ruta: `activos/${aviso.activoId}/avisos/${aviso.id}/resolver`,
                  body: {
                    resolucion,
                    motivo: f.get('motivo'),
                    soporte: f.get('soporte') || null,
                    documentoNuevoId: f.get('nuevo') || null,
                  },
                },
                { onSuccess: () => setAbierto(false) },
              );
            }}
          >
            <DialogBody className="space-y-3">
              <label>
                Resolución *
                <select
                  value={resolucion}
                  onChange={(e) => setResolucion(e.target.value)}
                  className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
                >
                  <option value="Cerrado">Cerrado</option>
                  {!aviso.esMantenimiento && <option value="Renovado">Renovado</option>}
                </select>
              </label>
              {resolucion === 'Renovado' ? (
                <>
                  <p>
                    Suba la nueva versión en Documentos del activo y seleccione la renovación con
                    vigencia posterior se resuelve el aviso. También puede seleccionar una
                    renovación ya registrada.
                  </p>
                  <Link
                    to="/activos-fijos/activos/$id"
                    params={{ id: aviso.activoId }}
                    className="text-brand"
                  >
                    Abrir documentos del activo
                  </Link>
                  <label>
                    Documento nuevo *
                    <select
                      name="nuevo"
                      required
                      className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
                    >
                      <option value="">Seleccione nueva versión</option>
                      {documentos.data
                        ?.filter(
                          (d) =>
                            !d.reemplazado &&
                            d.reemplazaA &&
                            d.vigenteHasta &&
                            d.vigenteHasta > aviso.vence,
                        )
                        .map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.folio} · Versión {d.numeroVersion} · {d.vigenteHasta}
                          </option>
                        ))}
                    </select>
                  </label>
                  {documentos.error && <p role="alert">{documentos.error.message}</p>}
                </>
              ) : (
                <>
                  <label>
                    Motivo *<Input name="motivo" required maxLength={2000} />
                  </label>
                  <label>
                    Soporte (opcional)
                    <Input name="soporte" maxLength={2000} />
                  </label>
                </>
              )}
              {guardar.error && <p role="alert">{guardar.error.message}</p>}
            </DialogBody>
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => setAbierto(false)}>
                Cancelar
              </Button>
              <Button type="submit" disabled={guardar.isPending}>
                Guardar resolución
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
