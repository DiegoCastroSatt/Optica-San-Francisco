-- Migración para bases existentes: token de idempotencia para pedidos de cotización.
-- Las bases nuevas ya obtienen esta columna desde database/schema.sql.

USE optica_db;

-- 1. Agregar columna token_cotizacion si no existe
SET @col_token_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND COLUMN_NAME = 'token_cotizacion'
);

SET @sql_add_token = IF(
    @col_token_existe = 0,
    'ALTER TABLE pedidos ADD COLUMN token_cotizacion VARCHAR(36) NULL;',
    'SELECT "Columna token_cotizacion ya existe en pedidos";'
);

PREPARE stmt_token FROM @sql_add_token;
EXECUTE stmt_token;
DEALLOCATE PREPARE stmt_token;

-- 2. Asegurar que la restricción UNIQUE se agregue aunque la columna ya existiera previamente
SET @uq_token_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.STATISTICS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND INDEX_NAME = 'uq_pedidos_token_cotizacion'
);

SET @sql_add_uq = IF(
    @uq_token_existe = 0,
    'ALTER TABLE pedidos ADD CONSTRAINT uq_pedidos_token_cotizacion UNIQUE (token_cotizacion);',
    'SELECT "Restriccion UNIQUE uq_pedidos_token_cotizacion ya existe en pedidos";'
);

PREPARE stmt_uq FROM @sql_add_uq;
EXECUTE stmt_uq;
DEALLOCATE PREPARE stmt_uq;
