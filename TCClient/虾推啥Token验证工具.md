# 虾推啥Token验证工具 🔍

## 📋 你的Token信息
- **Token**: `BQGgzRQfJhEgTt7A7WQCERiVi`
- **长度**: 25个字符
- **格式**: 字母数字混合

## ✅ Token格式检查

### 1. 基本格式验证
- ✅ 不包含URL前缀
- ✅ 不包含特殊字符
- ✅ 长度合理（20-30字符）
- ✅ 字母数字混合

### 2. 常见Token格式对比
```
正确格式示例：
✅ XT123456789          (以XT开头)
✅ ABC123DEF456         (纯字母数字)
✅ BQGgzRQfJhEgTt7A7WQCERiVi  (你的Token)

错误格式示例：
❌ https://xtuis.cn/XT123456789
❌ xtuis.cn/XT123456789
❌ /XT123456789
```

## 🧪 手动测试方法

### 方法1：浏览器测试
在浏览器中访问以下URL：
```
https://xtuis.cn/BQGgzRQfJhEgTt7A7WQCERiVi?text=测试消息
```

### 方法2：PowerShell测试
```powershell
# 在PowerShell中运行
Invoke-RestMethod "https://xtuis.cn/BQGgzRQfJhEgTt7A7WQCERiVi?text=测试消息"
```

### 方法3：curl测试
```bash
curl "https://xtuis.cn/BQGgzRQfJhEgTt7A7WQCERiVi?text=测试消息"
```

## 🔧 可能的解决方案

### 1. Token来源确认
- 确认Token是从 https://xtuis.cn/ 官网获取
- 检查是否有多个Token，尝试其他Token
- 确认Token是否已过期或被禁用

### 2. 重新获取Token
1. 访问 https://xtuis.cn/
2. 登录你的账号
3. 查找"我的Token"或类似页面
4. 复制新的Token（只复制Token部分）

### 3. 检查账号状态
- 确认虾推啥账号是否正常
- 检查是否有使用限制
- 查看是否需要验证或激活

## 📞 联系虾推啥客服

如果以上方法都无效，建议联系虾推啥客服：
- 提供你的Token: `BQGgzRQfJhEgTt7A7WQCERiVi`
- 说明遇到404错误
- 询问Token状态和使用方法

## 🎯 下一步操作建议

1. **立即测试**: 用浏览器访问上面的测试URL
2. **检查结果**: 
   - 如果收到推送 → Token正确，可能是应用问题
   - 如果没收到推送 → Token可能有问题
3. **根据结果**: 
   - Token正确 → 检查应用网络设置
   - Token错误 → 重新获取Token

---

💡 **提示**: 最可靠的测试方法是直接用浏览器访问测试URL，这样可以排除应用本身的问题。 