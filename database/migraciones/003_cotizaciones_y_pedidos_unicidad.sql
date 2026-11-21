-- Migración para bases existentes: cotizaciones y unicidad en pedidos.
-- Las bases nuevas ya obtienen estas tablas desde database/schema.sql.

USE optica_db;

CREATE TABLE IF NOT EXISTS cotizaciones (
    id_cotizacion INT AUTO_INCREMENT PRIMARY KEY,
    rut VARCHAR(12) NOT NULL,
    fecha DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    fecha_entrega_estimada DATE NULL,
    total DECIMAL(10,2) NOT NULL DEFAULT 0,
    productos TEXT NULL,
    servicios TEXT NULL,
    anotaciones TEXT NULL,
    estado ENUM('Pendiente', 'Convertida', 'Vencida', 'Cancelada') NOT NULL DEFAULT 'Pendiente',
    fecha_creacion DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_cotizaciones_cliente
        FOREIGN KEY (rut) REFERENCES clientes(rut)
        ON DELETE RESTRICT
        ON UPDATE CASCADE,
    INDEX idx_cotizaciones_rut_estado (rut, estado)
);

-- Agregar id_cotizacion con restricción UNIQUE a pedidos si no existe
SET @columna_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND COLUMN_NAME = 'id_cotizacion'
);

SET @sql_add_columna = IF(
    @columna_existe = 0,
    'ALTER TABLE pedidos ADD COLUMN id_cotizacion INT NULL, ADD CONSTRAINT fk_pedidos_cotizacion FOREIGN KEY (id_cotizacion) REFERENCES cotizaciones(id_cotizacion) ON DELETE RESTRICT, ADD CONSTRAINT uq_pedidos_cotizacion UNIQUE (id_cotizacion);',
    'SELECT "Columna id_cotizacion ya existe en pedidos";'
);

PREPARE stmt FROM @sql_add_columna;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
