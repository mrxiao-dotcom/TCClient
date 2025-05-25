-- 创建条件单表
CREATE TABLE conditional_orders (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    account_id BIGINT NOT NULL COMMENT '关联账户ID',
    symbol VARCHAR(20) NOT NULL COMMENT '交易对符号',
    direction VARCHAR(10) NOT NULL COMMENT '交易方向: BUY/SELL',
    condition_type VARCHAR(20) NOT NULL COMMENT '条件类型: BREAK_UP(向上突破)/BREAK_DOWN(向下突破)',
    trigger_price DECIMAL(20,8) NOT NULL COMMENT '触发价格',
    quantity DECIMAL(20,8) NOT NULL COMMENT '数量',
    leverage INT NOT NULL DEFAULT 10 COMMENT '杠杆倍数',
    stop_loss_price DECIMAL(20,8) COMMENT '止损价格',
    status VARCHAR(20) NOT NULL DEFAULT 'WAITING' COMMENT '状态: WAITING(等待触发)/TRIGGERED(已触发)/EXECUTED(已执行)/CANCELLED(已取消)/FAILED(执行失败)',
    execution_order_id VARCHAR(50) COMMENT '执行后的订单ID',
    error_message TEXT COMMENT '错误信息',
    create_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    trigger_time DATETIME COMMENT '触发时间',
    execution_time DATETIME COMMENT '执行时间',
    update_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    FOREIGN KEY (account_id) REFERENCES trading_accounts(id) ON DELETE CASCADE,
    INDEX idx_account_status (account_id, status),
    INDEX idx_symbol_status (symbol, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='条件单表'; 