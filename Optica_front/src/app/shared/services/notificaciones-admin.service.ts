import { Injectable, computed, signal } from '@angular/core';

export type TipoNotificacionAdmin = 'informacion' | 'exito' | 'advertencia' | 'error';

export interface NotificacionAdmin {
  id: number;
  titulo: string;
  mensaje: string;
  tipo: TipoNotificacionAdmin;
  persistente?: boolean;
  idProductoStockBajo?: number;
}

export interface NuevaNotificacionAdmin {
  titulo: string;
  mensaje: string;
  tipo?: TipoNotificacionAdmin;
}

@Injectable({ providedIn: 'root' })
export class NotificacionesAdminService {
  private readonly elementos = signal<NotificacionAdmin[]>([]);
  private siguienteId = 1;

  readonly notificaciones = this.elementos.asReadonly();
  readonly cantidad = computed(() => this.elementos().length);

  agregar(notificacion: NuevaNotificacionAdmin): number {
    const id = this.siguienteId++;
    this.elementos.update(elementos => [
      { ...notificacion, tipo: notificacion.tipo ?? 'informacion', id },
      ...elementos
    ]);
    return id;
  }

  actualizarAlertasStockBajo(
    productos: readonly { idProducto: number; nombre: string; stock: number; stockMinimo: number }[]
  ): void {
    const otrasNotificaciones = this.elementos().filter(
      notificacion => notificacion.idProductoStockBajo === undefined
    );
    const alertasStock = productos.map(notificacion => ({
      id: -notificacion.idProducto,
      titulo: `Bajo stock: ${notificacion.nombre}`,
      mensaje: `Quedan ${notificacion.stock}; mínimo ${notificacion.stockMinimo}.`,
      tipo: 'advertencia' as const,
      persistente: true,
      idProductoStockBajo: notificacion.idProducto
    }));

    this.elementos.set([...alertasStock, ...otrasNotificaciones]);
  }

  eliminar(id: number): void {
    this.elementos.update(elementos => elementos.filter(elemento =>
      elemento.id !== id || elemento.persistente
    ));
  }
}
