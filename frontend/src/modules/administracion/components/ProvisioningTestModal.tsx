import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { Bot } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { apiRequest } from '@/lib/api';

export function ProvisioningTestModal({ defaultNombre = '' }: { defaultNombre?: string }) {
  const [open, setOpen] = useState(false);
  const [habilitado, setHabilitado] = useState(true);
  const [esNuevo, setEsNuevo] = useState(true);
  
  const [nombre, setNombre] = useState(defaultNombre || 'user');
  const [apellido, setApellido] = useState('');
  const [upn, setUpn] = useState('');
  const [correo, setCorreo] = useState('uzieltzab8@gmail.com');
  const [loading, setLoading] = useState(false);

  // Estados de validación de UPN
  const [upnValidando, setUpnValidando] = useState(false);
  const [upnError, setUpnError] = useState<string | null>(null);
  const [upnSuccess, setUpnSuccess] = useState<string | null>(null);

  async function verificarUpn(correoValidar: string) {
    if (!correoValidar || correoValidar.indexOf('@') === -1) {
      setUpnError(null);
      setUpnSuccess(null);
      return;
    }
    
    setUpnValidando(true);
    setUpnError(null);
    setUpnSuccess(null);
    
    try {
      const { data } = await apiRequest<{existe: boolean}>(`/api/identidad/provisionar/verificar-upn?upn=${encodeURIComponent(correoValidar)}`);
      
      if (esNuevo) {
        if (data.existe) {
          setUpnError('Este correo ya está ocupado en Azure Entra ID.');
        } else {
          setUpnSuccess('Correo disponible ✅');
        }
      } else {
        if (!data.existe) {
          setUpnError('Este usuario no existe en Azure. Por favor, selecciona "Crear Nuevo".');
        } else {
          setUpnSuccess('Usuario encontrado en Azure ✅');
        }
      }
    } catch {
      // Ignorar errores silenciosos en la validación
    } finally {
      setUpnValidando(false);
    }
  }

  // Lee el dominio desde el .env del frontend, si no existe usa tu sandbox por defecto
  const entraDomain = import.meta.env.VITE_ENTRA_DOMAIN || 'uzieltzaboutlook.onmicrosoft.com';

  // Autogenerar UPN amigable cuando cambian los datos
  useEffect(() => {
    if (esNuevo) {
      const n = (nombre || '').toLowerCase().trim().replace(/\s+/g, '.');
      const a = (apellido || '').toLowerCase().trim().replace(/\s+/g, '.');
      const prefix = [n, a].filter(Boolean).join('.');
      if (prefix) {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setUpn(`${prefix}@${entraDomain}`);
      } else {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setUpn(`usuario@${entraDomain}`);
      }
    }
  }, [nombre, apellido, esNuevo, entraDomain]);

  async function handleTestApi() {
    if (!habilitado) {
      toast.info("Aprovisionamiento desactivado. Solo se crearía el empleado.");
      setOpen(false);
      return;
    }

    setLoading(true);
    try {
      const { data: response } = await apiRequest<{mensaje: string, entraOid: string}>('/api/identidad/provisionar', {
        method: 'POST',
        body: {
          nombre,
          apellido,
          upn,
          correoContacto: correo,
          esNuevo,
        }
      });
      
      toast.success(`Éxito: ${response.mensaje}`, {
        description: `OID Azure: ${response.entraOid}`
      });
      setOpen(false);
    } catch (error: unknown) {
      const err = error as Record<string, unknown>; const msg = (err.problem as Record<string, string>)?.title || (err.message as string);
      toast.error("Error en el API de Provisión", { description: msg });
    } finally {
      setLoading(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="border-indigo-200 bg-indigo-50 text-indigo-700 hover:bg-indigo-100">
          <Bot className="mr-1 h-4 w-4" />
          Probar API Identidad (PoC)
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Módulo de Provisión (Test API)</DialogTitle>
          <DialogDescription>
            Simulador del Wizard para probar la creación/vinculación real en Azure Entra ID.
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center space-x-2 py-4">
          <input 
            type="checkbox" 
            id="hab" 
            checked={habilitado} 
            onChange={e => setHabilitado(e.target.checked)} 
            className="w-4 h-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
          />
          <Label htmlFor="hab" className="cursor-pointer">Habilitar acceso al sistema ERP (Entra ID)</Label>
        </div>

        {habilitado && (
          <div className="space-y-4 rounded-md border bg-zinc-50 p-4">
            <div className="flex gap-4">
              <label className="flex items-center space-x-2 cursor-pointer">
                <input 
                  type="radio" 
                  name="tipoCuenta" 
                  value="nuevo" 
                  checked={esNuevo} 
                  onChange={() => setEsNuevo(true)} 
                  className="w-4 h-4 text-indigo-600 border-gray-300 focus:ring-indigo-500"
                />
                <span className="text-sm font-medium">Crear Nuevo</span>
              </label>
              <label className="flex items-center space-x-2 cursor-pointer">
                <input 
                  type="radio" 
                  name="tipoCuenta" 
                  value="existente" 
                  checked={!esNuevo} 
                  onChange={() => setEsNuevo(false)} 
                  className="w-4 h-4 text-indigo-600 border-gray-300 focus:ring-indigo-500"
                />
                <span className="text-sm font-medium">Vincular Existente</span>
              </label>
            </div>

            {esNuevo && (
              <div className="grid grid-cols-2 gap-3 mt-3">
                <div className="space-y-1">
                  <Label className="text-xs">Nombre</Label>
                  <Input value={nombre} onChange={e => setNombre(e.target.value)} disabled={!esNuevo} />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Apellido</Label>
                  <Input value={apellido} onChange={e => setApellido(e.target.value)} disabled={!esNuevo} />
                </div>
              </div>
            )}

            <div className="space-y-1 mt-3">
              <Label className="text-xs">Correo Organizacional (UPN)</Label>
              <Input value={upn} onChange={e => { setUpn(e.target.value); setUpnError(null); setUpnSuccess(null); }} onBlur={(e) => verificarUpn(e.target.value)} placeholder="usuario@millet.onmicrosoft.com" />
              {upnValidando && <p className="text-xs text-blue-600 font-medium">Validando en Azure...</p>}
              {upnError && <p className="text-xs text-red-600 font-medium">{upnError}</p>}
              {upnSuccess && <p className="text-xs text-emerald-600 font-medium">{upnSuccess}</p>}
            </div>

            <div className="space-y-1">
              <Label className="text-xs">Correo de Contacto (Para notificaciones)</Label>
              <Input value={correo} onChange={e => setCorreo(e.target.value)} type="email" />
            </div>
            
            {esNuevo && (
              <div className="mt-2 text-xs text-amber-600 bg-amber-50 p-2 rounded">
                Se autogenerará una contraseña temporal y se enviará por correo. El usuario deberá cambiarla.
              </div>
            )}
          </div>
        )}

        <DialogFooter className="sm:justify-end">
          <Button type="button" variant="secondary" onClick={() => setOpen(false)}>
            Cerrar
          </Button>
          <Button type="button" onClick={handleTestApi} disabled={loading || upnValidando || upnError !== null}>
            {loading ? "Ejecutando API..." : "Simular Llamada al Backend"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
