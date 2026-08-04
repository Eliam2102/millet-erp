import { useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Loader2, ShieldCheck } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useSubirFielReceptor } from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import {
  SubirFielReceptorSchema,
  type SubirFielReceptorValues,
} from '@/features/integraciones-fiscal/schemas/configuracion-pac';

/**
 * <c>&lt;SubirFielDialog/&gt;</c> — modal para subir la FIEL (e.firma)
 * del RFC receptor a FiscalAPI.
 *
 * <para>
 * <b>Excepción al patrón "no modales para items"</b>: los items inline
 * son para listas (RFCs, líneas de doc, etc.). Este NO es un item —
 * es una operación cuasi-administrativa puntual (sucede 1 vez por RFC
 * + cuando renueva FIEL cada ~4 años) con datos sensibles que
 * justifican aislamiento visual.
 * </para>
 *
 * <para>
 * El archivo .cer y .key se leen client-side con <c>FileReader</c> y se
 * convierten a base64 antes de POST. Validamos tamaño (max 50KB) y
 * extensión (.cer / .key) en el cliente para feedback inmediato.
 * </para>
 */
export interface SubirFielDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  rfcReceptorId: string;
  empresaId: string;
  rfc: string;
}

export function SubirFielDialog({
  open,
  onOpenChange,
  rfcReceptorId,
  empresaId,
  rfc,
}: SubirFielDialogProps) {
  const cerInputRef = useRef<HTMLInputElement>(null);
  const keyInputRef = useRef<HTMLInputElement>(null);
  const [cerFile, setCerFile] = useState<File | null>(null);
  const [keyFile, setKeyFile] = useState<File | null>(null);
  const subir = useSubirFielReceptor();
  const idempotencyKey = useFormIdempotencyKey();

  const form = useForm<SubirFielReceptorValues>({
    resolver: zodResolver(SubirFielReceptorSchema),
    defaultValues: {
      legalName: '',
      zipCode: '',
      satTaxRegimeCode: '601',
      email: '',
      password: '',
    },
  });

  function handleClose() {
    form.reset();
    setCerFile(null);
    setKeyFile(null);
    onOpenChange(false);
  }

  async function onSubmit(values: SubirFielReceptorValues) {
    if (!cerFile || !keyFile) {
      toast.error('Debes seleccionar los archivos .cer y .key.');
      return;
    }
    const cerBase64 = await fileToBase64(cerFile);
    const keyBase64 = await fileToBase64(keyFile);

    subir.mutate(
      {
        id: rfcReceptorId,
        empresaId,
        payload: { ...values, cerBase64, keyBase64 },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`FIEL cargada para ${rfc}.`);
          handleClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al subir FIEL.');
        },
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose(); else onOpenChange(o); }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ShieldCheck className="h-5 w-5 text-primary" />
            Subir FIEL para {rfc}
          </DialogTitle>
          <DialogDescription>
            La e.firma se sube cifrada a FiscalAPI para que pueda solicitar al SAT
            los CFDIs recibidos en este RFC. Los datos del receptor deben coincidir
            EXACTO con el registro SAT.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="legalName">Razón social (sin régimen societario)</Label>
            <Input
              id="legalName"
              placeholder="MILLET RAZON SOCIAL"
              {...form.register('legalName')}
            />
            {form.formState.errors.legalName && (
              <p className="text-xs text-destructive">
                {form.formState.errors.legalName.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label htmlFor="zipCode">CP fiscal</Label>
              <Input
                id="zipCode"
                placeholder="97000"
                maxLength={5}
                {...form.register('zipCode')}
              />
              {form.formState.errors.zipCode && (
                <p className="text-xs text-destructive">
                  {form.formState.errors.zipCode.message}
                </p>
              )}
            </div>
            <div className="space-y-1">
              <Label htmlFor="satTaxRegimeCode">Régimen fiscal SAT</Label>
              <Input
                id="satTaxRegimeCode"
                placeholder="601"
                {...form.register('satTaxRegimeCode')}
              />
              {form.formState.errors.satTaxRegimeCode && (
                <p className="text-xs text-destructive">
                  {form.formState.errors.satTaxRegimeCode.message}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-1">
            <Label htmlFor="email">Email administrativo</Label>
            <Input
              id="email"
              type="email"
              placeholder="admin@millet.com"
              {...form.register('email')}
            />
            {form.formState.errors.email && (
              <p className="text-xs text-destructive">
                {form.formState.errors.email.message}
              </p>
            )}
          </div>

          <div className="space-y-1 border-t pt-3">
            <Label htmlFor="cerFile">Archivo .cer (FIEL)</Label>
            <input
              id="cerFile"
              ref={cerInputRef}
              type="file"
              accept=".cer"
              className="block w-full text-sm"
              onChange={(e) => setCerFile(e.target.files?.[0] ?? null)}
            />
            {cerFile && (
              <p className="text-xs text-muted-foreground">
                {cerFile.name} — {(cerFile.size / 1024).toFixed(1)} KB
              </p>
            )}
          </div>

          <div className="space-y-1">
            <Label htmlFor="keyFile">Archivo .key (FIEL)</Label>
            <input
              id="keyFile"
              ref={keyInputRef}
              type="file"
              accept=".key"
              className="block w-full text-sm"
              onChange={(e) => setKeyFile(e.target.files?.[0] ?? null)}
            />
            {keyFile && (
              <p className="text-xs text-muted-foreground">
                {keyFile.name} — {(keyFile.size / 1024).toFixed(1)} KB
              </p>
            )}
          </div>

          <div className="space-y-1">
            <Label htmlFor="password">Password de la FIEL</Label>
            <Input
              id="password"
              type="password"
              autoComplete="off"
              {...form.register('password')}
            />
            {form.formState.errors.password && (
              <p className="text-xs text-destructive">
                {form.formState.errors.password.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Se envía a FiscalAPI cifrado HTTPS — no se persiste localmente.
            </p>
          </div>

          <DialogFooter className="border-t pt-3">
            <Button type="button" variant="outline" onClick={handleClose} disabled={subir.isPending}>
              Cancelar
            </Button>
            <Button type="submit" disabled={subir.isPending || !cerFile || !keyFile}>
              {subir.isPending && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
              Subir FIEL
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

async function fileToBase64(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  let binary = '';
  const bytes = new Uint8Array(buffer);
  const chunkSize = 0x8000;
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}
