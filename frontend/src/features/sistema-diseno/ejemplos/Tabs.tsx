import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';

export default function Ejemplo() {
  return (
    <div className="space-y-3">
      {(['linea', 'pill'] as const).map((variant) => (
        <Tabs key={variant} defaultValue="datos">
          <TabsList variant={variant} aria-label={`Vista ${variant}`}>
            <TabsTrigger value="datos">Datos</TabsTrigger>
            <TabsTrigger value="actividad" count={0}>
              Actividad
            </TabsTrigger>
            <TabsTrigger value="bloqueada" disabled title="Selecciona un documento">
              Adjuntos
            </TabsTrigger>
          </TabsList>
          <TabsContent value="datos">[DATOS FICTICIOS]</TabsContent>
          <TabsContent value="actividad">Sin actividad ficticia</TabsContent>
        </Tabs>
      ))}
    </div>
  );
}
