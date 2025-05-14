-- 用户表
CREATE TABLE `users` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`username` VARCHAR(50) NOT NULL COMMENT '用户名' COLLATE 'utf8mb4_0900_ai_ci',
	`password_hash` VARCHAR(100) NOT NULL COMMENT '密码哈希' COLLATE 'utf8mb4_0900_ai_ci',
	`email` VARCHAR(100) NULL DEFAULT NULL COMMENT '邮箱' COLLATE 'utf8mb4_0900_ai_ci',
	`last_login_time` DATETIME NULL DEFAULT NULL COMMENT '最后登录时间',
	`status` TINYINT(1) NULL DEFAULT '1' COMMENT '状态：1-启用，0-禁用',
	`create_time` DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP',
	`update_time` DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP' ON UPDATE CURRENT_TIMESTAMP,
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `username` (`username`) USING BTREE
)
COMMENT='用户表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
;


-- 交易账户表（整合原accounts和acct_info表）
CREATE TABLE trading_accounts (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    account_name VARCHAR(50) NOT NULL COMMENT '账户名称',
    binance_account_id VARCHAR(50) NOT NULL COMMENT '币安账户ID',
    api_key VARCHAR(255) NOT NULL COMMENT 'API Key',
    api_secret VARCHAR(255) NOT NULL COMMENT 'API Secret',
    api_passphrase VARCHAR(255) NULL COMMENT 'API Passphrase（如果需要）',
    equity DECIMAL(20,2) DEFAULT 0.00 COMMENT '当前权益',
    initial_equity DECIMAL(20,2) DEFAULT 0.00 COMMENT '初始资金',
    opportunity_count INT DEFAULT 10 COMMENT '机会次数（用于计算单笔风险）',
    status TINYINT(1) DEFAULT 1 COMMENT '状态：1-启用，0-禁用',
    is_active TINYINT(1) DEFAULT 0 COMMENT '是否当前激活账户',
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    update_time DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_account_name (account_name),
    UNIQUE KEY uk_binance_account_id (binance_account_id)
) COMMENT='交易账户表';

-- 用户账户关联表
CREATE TABLE user_trading_accounts (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    user_id BIGINT NOT NULL COMMENT '用户ID',
    account_id BIGINT NOT NULL COMMENT '交易账户ID',
    is_default TINYINT(1) DEFAULT 0 COMMENT '是否为默认账户',
    last_used_time DATETIME NULL COMMENT '最后使用时间',
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    update_time DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (account_id) REFERENCES trading_accounts(id) ON DELETE CASCADE,
    UNIQUE KEY uk_user_account (user_id, account_id)
) COMMENT='用户账户关联表';

-- 账户风险监控表（优化原account_risk_monitor表）
CREATE TABLE account_risk_monitor (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    account_id BIGINT NOT NULL COMMENT '交易账户ID',
    total_equity DECIMAL(20,2) DEFAULT 0.00 COMMENT '总权益',
    used_margin DECIMAL(20,2) DEFAULT 0.00 COMMENT '已用保证金',
    available_margin DECIMAL(20,2) DEFAULT 0.00 COMMENT '可用保证金',
    risk_ratio DECIMAL(10,4) DEFAULT 0.0000 COMMENT '风险度（总市值/总权益）',
    position_count INT DEFAULT 0 COMMENT '当前持仓数量',
    single_order_risk DECIMAL(20,2) DEFAULT 0.00 COMMENT '单笔订单风险金额（equity/opportunity_count）',
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    update_time DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (account_id) REFERENCES trading_accounts(id) ON DELETE CASCADE
) COMMENT='账户风险监控表';


