import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ConfirmacionContent } from './ConfirmacionContent';
import { Button } from '@/components/ui/button';
import { apiRequest, esApiError } from '@/lib/api';
import { apiFetch } from '@/lib/auth/api-client';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { adminKeys } from '@/modules/administracion/api/keys';

export function EmpresaLogoSection({ empresaId }: { empresaId: string }) {
  const puedeEditar = useHasPermission(PermisosCanonicos.AdminEmpresasEditar);
  const cliente = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const preview = useRef<HTMLImageElement>(null);
  const [confirmarQuitar, setConfirmarQuitar] = useState(false);
  const [errorLocal, setErrorLocal] = useState<string>();
  const path = `/api/v1/admin/empresas/${empresaId}/logo`;
  const queryKey = adminKeys.logoEmpresa(empresaId);
  const logo = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const response = await apiFetch(path, { signal });
      if (response.status === 404) return null;
      if (!response.ok) throw new Error('No se pudo consultar el logo. Recarga la empresa.');
      return response.blob();
    },
  });
  useEffect(() => {
    if (!logo.data || !preview.current) return;
    const objectUrl = URL.createObjectURL(logo.data);
    preview.current.src = objectUrl;
    return () => URL.revokeObjectURL(objectUrl);
  }, [logo.data]);
  const guardar = useMutation({
    mutationFn: async (archivo: File | null) => {
      const body = new FormData();
      if (archivo) body.append('archivo', archivo);
      await apiRequest(path, {
        method: archivo ? 'PUT' : 'DELETE',
        idempotencyKey: crypto.randomUUID(),
        body: archivo ? body : undefined,
      });
    },
    onSuccess: async () => {
      setErrorLocal(undefined);
      await Promise.all([
        cliente.invalidateQueries({ queryKey }),
        cliente.invalidateQueries({ queryKey: adminKeys.empresas() }),
      ]);
    },
  });
  const error =
    errorLocal ??
    (guardar.error
      ? esApiError(guardar.error)
        ? guardar.error.problem.detail || 'No se pudo guardar el logo. Intenta de nuevo.'
        : 'No se pudo guardar el logo. Intenta de nuevo.'
      : undefined);
  return (
    <section
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
      aria-label="Logo para documentos impresos"
    >
      <h2 className="text-lg font-semibold">Logo para documentos impresos</h2>
      <p className="text-xs text-ink-muted">
        PNG o JPG de hasta 1 MB. Se usa en órdenes de compra, facturas y notas de crédito.
      </p>
      <p className="text-xs text-ink-muted">Ancho mínimo: 200 píxeles.</p>
      {logo.isPending ? (
        <p>Cargando logo…</p>
      ) : logo.isError ? (
        <p role="alert">No se pudo consultar el logo. Recarga la empresa.</p>
      ) : logo.data ? (
        <img
          ref={preview}
          alt="Logo de la empresa para documentos impresos"
          className="max-h-32 max-w-full object-contain"
        />
      ) : (
        <p className="text-sm text-ink-muted">Sin logo</p>
      )}
      {puedeEditar && (
        <div className="flex gap-2">
          <input
            ref={input}
            type="file"
            accept="image/png,image/jpeg"
            className="hidden"
            aria-label="Archivo de logo"
            onChange={(event) => {
              const archivo = event.target.files?.[0];
              event.target.value = '';
              if (!archivo) return;
              if (archivo.size > 1024 * 1024) {
                setErrorLocal('El logo excede 1 MB. Selecciona una imagen más pequeña.');
                return;
              }
              setErrorLocal(undefined);
              guardar.mutate(archivo);
            }}
          />
          <Button
            type="button"
            variant="outline"
            disabled={guardar.isPending}
            title={guardar.isPending ? 'Espera a que termine de guardarse el logo' : undefined}
            onClick={() => input.current?.click()}
          >
            {logo.data ? 'Cambiar' : 'Subir logo'}
          </Button>
          {logo.data && (
            <Button
              type="button"
              variant="ghost"
              disabled={guardar.isPending}
              title={guardar.isPending ? 'Espera a que termine de guardarse el logo' : undefined}
              onClick={() => setConfirmarQuitar(true)}
            >
              Quitar
            </Button>
          )}
        </div>
      )}
      <AlertDialog open={confirmarQuitar} onOpenChange={setConfirmarQuitar}>
        <ConfirmacionContent>
          <AlertDialogHeader className="px-6 pt-5 pb-3">
            <AlertDialogTitle>Quitar logo</AlertDialogTitle>
          </AlertDialogHeader>
          <div className="px-6 py-4">
            <AlertDialogDescription>
              ¿Quitar el logo de los documentos impresos de esta empresa?
            </AlertDialogDescription>
          </div>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
            <AlertDialogCancel disabled={guardar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={() => guardar.mutate(null)} disabled={guardar.isPending}>
              Quitar logo
            </AlertDialogAction>
          </AlertDialogFooter>
        </ConfirmacionContent>
      </AlertDialog>
      {error && (
        <p role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
    </section>
  );
}
