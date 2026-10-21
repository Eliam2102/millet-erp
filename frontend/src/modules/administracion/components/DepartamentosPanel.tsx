import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { ListadoCatalogo } from '@/modules/administracion/components/ListadoCatalogo';
import { OPCIONES_CATALOGO } from '@/modules/administracion/lib/estados-ui';
import { useState } from 'react';
import { Pencil, Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EstatusCatalogo, type DepartamentoResponse } from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DepartamentoInlineForm } from '@/modules/administracion/components/DepartamentoInlineForm';

/**
 * Panel "Departamentos" del detalle de empresa. Espejo de
 * <c>SucursalesPanel</c> sin "Desactivar" — el backend MVP no expone
 * el endpoint; solo POST y PATCH. Si más adelante se agrega, se
 * espeja la pieza aquí.
 */
export interface DepartamentosPanelProps {
  empresaId: string;
  departamentos: readonly DepartamentoResponse[];
  /**
   * Cuando la empresa que se está viendo no coincide con la empresa
   * activa de la sesión, la gestión (alta/edición) queda bloqueada
   * aunque el usuario tenga el permiso — el backend resolvería la
   * escritura contra la empresa activa, no contra la que se ve.
   */
  bloqueadoPorEmpresaActiva?: boolean;
}

export function DepartamentosPanel({
  empresaId,
  departamentos,
  bloqueadoPorEmpresaActiva = false,
}: DepartamentosPanelProps) {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);

  const canGestionar =
    useHasPermission(PermisosCanonicos.AdminDepartamentosGestionar) && !bloqueadoPorEmpresaActiva;

  return (
    <section className="space-y-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-base font-semibold">Departamentos</h3>
          <p className="text-xs text-ink-muted">
            Catálogo organizacional compartido. Total: {departamentos.length}.
          </p>
        </div>
        {canGestionar && !agregando && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              setAgregando(true);
              setEditandoId(null);
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            Agregar departamento
          </Button>
        )}
      </header>

      {agregando && canGestionar && (
        <DepartamentoInlineForm
          empresaId={empresaId}
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {departamentos.length === 0 ? (
        <div className="rounded-md border border-dashed bg-surface-muted/20 px-4 py-6 text-center text-sm text-ink-muted">
          No hay departamentos registrados.
        </div>
      ) : (
        <ListadoCatalogo
          items={departamentos}
          texto={(d) => d.clave + ' ' + d.nombre}
          estado={(d) => d.estatus}
          opciones={OPCIONES_CATALOGO}
        >
          {(visibles) => (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Clave</TableHead>
                  <TableHead>Departamento</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibles.map((d) => {
                  const editando = editandoId === d.id;
                  const activo = d.estatus === EstatusCatalogo.Activo;
                  return (
                    <TableRow key={d.id} className="px-3 py-2">
                      {editando && canGestionar ? (
                        <TableCell colSpan={4}>
                          <DepartamentoInlineForm
                            empresaId={empresaId}
                            departamento={d}
                            onCancel={() => setEditandoId(null)}
                            onSaved={() => setEditandoId(null)}
                          />
                        </TableCell>
                      ) : (
                        <>
                          <TableCell className="w-32">
                            <span className="font-mono text-sm font-semibold">{d.clave}</span>
                          </TableCell>
                          <TableCell className="min-w-48">
                            <span className="flex-1 truncate text-sm">{d.nombre}</span>
                          </TableCell>
                          <TableCell className="w-32">
                            {activo ? (
                              <Badge variant="success">Activo</Badge>
                            ) : (
                              <Badge variant="neutral">Inactivo</Badge>
                            )}
                          </TableCell>
                          <TableCell className="w-28">
                            {canGestionar && (
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => {
                                  setEditandoId(d.id);
                                  setAgregando(false);
                                }}
                                aria-label={`Editar departamento ${d.clave}`}
                              >
                                <Pencil className="h-3.5 w-3.5" />
                              </Button>
                            )}
                          </TableCell>
                        </>
                      )}
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </ListadoCatalogo>
      )}
    </section>
  );
}
