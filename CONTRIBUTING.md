# 贡献指南

1. 先阅读 `AUTHORS.md`、`THIRD_PARTY_NOTICES.md` 和 `COMMERCIAL_USE.md`。
2. 不提交闭源泄露、反编译专有代码、账号数据、规避检测教程或无许可证素材。
3. 新功能应尽量扩展现有模块/设置模型，不为三套 UI 复制三份逻辑。
4. 内存读取必须保持只读；新增字段解析要有失败路径和诊断。
5. 自动输入必须有焦点、界面状态和用户开关门控。
6. 新增来源时同步更新第三方声明与参考文档。
7. Pull Request 应说明：问题、实现、测试、兼容性和截图/视频。

提交前运行：

```powershell
dotnet build DesktopPet.csproj -c Release
```
