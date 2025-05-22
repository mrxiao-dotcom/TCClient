-- 用户表
CREATE TABLE users (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    username VARCHAR(50) NOT NULL UNIQUE COMMENT '用户名',
    password VARCHAR(100) NOT NULL COMMENT '密码（加密存储）',
    email VARCHAR(100) NULL COMMENT '邮箱',
    last_login_time DATETIME NULL COMMENT '最后登录时间',
    status TINYINT(1) DEFAULT 1 COMMENT '状态：1-启用，0-禁用',
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    update_time DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) COMMENT='用户表';

-- 交易账户表（整合原accounts和acct_info表）
CREATE TABLE trading_accounts (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    account_name VARCHAR(50) NOT NULL COMMENT '账户名称',
    type VARCHAR(20) NOT NULL DEFAULT '模拟账户' COMMENT '账户类型：模拟账户/实盘账户',
    binance_account_id VARCHAR(50) NOT NULL COMMENT '币安账户ID',
    api_key VARCHAR(255) NOT NULL COMMENT 'API Key',
    api_secret VARCHAR(255) NOT NULL COMMENT 'API Secret',
    api_passphrase VARCHAR(255) NULL COMMENT 'API Passphrase（如果需要）',
    equity DECIMAL(20,2) DEFAULT 0.00 COMMENT '当前权益',
    initial_equity DECIMAL(20,2) DEFAULT 0.00 COMMENT '初始资金',
    opportunity_count INT DEFAULT 10 COMMENT '机会次数（用于计算单笔风险）',
    status TINYINT(1) DEFAULT 1 COMMENT '状态：1-启用，0-禁用',
    is_active TINYINT(1) DEFAULT 0 COMMENT '是否当前激活账户',
    description TEXT NULL COMMENT '备注说明',
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