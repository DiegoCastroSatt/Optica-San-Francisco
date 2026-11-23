import { Injectable, computed, signal } from '@angular/core';

export type TipoNotificacionAdmin = 'informacion' | 'exito' | 'advertencia' | 'error';

export interface NotificacionAdmin {
  id: number;
  titulo: string;
  mensaje: string;
  tipo: TipoNotificacionAdmin;
  persistente?: boolean;
  idProductoStockBajo?: number;
  idAvisoReposicion?: number;
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

  actualizarSolicitudesReposicion(
    avisos: readonly {
      idAviso: number;
      nombreCliente: string;
      nombreProducto: string;
      estado: string;
    }[]
  ): void {
    const notificacionesActuales = this.elementos();
    const otrasNotificaciones = notificacionesActuales.filter(
      notificacion => notificacion.idAvisoReposicion === undefined
    );
    const notificacionesReposicion = avisos
      .filter(aviso => aviso.estado === 'Abierta')
      .map(aviso => {
        const notificacionExistente = notificacionesActuales.find(
          notificacion => notificacion.idAvisoReposicion === aviso.idAviso
        );

        return {
          id: notificacionExistente?.id ?? this.siguienteId++,
          idAvisoReposicion: aviso.idAviso,
          titulo: `Solicitud de reposición: ${aviso.nombreProducto}`,
          mensaje: `${aviso.nombreCliente} solicitó un aviso cuando el producto vuelva a estar disponible.`,
          tipo: 'informacion' as const,
          persistente: true
        };
      });

    this.elementos.set([...notificacionesReposicion, ...otrasNotificaciones]);
  }

  eliminar(id: number): void {
    this.elementos.update(elementos => elementos.filter(elemento =>
      elemento.id !== id || elemento.persistente
    ));
  }
}