CREATE TABLE `realtime_ranking_history` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`symbol` VARCHAR(20) NOT NULL COMMENT '交易对' COLLATE 'utf8mb4_0900_ai_ci',
	`last_price` DECIMAL(20,10) NOT NULL COMMENT '最新价格',
	`change_rate` DECIMAL(10,4) NOT NULL COMMENT '涨跌幅',
	`amount_24h` DECIMAL(30,8) NOT NULL COMMENT '24小时成交额',
	`ranking` INT(10) NOT NULL COMMENT '排名',
	`timestamp` BIGINT(19) NOT NULL COMMENT '时间戳',
	`record_time` DATETIME NOT NULL COMMENT '记录时间',
	`created_at` TIMESTAMP NULL DEFAULT 'CURRENT_TIMESTAMP',
	`volume` DECIMAL(30,8) NOT NULL COMMENT '24小时成交量(USDT)',
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `idx_timestamp_ranking` (`timestamp`, `ranking`) USING BTREE,
	INDEX `idx_record_time` (`record_time`) USING BTREE,
	INDEX `idx_timestamp` (`timestamp`) USING BTREE,
	INDEX `idx_volume` (`volume`) USING BTREE
)
COMMENT='实时排行榜历史记录'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=45141
;
CREATE TABLE `simulation_orders` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`order_id` VARCHAR(50) NOT NULL COMMENT '订单UUID' COLLATE 'utf8mb4_0900_ai_ci',
	`account_id` BIGINT(19) NOT NULL COMMENT '关联账户ID',
	`contract` VARCHAR(20) NOT NULL COMMENT '合约名称' COLLATE 'utf8mb4_0900_ai_ci',
	`contract_size` DECIMAL(20,8) NOT NULL COMMENT '合约面值',
	`direction` VARCHAR(10) NOT NULL COMMENT '方向：buy-买入, sell-卖出' COLLATE 'utf8mb4_0900_ai_ci',
	`quantity` INT(10) NOT NULL COMMENT '持仓数量',
	`entry_price` DECIMAL(20,8) NOT NULL COMMENT '开仓价格',
	`initial_stop_loss` DECIMAL(20,8) NOT NULL COMMENT '止损价格',
	`current_stop_loss` DECIMAL(20,8) NOT NULL COMMENT '当前止损价格',
	`highest_price` DECIMAL(20,8) NULL DEFAULT NULL COMMENT '订单期间最高价格（用于回撤策略）',
	`max_floating_profit` DECIMAL(20,2) NULL DEFAULT NULL COMMENT '最大浮动盈利（用于浮盈触发策略）',
	`leverage` INT(10) NOT NULL DEFAULT '10' COMMENT '杠杆倍数',
	`margin` DECIMAL(20,2) NOT NULL COMMENT '保证金',
	`total_value` DECIMAL(20,2) NOT NULL COMMENT '总市值',
	`status` VARCHAR(20) NOT NULL COMMENT '状态：open-持仓中, pending-挂单中, closed-已平仓' COLLATE 'utf8mb4_0900_ai_ci',
	`open_time` DATETIME NOT NULL COMMENT '开仓时间',
	`close_time` DATETIME NULL DEFAULT NULL COMMENT '平仓时间',
	`close_price` DECIMAL(20,8) NULL DEFAULT NULL COMMENT '平仓价格',
	`realized_profit` DECIMAL(20,2) NULL DEFAULT NULL COMMENT '已实现盈亏',
	`close_type` VARCHAR(20) NULL DEFAULT NULL COMMENT '平仓类型：take_profit_fixed-固定价格止盈, take_profit_drawdown-回撤止盈, take_profit_trigger-浮盈触发止盈, take_profit_breakeven-保本止盈, stop_loss-止损, manual-手动' COLLATE 'utf8mb4_0900_ai_ci',
	`real_profit` DECIMAL(20,2) NULL DEFAULT NULL COMMENT '实盈（止损价与开仓价的盈亏）',
	`floating_pnl` DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '浮动盈亏',
	`current_price` DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '当前价格',
	`last_update_time` DATETIME NULL COMMENT '最后更新时间',
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `uk_order_id` (`order_id`) USING BTREE,
	INDEX `idx_account_status` (`account_id`, `status`) USING BTREE,
	CONSTRAINT `fk_simulation_orders_account` FOREIGN KEY (`account_id`) REFERENCES `trading_accounts` (`id`) ON UPDATE NO ACTION ON DELETE NO ACTION
)
COMMENT='模拟交易订单表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=85
;


-- 推仓信息表
CREATE TABLE position_push_info (
    id BIGINT(19) NOT NULL AUTO_INCREMENT,
    contract VARCHAR(20) NOT NULL COMMENT '合约名称' COLLATE 'utf8mb4_0900_ai_ci',
    account_id BIGINT(19) NOT NULL COMMENT '关联账户ID',
    create_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '推仓创建时间',
    status VARCHAR(10) NOT NULL DEFAULT 'open' COMMENT '推仓状态：open-持仓中, closed-已完结' COLLATE 'utf8mb4_0900_ai_ci',
    close_time DATETIME NULL DEFAULT NULL COMMENT '推仓完结时间',
    PRIMARY KEY (id) USING BTREE,
    INDEX idx_account_contract (account_id, contract, status)
) COMMENT='推仓信息表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB;

-- 推仓与订单关联表
CREATE TABLE position_push_order_rel (
    id BIGINT(19) NOT NULL AUTO_INCREMENT,
    push_id BIGINT(19) NOT NULL COMMENT '推仓ID',
    order_id BIGINT(19) NOT NULL COMMENT '订单ID',
    create_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id) USING BTREE,
    UNIQUE KEY uk_push_order (push_id, order_id),
    FOREIGN KEY (push_id) REFERENCES position_push_info(id) ON DELETE CASCADE,
    FOREIGN KEY (order_id) REFERENCES simulation_orders(id) ON DELETE CASCADE
) COMMENT='推仓与订单关联表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB;

