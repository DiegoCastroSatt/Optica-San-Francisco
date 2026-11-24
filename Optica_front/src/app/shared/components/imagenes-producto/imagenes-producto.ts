import { Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, output, signal } from '@angular/core';
import { obtenerOrigenApi } from '../../services/api-origin';

export interface SeleccionImagenes {
  archivos: File[];
  rutas: string[];
  indicePrincipal: number;
}

interface Foto {
  archivo?: File;
  ruta: string;
  vista: string;
}

@Component({
  selector: 'app-imagenes-producto',
  templateUrl: './imagenes-producto.html',
  styleUrl: './imagenes-producto.css'
})
export class ImagenesProducto implements OnChanges, OnDestroy {
  @Input() rutas: string[] = [];
  @Input() bloqueado = false;
  @ViewChild('video') video?: ElementRef<HTMLVideoElement>;
  readonly cambio = output<SeleccionImagenes>();
  protected readonly fotos = signal<Foto[]>([]);
  protected readonly principal = signal(0);
  protected readonly error = signal('');
  protected readonly camaraAbierta = signal(false);
  protected readonly esperando = signal(false);
  protected readonly capturando = signal(false);
  private stream?: MediaStream;
  private turnoCamara = 0;

  ngOnChanges(): void {
    if (this.bloqueado) this.cerrarCamara();
    // El padre conserva la misma referencia mientras se edita.
    if (this.rutasActuales !== this.rutas) {
      this.liberarFotos();
      this.rutasActuales = this.rutas;
      this.fotos.set(this.rutas.map(ruta => ({
        ruta, vista: ruta.startsWith('http') ? ruta : `${obtenerOrigenApi()}${ruta}`
      })));
      this.principal.set(0);
    }
  }

  private rutasActuales?: string[];

  protected seleccionar(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.agregar(Array.from(input.files ?? []));
    input.value = '';
  }

  private agregar(archivos: File[]): void {
    this.error.set('');
    if (!archivos.length) return;
    if (this.fotos().length + archivos.length > 5) {
      this.error.set('Puedes guardar hasta 5 fotos por producto.');
      return;
    }
    if (archivos.some(f => !['image/jpeg', 'image/png', 'image/webp'].includes(f.type) || f.size > 5 * 1024 * 1024 || f.size === 0)) {
      this.error.set('Cada foto debe ser JPG, PNG o WebP y pesar hasta 5 MB.');
      return;
    }
    this.fotos.update(fotos => [...fotos, ...archivos.map(archivo => ({
      archivo, ruta: '', vista: URL.createObjectURL(archivo)
    }))]);
    this.emitir();
  }

  protected quitar(indice: number): void {
    const fotos = [...this.fotos()];
    const [foto] = fotos.splice(indice, 1);
    if (foto.archivo) URL.revokeObjectURL(foto.vista);
    const principal = this.principal();
    this.principal.set(indice === principal ? 0 : indice < principal ? principal - 1 : principal);
    this.fotos.set(fotos);
    this.emitir();
  }

  protected elegirPrincipal(indice: number): void {
    this.principal.set(indice);
    this.emitir();
  }

  private emitir(): void {
    const fotos = this.fotos();
    const ordenadas = [...fotos.filter(f => !f.archivo), ...fotos.filter(f => f.archivo)];
    this.cambio.emit({
      archivos: fotos.flatMap(f => f.archivo ? [f.archivo] : []),
      rutas: fotos.filter(f => !f.archivo).map(f => f.ruta),
      indicePrincipal: Math.max(0, ordenadas.indexOf(fotos[this.principal()]))
    });
  }

  protected async abrirCamara(): Promise<void> {
    const turno = ++this.turnoCamara;
    this.error.set('');
    this.esperando.set(true);
    this.camaraAbierta.set(true);
    try {
      if (!navigator.mediaDevices?.getUserMedia) throw new Error('Sin cámara');
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
      if (turno !== this.turnoCamara) {
        stream.getTracks().forEach(track => track.stop());
        return;
      }
      this.stream = stream;
      if (!this.video) throw new Error('Sin visor');
      this.video.nativeElement.srcObject = stream;
      await this.video.nativeElement.play();
    } catch {
      if (turno === this.turnoCamara) {
        this.cerrarCamara();
        this.error.set('No se pudo abrir la cámara. Revisa el permiso y usa una conexión HTTPS o localhost.');
      }
    } finally {
      if (turno === this.turnoCamara) this.esperando.set(false);
    }
  }

  protected capturar(): void {
    const video = this.video?.nativeElement;
    if (!video?.videoWidth || this.capturando()) return;
    const turno = this.turnoCamara;
    this.capturando.set(true);
    const canvas = document.createElement('canvas');
    const escala = Math.min(1, 1600 / Math.max(video.videoWidth, video.videoHeight));
    canvas.width = Math.round(video.videoWidth * escala);
    canvas.height = Math.round(video.videoHeight * escala);
    canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height);
    canvas.toBlob(blob => {
      this.capturando.set(false);
      if (turno !== this.turnoCamara) return;
      if (!blob) {
        this.error.set('No se pudo capturar la foto. Inténtalo nuevamente.');
        return;
      }
      this.agregar([new File([blob], `producto-${Date.now()}.jpg`, { type: 'image/jpeg' })]);
      this.cerrarCamara();
    }, 'image/jpeg', .9);
  }

  protected cerrarCamara(): void {
    this.turnoCamara++;
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = undefined;
    if (this.video) this.video.nativeElement.srcObject = null;
    this.camaraAbierta.set(false);
    this.esperando.set(false);
  }

  private liberarFotos(): void {
    this.fotos().filter(f => f.archivo).forEach(f => URL.revokeObjectURL(f.vista));
  }

  ngOnDestroy(): void {
    this.cerrarCamara();
    this.liberarFotos();
  }
}
