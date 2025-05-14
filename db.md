CREATE TABLE accounts (
    id BIGINT(19) NOT NULL AUTO_INCREMENT,
    name VARCHAR(50) NOT NULL COMMENT '账户名称' COLLATE 'utf8mb4_0900_ai_ci',
    equity DECIMAL(20,2) NULL DEFAULT NULL COMMENT '当前权益',
    init_value DECIMAL(20,2) NULL DEFAULT NULL COMMENT '初始资金',
    single_order_risk DECIMAL(10,2) NULL DEFAULT '5000.00' COMMENT '单笔订单风险金',
    status VARCHAR(20) NULL DEFAULT 'active' COMMENT '账户状态：active-活跃, frozen-冻结, closed-关闭' COLLATE 'utf8mb4_0900_ai_ci',
    created_at DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP',
    updated_at DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP' ON UPDATE CURRENT_TIMESTAMP,
    max_total_risk DECIMAL(10,2) NULL DEFAULT '0.00' COMMENT '总风险金',
    PRIMARY KEY (id) USING BTREE,
    UNIQUE INDEX name (name) USING BTREE
)
COMMENT='账户基本信息表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=4
;

CREATE TABLE acct_info (
    acct_id INT(10) NOT NULL AUTO_INCREMENT,
    acct_name VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
    acct_date DATETIME NULL DEFAULT NULL,
    memo VARCHAR(255) NULL DEFAULT NULL COMMENT '显示名字' COLLATE 'gb2312_chinese_ci',
    apikey VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
    secretkey VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
    apipass VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
    state INT(10) NULL DEFAULT NULL,
    status INT(10) NULL DEFAULT NULL,
    email VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
    group_id INT(10) NULL DEFAULT NULL,
    sendflag INT(10) NULL DEFAULT NULL,
    PRIMARY KEY (acct_id) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=9995
;


CREATE TABLE simulation_orders (
    id BIGINT(19) NOT NULL AUTO_INCREMENT,
    order_id VARCHAR(50) NOT NULL COMMENT '订单UUID' COLLATE 'utf8mb4_0900_ai_ci',
    account_id BIGINT(19) NOT NULL COMMENT '关联账户ID',
    contract VARCHAR(20) NOT NULL COMMENT '合约名称' COLLATE 'utf8mb4_0900_ai_ci',
    contract_size DECIMAL(20,8) NOT NULL COMMENT '合约面值',
    direction VARCHAR(10) NOT NULL COMMENT '方向：buy-买入, sell-卖出' COLLATE 'utf8mb4_0900_ai_ci',
    quantity INT(10) NOT NULL COMMENT '持仓数量',
    entry_price DECIMAL(20,4) NOT NULL COMMENT '开仓价格',
    initial_stop_loss DECIMAL(20,4) NOT NULL COMMENT '止损价格',
    current_stop_loss DECIMAL(20,4) NOT NULL COMMENT '当前止损价格',
    highest_price DECIMAL(20,4) NULL DEFAULT NULL COMMENT '订单期间最高价格（用于回撤策略）',
    max_floating_profit DECIMAL(20,2) NULL DEFAULT NULL COMMENT '最大浮动盈利（用于浮盈触发策略）',
    leverage INT(10) NOT NULL DEFAULT '10' COMMENT '杠杆倍数',
    margin DECIMAL(20,2) NOT NULL COMMENT '保证金',
    total_value DECIMAL(20,2) NOT NULL COMMENT '总市值',
    status VARCHAR(20) NOT NULL COMMENT '状态：open-持仓中, pending-挂单中, closed-已平仓' COLLATE 'utf8mb4_0900_ai_ci',
    open_time DATETIME NOT NULL COMMENT '开仓时间',
    close_time DATETIME NULL DEFAULT NULL COMMENT '平仓时间',
    close_price DECIMAL(20,4) NULL DEFAULT NULL COMMENT '平仓价格',
    realized_profit DECIMAL(20,2) NULL DEFAULT NULL COMMENT '已实现盈亏',
    close_type VARCHAR(20) NULL DEFAULT NULL COMMENT '平仓类型：take_profit_fixed-固定价格止盈, take_profit_drawdown-回撤止盈, take_profit_trigger-浮盈触发止盈, take_profit_breakeven-保本止盈, stop_loss-止损, manual-手动' COLLATE 'utf8mb4_0900_ai_ci',
    real_profit DECIMAL(20,2) NULL DEFAULT NULL COMMENT '实盈（止损价与开仓价的盈亏）',
    floating_pnl DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '浮动盈亏',
    current_price DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '当前价格',
    last_update_time DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP' COMMENT '最后更新时间',
    PRIMARY KEY (id) USING BTREE,
    UNIQUE INDEX uk_order_id (order_id) USING BTREE,
    INDEX idx_account_status (account_id, status) USING BTREE,
    CONSTRAINT fk_simulation_orders_account FOREIGN KEY (account_id) REFERENCES accounts (id) ON UPDATE NO ACTION ON DELETE NO ACTION
)
COMMENT='模拟交易订单表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=17
;



CREATE TABLE account_risk_monitor (
    id BIGINT(19) NOT NULL AUTO_INCREMENT,
    account_id BIGINT(19) NOT NULL COMMENT '关联账户ID',
    total_equity DECIMAL(20,2) NULL DEFAULT NULL COMMENT '总权益',
    used_margin DECIMAL(20,2) NULL DEFAULT NULL COMMENT '已用保证金',
    available_margin DECIMAL(20,2) NULL DEFAULT NULL COMMENT '可用保证金',
    risk_ratio DECIMAL(10,4) NULL DEFAULT NULL COMMENT '风险度（总市值/总权益）',
    position_count INT(10) NULL DEFAULT NULL COMMENT '当前持仓数量',
    created_at DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP',
    available_risk DECIMAL(20,2) NULL DEFAULT NULL COMMENT '可用风险金',
    used_risk DECIMAL(20,2) NULL DEFAULT NULL COMMENT '已用风险金',
    updated_at TIMESTAMP NULL DEFAULT 'CURRENT_TIMESTAMP' ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (id) USING BTREE,
    INDEX idx_account_risk_monitor_account_id (account_id) USING BTREE
)
COMMENT='账户风险度监控表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=9
;

-- 用户表
CREATE TABLE users (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    username VARCHAR(50) NOT NULL UNIQUE,
    password VARCHAR(100) NOT NULL COMMENT '密码（已废弃，使用password_hash）',
    password_hash VARCHAR(100) NOT NULL COMMENT '密码哈希',
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    update_time DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- 用户账户关联表
CREATE TABLE user_accounts (
    user_id BIGINT NOT NULL,
    account_id BIGINT NOT NULL,
    is_default TINYINT(1) DEFAULT 0,  -- 是否为默认账户
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, account_id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (account_id) REFERENCES accounts(id)
);

-- 机会数据表
CREATE TABLE price_range_20d (
        id BIGINT(19) NOT NULL AUTO_INCREMENT,
        symbol VARCHAR(50) NOT NULL COLLATE 'gb2312_chinese_ci',
        high_price_20d DECIMAL(20,8) NULL DEFAULT NULL,
        low_price_20d DECIMAL(20,8) NULL DEFAULT NULL,
        last_price DECIMAL(20,8) NULL DEFAULT NULL,
        amplitude DECIMAL(10,4) NULL DEFAULT NULL,
        position_ratio DECIMAL(10,4) NULL DEFAULT NULL,
        update_date DATE NOT NULL,
        update_time DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP' ON UPDATE CURRENT_TIMESTAMP,
        volume_24h DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '24小时成交量',
        open_price DECIMAL(20,8) NOT NULL DEFAULT '0.00000000' COMMENT '今日开盘价',
        daily_change DECIMAL(10,4) NOT NULL DEFAULT '0.0000' COMMENT '当日涨幅（%）',
        PRIMARY KEY (id) USING BTREE,
        UNIQUE INDEX uk_symbol_date (symbol, update_date) USING BTREE,
        UNIQUE INDEX uk_symbol (symbol) USING BTREE,
        INDEX idx_symbol (symbol) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=905986
;


-- 日线数据表
CREATE TABLE kline_data (
        id INT(10) NOT NULL AUTO_INCREMENT,
        symbol VARCHAR(20) NOT NULL COLLATE 'utf8mb4_0900_ai_ci',
        date DATETIME NOT NULL,
        open DECIMAL(20,8) NOT NULL,
        high DECIMAL(20,8) NOT NULL,
        low DECIMAL(20,8) NOT NULL,
        close DECIMAL(20,8) NOT NULL,
        volume DECIMAL(30,8) NOT NULL,
        created_at TIMESTAMP NULL DEFAULT 'CURRENT_TIMESTAMP',
        PRIMARY KEY (id) USING BTREE,
        INDEX idx_symbol_date (symbol, date) USING BTREE
)
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=186222
;