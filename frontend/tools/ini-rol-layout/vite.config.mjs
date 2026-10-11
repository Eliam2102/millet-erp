import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';
export default defineConfig({root: path.resolve(import.meta.dirname), base:'./', plugins: [react(),tailwindcss()],
 resolve:{alias:{'@':path.resolve(import.meta.dirname,'../../src')}}, server:{fs:{allow:[path.resolve(import.meta.dirname,'../..')]}}});
